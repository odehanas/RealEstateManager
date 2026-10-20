using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Applies the numbered scripts in SQL/ (01_..., 02_..., ...) to the database when the
    /// application starts, so a new build brings its own database changes with it instead of
    /// someone pasting them into the hosting provider's SQL console.
    ///
    /// The scripts are embedded in the build (see RealEstateManager.csproj). Each one runs at
    /// most once: SchemaScriptsApplied records every script that has been applied. A script
    /// runs one numbered statement at a time ("-- 1. ...", "-- 2. ..."), all inside one
    /// transaction, and is recorded only when every statement succeeded.
    ///
    /// On any problem the runner logs it, keeps the details for the owner's Database Updates
    /// page, and stops applying further scripts. It never throws: like the seeders, a failure
    /// here must not stop the site from starting (HTTP 500.30 leaves no UI to diagnose from).
    /// </summary>
    public sealed class SchemaScriptRunner
    {
        public const string TrackingTable = "SchemaScriptsApplied";

        // Header directives a script may carry before its first numbered statement.
        public const string AllowRiskyDirective = "-- @allow-risky-statements";
        public const string NoTransactionDirective = "-- @no-transaction";

        private const string ResourcePrefix = "SchemaScripts.";

        // Under the hosting's start-up time limit: a statement that runs longer is cancelled,
        // its script rolled back, and the site starts without it.
        private const int StatementTimeoutSeconds = 90;

        /// <summary>
        /// Scripts 01-21 were applied by hand in the site4now console before this runner
        /// existed. A database with no SchemaScriptsApplied table is judged by the objects
        /// these scripts created: the highest script whose object - and every earlier one's -
        /// is present, and all scripts up to it, are recorded as already applied.
        /// Never needs extending: once the table exists, it is the record.
        /// </summary>
        private static readonly (int Number, string Table, string? Column)[] BaselineMarkers =
        {
            (14, "ApprovalRequests", null),
            (15, "AppUserCompanies", "IsCompanyAdmin"),
            (16, "OwnerSettlements", null),
            (17, "OwnerCommissionInvoices", null),
            (18, "PropertyDocuments", null),
            (19, "DevelopmentProjects", null),
            (20, "DevelopmentTransfers", null),
            (21, "UnitSales", null),
        };

        private static readonly Regex ScriptNamePattern = new(@"^(\d+)_.+\.sql$", RegexOptions.IgnoreCase);
        private static readonly Regex StatementMarker = new(@"^\s*--\s*(\d+)\.(\s|$)");
        private static readonly Regex GoLine = new(@"^\s*GO(\s+\d+)?\s*;?\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        // ON DELETE CASCADE and friends describe a foreign key; they delete nothing now.
        private static readonly Regex ReferentialAction = new(
            @"\bON\s+(DELETE|UPDATE)\s+(CASCADE|NO\s+ACTION|SET\s+NULL|SET\s+DEFAULT)\b", RegexOptions.IgnoreCase);
        private static readonly Regex RiskyKeyword = new(
            @"\b(DROP|DELETE|TRUNCATE|UPDATE|MERGE|EXEC|EXECUTE)\b", RegexOptions.IgnoreCase);

        private readonly DatabaseConnectionOptions _connection;
        private readonly SchemaUpdateStatus _status;
        private readonly ILogger<SchemaScriptRunner> _logger;
        private readonly Assembly _assembly;

        public SchemaScriptRunner(
            DatabaseConnectionOptions connection,
            SchemaUpdateStatus status,
            ILogger<SchemaScriptRunner> logger)
        {
            _connection = connection;
            _status = status;
            _logger = logger;
            _assembly = typeof(SchemaScriptRunner).Assembly;
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            _status.Begin();
            try
            {
                await RunCoreAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database update failed before any script could be applied. The application will still start.");
                _status.Fail(null, null, null, ex.Message);
            }
        }

        private async Task RunCoreAsync(CancellationToken cancellationToken)
        {
            var scripts = LoadEmbeddedScripts();

            await using var connection = new SqlConnection(_connection.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            // Two copies of the site can overlap for a moment during a deployment. Only one
            // of them may apply scripts; the other waits, then finds nothing left to do.
            if (!await AcquireLockAsync(connection, cancellationToken))
            {
                _logger.LogWarning("Database update skipped: another copy of the application held the update lock for 60 seconds.");
                _status.Skip("Another copy of the application was applying updates at the same time. Restart the site to check again.");
                return;
            }

            if (!await TableExistsAsync(connection, TrackingTable, cancellationToken))
            {
                if (!await CreateTrackingTableWithBaselineAsync(connection, scripts, cancellationToken))
                    return;
            }

            var applied = await ReadAppliedNamesAsync(connection, cancellationToken);
            var pending = scripts.Where(s => !applied.Contains(s.Name)).ToList();
            _status.SetPending(pending.Select(s => s.Name));

            if (pending.Count == 0)
            {
                _logger.LogInformation("Database is up to date: all {Count} scripts are applied.", scripts.Count);
                _status.Complete();
                return;
            }

            foreach (var script in pending)
            {
                if (!await ApplyScriptAsync(connection, script, cancellationToken))
                    return; // Later scripts may depend on this one: stop here.
            }

            _status.Complete();
        }

        // ---- Scripts -------------------------------------------------------------------

        public sealed record EmbeddedScript(int Number, string Name, string Text);

        public IReadOnlyList<EmbeddedScript> LoadEmbeddedScripts()
        {
            var scripts = new List<EmbeddedScript>();
            foreach (var resource in _assembly.GetManifestResourceNames())
            {
                if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                    continue;

                var name = resource[ResourcePrefix.Length..];
                var match = ScriptNamePattern.Match(name);
                if (!match.Success)
                    continue;

                using var stream = _assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                scripts.Add(new EmbeddedScript(int.Parse(match.Groups[1].Value), name, reader.ReadToEnd()));
            }

            var duplicate = scripts.GroupBy(s => s.Number).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new InvalidOperationException(
                    $"Two scripts share the number {duplicate.Key}: {string.Join(" and ", duplicate.Select(s => s.Name))}. " +
                    "Renumber one of them; nothing was applied.");

            return scripts.OrderBy(s => s.Number).ToList();
        }

        public sealed record ParsedScript(
            IReadOnlyList<(int Number, string Sql)> Statements,
            bool AllowRisky,
            bool NoTransaction);

        /// <summary>
        /// Splits a script on its "-- N. description" lines. Text before the first one is the
        /// header, where directives live. A numbered section holding only comments (a note,
        /// not a statement) is skipped.
        /// </summary>
        public static ParsedScript Parse(string text)
        {
            var header = new StringBuilder();
            var statements = new List<(int Number, StringBuilder Sql)>();

            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                var marker = StatementMarker.Match(line);
                if (marker.Success)
                {
                    statements.Add((int.Parse(marker.Groups[1].Value), new StringBuilder()));
                    continue;
                }

                if (statements.Count == 0)
                    header.AppendLine(line);
                else
                    statements[^1].Sql.AppendLine(line);
            }

            var headerText = header.ToString();
            return new ParsedScript(
                statements
                    .Select(s => (s.Number, Sql: s.Sql.ToString().Trim()))
                    .Where(s => StripComments(s.Sql).Trim().Length > 0)
                    .ToList(),
                HasDirective(headerText, AllowRiskyDirective),
                HasDirective(headerText, NoTransactionDirective));
        }

        private static bool HasDirective(string header, string directive) =>
            header.Split('\n').Any(l => l.Trim().Equals(directive, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Why a statement may not run, or null when it may. Checked for every statement of a
        /// script before the first one runs, so a refused script changes nothing.
        /// </summary>
        public static string? RefusalReason(string sql, bool allowRisky)
        {
            var code = StripComments(sql);

            if (GoLine.IsMatch(code))
                return "it contains a GO line. Each numbered statement is sent on its own, so GO is not needed and SQL Server rejects it.";

            if (allowRisky)
                return null;

            var risky = RiskyKeyword.Match(ReferentialAction.Replace(code, " "));
            if (risky.Success)
                return $"it contains {risky.Value.ToUpperInvariant()}, which can remove or change existing data. " +
                       $"A script that genuinely needs it must say so with the line \"{AllowRiskyDirective}\" in its header.";

            return null;
        }

        /// <summary>Replaces -- and /* */ comments with spaces, leaving string literals intact.</summary>
        public static string StripComments(string sql)
        {
            var result = new StringBuilder(sql.Length);
            var i = 0;
            while (i < sql.Length)
            {
                var c = sql[i];
                var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

                if (c == '\'')
                {
                    // A string literal; '' inside it is an escaped quote.
                    var end = i + 1;
                    while (end < sql.Length && !(sql[end] == '\'' && (end + 1 >= sql.Length || sql[end + 1] != '\'')))
                        end += sql[end] == '\'' ? 2 : 1;
                    end = Math.Min(end + 1, sql.Length);
                    result.Append(sql, i, end - i);
                    i = end;
                }
                else if (c == '-' && next == '-')
                {
                    while (i < sql.Length && sql[i] != '\n')
                        i++;
                    result.Append(' ');
                }
                else if (c == '/' && next == '*')
                {
                    var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? sql.Length : end + 2;
                    result.Append(' ');
                }
                else
                {
                    result.Append(c);
                    i++;
                }
            }
            return result.ToString();
        }

        private async Task<bool> ApplyScriptAsync(SqlConnection connection, EmbeddedScript script, CancellationToken cancellationToken)
        {
            ParsedScript parsed;
            try
            {
                parsed = Parse(script.Text);
            }
            catch (Exception ex)
            {
                return Refuse(script, null, null, $"the script could not be read: {ex.Message}");
            }

            if (parsed.Statements.Count == 0)
                return Refuse(script, null, null,
                    "it has no numbered statements. Each statement must follow a line such as \"-- 1. What this does\".");

            foreach (var (number, sql) in parsed.Statements)
            {
                var reason = RefusalReason(sql, parsed.AllowRisky);
                if (reason != null)
                    return Refuse(script, number, sql, $"statement {number} was refused: {reason}");
            }

            _logger.LogInformation("Applying database script {Script} ({Count} statements).", script.Name, parsed.Statements.Count);

            await using var transaction = parsed.NoTransaction
                ? null
                : (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            int? current = null;
            string? currentSql = null;
            try
            {
                foreach (var (number, sql) in parsed.Statements)
                {
                    current = number;
                    currentSql = sql;
                    await using var command = new SqlCommand(sql, connection, transaction)
                    {
                        CommandTimeout = StatementTimeoutSeconds
                    };
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                current = null;
                currentSql = null;
                await using (var record = new SqlCommand(
                    $"INSERT INTO [{TrackingTable}] ([ScriptName], [AppliedAt], [Method], [Statements]) VALUES (@name, SYSUTCDATETIME(), N'Applied', @count);",
                    connection, transaction))
                {
                    record.Parameters.AddWithValue("@name", script.Name);
                    record.Parameters.AddWithValue("@count", parsed.Statements.Count);
                    await record.ExecuteNonQueryAsync(cancellationToken);
                }

                if (transaction != null)
                    await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                var rolledBack = false;
                if (transaction != null)
                {
                    try
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                        rolledBack = true;
                    }
                    catch (Exception rollbackError)
                    {
                        // SQL Server may already have rolled it back itself after a severe error.
                        _logger.LogWarning(rollbackError, "Rollback of {Script} reported an error.", script.Name);
                        rolledBack = true;
                    }
                }

                var where = current.HasValue ? $"statement {current}" : "recording the script as applied";
                var undone = rolledBack
                    ? "Nothing from this script was kept."
                    : "This script runs without a transaction, so statements before the failing one were kept.";
                _logger.LogError(ex, "Database script {Script} failed at {Where}. {Undone} No later scripts were applied.",
                    script.Name, where, undone);
                _status.Fail(script.Name, current, currentSql, $"{where} failed: {ex.Message} {undone}");
                return false;
            }

            _logger.LogInformation("Database script {Script} applied.", script.Name);
            _status.Applied(script.Name);
            return true;
        }

        private bool Refuse(EmbeddedScript script, int? statement, string? sql, string reason)
        {
            _logger.LogError("Database script {Script} was not applied: {Reason} No later scripts were applied.", script.Name, reason);
            _status.Fail(script.Name, statement, sql, $"Not applied: {reason}");
            return false;
        }

        // ---- Tracking table ------------------------------------------------------------

        private async Task<bool> CreateTrackingTableWithBaselineAsync(
            SqlConnection connection, IReadOnlyList<EmbeddedScript> scripts, CancellationToken cancellationToken)
        {
            var level = 0;
            foreach (var (number, table, column) in BaselineMarkers)
            {
                var present = column == null
                    ? await TableExistsAsync(connection, table, cancellationToken)
                    : await ColumnExistsAsync(connection, table, column, cancellationToken);
                if (!present)
                    break;
                level = number;
            }

            if (level == 0)
            {
                const string message =
                    "This database has no record of applied scripts, and it is either empty or older than script 14, " +
                    "so it cannot be told which scripts it already has. Nothing was applied. Bring it up to date by hand " +
                    "(or restore a current backup), then restart the site.";
                _logger.LogError("Database update stopped: {Message}", message);
                _status.Fail(null, null, null, message);
                return false;
            }

            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await using (var create = new SqlCommand(
                $@"CREATE TABLE [{TrackingTable}] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_{TrackingTable}] PRIMARY KEY,
    [ScriptName] nvarchar(200) NOT NULL CONSTRAINT [UQ_{TrackingTable}_ScriptName] UNIQUE,
    [AppliedAt] datetime2 NOT NULL,
    [Method] nvarchar(20) NOT NULL,
    [Statements] int NULL
);", connection, transaction))
            {
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            var baseline = scripts.Where(s => s.Number <= level).ToList();
            foreach (var script in baseline)
            {
                await using var insert = new SqlCommand(
                    $"INSERT INTO [{TrackingTable}] ([ScriptName], [AppliedAt], [Method], [Statements]) VALUES (@name, SYSUTCDATETIME(), N'Baseline', NULL);",
                    connection, transaction);
                insert.Parameters.AddWithValue("@name", script.Name);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Created {Table}. The database already contains the changes of scripts up to {Level}, so {Count} scripts were recorded as applied without running them.",
                TrackingTable, level, baseline.Count);
            _status.Baselined(level, baseline.Count);
            return true;
        }

        private static async Task<HashSet<string>> ReadAppliedNamesAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = new SqlCommand($"SELECT [ScriptName] FROM [{TrackingTable}];", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                names.Add(reader.GetString(0));
            return names;
        }

        public sealed record AppliedScript(string ScriptName, DateTime AppliedAt, string Method, int? Statements);

        /// <summary>The scripts recorded as applied, for the owner's page. Read-only.</summary>
        public async Task<IReadOnlyList<AppliedScript>?> ReadHistoryAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(_connection.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            if (!await TableExistsAsync(connection, TrackingTable, cancellationToken))
                return null;

            var rows = new List<AppliedScript>();
            await using var command = new SqlCommand(
                $"SELECT [ScriptName], [AppliedAt], [Method], [Statements] FROM [{TrackingTable}];", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new AppliedScript(
                    reader.GetString(0),
                    reader.GetDateTime(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3)));
            }

            return rows
                .OrderByDescending(r => ScriptNamePattern.Match(r.ScriptName) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0)
                .ToList();
        }

        // ---- Helpers ---------------------------------------------------------------------

        private static async Task<bool> AcquireLockAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand("sp_getapplock", connection)
            {
                CommandType = System.Data.CommandType.StoredProcedure,
                CommandTimeout = 70
            };
            command.Parameters.AddWithValue("@Resource", "RealEstateManager.SchemaScripts");
            command.Parameters.AddWithValue("@LockMode", "Exclusive");
            command.Parameters.AddWithValue("@LockOwner", "Session");
            command.Parameters.AddWithValue("@LockTimeout", 60_000);
            var result = command.Parameters.Add("@Result", System.Data.SqlDbType.Int);
            result.Direction = System.Data.ParameterDirection.ReturnValue;
            await command.ExecuteNonQueryAsync(cancellationToken);
            // 0 = granted, 1 = granted after waiting; negative = timed out or refused.
            // The lock is released when the connection closes.
            return (int)result.Value >= 0;
        }

        private static async Task<bool> TableExistsAsync(SqlConnection connection, string table, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@name, N'U') IS NULL THEN 0 ELSE 1 END;", connection);
            command.Parameters.AddWithValue("@name", $"[dbo].[{table}]");
            return (int)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
        }

        private static async Task<bool> ColumnExistsAsync(SqlConnection connection, string table, string column, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand("SELECT CASE WHEN COL_LENGTH(@table, @column) IS NULL THEN 0 ELSE 1 END;", connection);
            command.Parameters.AddWithValue("@table", $"[dbo].[{table}]");
            command.Parameters.AddWithValue("@column", column);
            return (int)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
        }
    }

    /// <summary>
    /// What the database update did at the last start-up, for the owner's page. Held in
    /// memory: a failure stops the run, so it is still here until the next restart retries.
    /// </summary>
    public sealed class SchemaUpdateStatus
    {
        private readonly object _gate = new();
        private readonly List<string> _appliedThisRun = new();
        private List<string> _pending = new();

        public DateTime? StartedAt { get; private set; }
        public DateTime? FinishedAt { get; private set; }
        public string Outcome { get; private set; } = "Not run";
        public string? BaselineNote { get; private set; }
        public string? FailedScript { get; private set; }
        public int? FailedStatement { get; private set; }
        public string? FailedSql { get; private set; }
        public string? FailureMessage { get; private set; }

        public bool HasFailed => FailureMessage != null;
        public IReadOnlyList<string> AppliedThisRun { get { lock (_gate) return _appliedThisRun.ToList(); } }
        public IReadOnlyList<string> Pending { get { lock (_gate) return _pending.ToList(); } }

        internal void Begin()
        {
            lock (_gate)
            {
                StartedAt = DateTime.UtcNow;
                FinishedAt = null;
                Outcome = "Running";
                _appliedThisRun.Clear();
                _pending.Clear();
                BaselineNote = FailedScript = FailedSql = FailureMessage = null;
                FailedStatement = null;
            }
        }

        internal void Baselined(int level, int count)
        {
            lock (_gate)
                BaselineNote = $"First run on this database: it already contained the changes of scripts up to {level:00}, " +
                               $"so {count} scripts were recorded as applied without running them again.";
        }

        internal void SetPending(IEnumerable<string> names)
        {
            lock (_gate) _pending = names.ToList();
        }

        internal void Applied(string name)
        {
            lock (_gate)
            {
                _appliedThisRun.Add(name);
                _pending.Remove(name);
            }
        }

        internal void Complete()
        {
            lock (_gate)
            {
                FinishedAt = DateTime.UtcNow;
                Outcome = _appliedThisRun.Count == 0 ? "Up to date" : "Updated";
            }
        }

        internal void Skip(string message)
        {
            lock (_gate)
            {
                FinishedAt = DateTime.UtcNow;
                Outcome = "Skipped";
                FailureMessage = message;
            }
        }

        internal void Fail(string? script, int? statement, string? sql, string message)
        {
            lock (_gate)
            {
                FinishedAt = DateTime.UtcNow;
                Outcome = "Failed";
                FailedScript = script;
                FailedStatement = statement;
                FailedSql = sql;
                FailureMessage = message;
            }
        }
    }
}
