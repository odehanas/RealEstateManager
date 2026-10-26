using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RealEstateManager.Data;
using RealEstateManager.Services.Assistant;

namespace RealEstateManager.Services.Reporting
{
    /// <summary>
    /// The Report Builder's engine. A report is a definition - a source (record type),
    /// columns, filters, an optional grouping with a total, a sort and a chart - run as
    /// LINQ over ApplicationDbContext, never SQL, so the company filter applies exactly
    /// as on the screens. Each source carries the permission of its own screen, so
    /// people only report on what they may already see.
    /// </summary>
    public class ReportEngine
    {
        public const int MaxDetailRows = 2000;
        private const int MaxGroupedInputRows = 100000;
        private const int MaxDepth = 2;

        private readonly ApplicationDbContext _db;
        private readonly PermissionService _permissions;

        public ReportEngine(ApplicationDbContext db, PermissionService permissions)
        {
            _db = db;
            _permissions = permissions;
        }

        // =====================================================================
        //  WHAT CAN BE REPORTED ON
        // =====================================================================

        private static readonly Dictionary<string, string> SourceLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Property"] = "Properties", ["Unit"] = "Units", ["Tenant"] = "Tenants", ["Vendor"] = "Vendors",
            ["Lease"] = "Contracts (leases)", ["CustomerInvoice"] = "Customer invoices", ["CustomerReceipt"] = "Customer receipts",
            ["CustomerCreditNote"] = "Credit notes", ["PdcReceipt"] = "Post-dated cheques", ["SecurityDeposit"] = "Security deposits",
            ["VendorBill"] = "Vendor bills", ["VendorPayment"] = "Vendor payments", ["JournalEntry"] = "Journal entries",
            ["JournalEntryLine"] = "Journal lines (ledger)", ["Account"] = "Chart of accounts", ["AccountingPeriod"] = "Accounting periods",
            ["FixedAsset"] = "Fixed assets", ["TenantServiceRequest"] = "Tenant service requests", ["ApprovalRequest"] = "Contract approvals",
            ["OwnerSettlement"] = "Owner settlements", ["UnitSale"] = "Unit sales", ["DevelopmentProject"] = "Development projects",
            ["Employee"] = "Employees", ["LeaveRequest"] = "Leave", ["EmployeeAdvance"] = "Salary advances",
            ["PayrollRun"] = "Payroll runs", ["PayrollRunLine"] = "Payslips (payroll lines)", ["AttendanceRecord"] = "Attendance",
            ["PaymentApproval"] = "Payment approvals",
            ["VendorBillLine"] = "Vendor bill lines (project costs, VAT)",
            ["DevelopmentTransfer"] = "Development completions (transfers out of WIP)"
        };

        public sealed record SourceInfo(string Key, string Label);

        /// <summary>A reportable field. Kind is text, number, date or bool.</summary>
        public sealed record FieldInfo(string Path, string Label, string Kind);

        public async Task<List<SourceInfo>> SourcesAsync(ClaimsPrincipal user)
        {
            var result = new List<SourceInfo>();
            foreach (var (key, screen) in AssistantQueryTools.Queryable)
            {
                if (EntityType(key) != null && await MayReadAsync(user, screen))
                    result.Add(new SourceInfo(key, SourceLabels.TryGetValue(key, out var label) ? label : Humanize(key)));
            }

            return result.OrderBy(x => x.Label).ToList();
        }

        public async Task<List<FieldInfo>?> FieldsAsync(ClaimsPrincipal user, string source)
        {
            var type = await AllowedEntityAsync(user, source);
            return type == null ? null : Catalog(type).Values.ToList();
        }

        private Task<bool> MayReadAsync(ClaimsPrincipal user, (string Controller, string Action) screen)
        {
            var permissions = PermissionCatalog.ResolvePermissions(screen.Controller, screen.Action);
            return _permissions.HasAnyPermissionAsync(user, permissions.ToArray());
        }

        private IEntityType? EntityType(string name)
        {
            var type = _db.Model.GetEntityTypes().FirstOrDefault(x => string.Equals(x.ClrType.Name, name, StringComparison.OrdinalIgnoreCase));
            // Never report on a table that is not filtered to the company.
            return type?.GetQueryFilter() == null ? null : type;
        }

        private async Task<IEntityType?> AllowedEntityAsync(ClaimsPrincipal user, string? source)
        {
            if (source == null || !AssistantQueryTools.Queryable.TryGetValue(source, out var screen))
                return null;
            return await MayReadAsync(user, screen) ? EntityType(source) : null;
        }

        private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> CatalogCache = new();

        /// <summary>Every reportable field of an entity, including those of linked records up to two steps away.</summary>
        private static Dictionary<string, FieldInfo> Catalog(IEntityType type)
        {
            lock (CatalogCache)
            {
                if (CatalogCache.TryGetValue(type.ClrType, out var cached))
                    return cached;

                var fields = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
                Collect(type, string.Empty, string.Empty, 0, fields);
                CatalogCache[type.ClrType] = fields;
                return fields;
            }
        }

        private static void Collect(IEntityType type, string prefix, string labelPrefix, int depth, Dictionary<string, FieldInfo> fields)
        {
            foreach (var property in type.GetProperties())
            {
                if (property.PropertyInfo == null || AssistantQueryTools.IsHiddenField(property.Name))
                    continue;
                // Keys are internal numbers; the linked record's own fields are offered instead.
                if (property.Name.EndsWith("Id", StringComparison.Ordinal))
                    continue;

                var path = prefix + property.Name;
                fields[path] = new FieldInfo(path, labelPrefix + Humanize(property.Name), Kind(property.ClrType));
            }

            if (depth >= MaxDepth)
                return;

            foreach (var navigation in type.GetNavigations())
            {
                if (navigation.IsCollection || navigation.PropertyInfo == null ||
                    AssistantKnowledgeTools.HiddenEntities.Contains(navigation.TargetEntityType.ClrType.Name))
                    continue;

                Collect(navigation.TargetEntityType, prefix + navigation.Name + ".",
                    labelPrefix + Humanize(navigation.Name) + " › ", depth + 1, fields);
            }
        }

        private static string Kind(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            if (t == typeof(DateTime) || t == typeof(DateOnly)) return "date";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(decimal) || t == typeof(int) || t == typeof(long) || t == typeof(double) || t == typeof(float) || t == typeof(short))
                return "number";
            return "text";
        }

        private static string Humanize(string name)
        {
            var words = Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
            if (words.Length == 0)
                return name;
            var text = char.ToUpperInvariant(words[0]) + words[1..].ToLowerInvariant();
            // Abbreviations stay in capitals wherever they fall.
            return Regex.Replace(text, @"\b(vat|trn|tin|iban|pdc|wps|id|uuid|qr)\b", m => m.Value.ToUpperInvariant(), RegexOptions.IgnoreCase);
        }

        // =====================================================================
        //  RUNNING A REPORT
        // =====================================================================

        public sealed class ReportFilter
        {
            public string Field { get; set; } = string.Empty;
            public string Op { get; set; } = "eq";
            public string? Value { get; set; }
            public string? Value2 { get; set; }
        }

        public sealed class ReportDefinition
        {
            public string Source { get; set; } = string.Empty;
            public List<string> Columns { get; set; } = new();
            public List<ReportFilter> Filters { get; set; } = new();

            /// <summary>Group the records by this field and show one total per group.</summary>
            public string? GroupBy { get; set; }

            /// <summary>For a date grouping: day, month, quarter or year.</summary>
            public string? DateGroup { get; set; }

            /// <summary>count, sum, avg, min or max.</summary>
            public string Measure { get; set; } = "count";
            public string? MeasureField { get; set; }

            /// <summary>A field, or for a grouped report "group" or "value".</summary>
            public string? SortBy { get; set; }
            public bool SortDesc { get; set; }
            public int Limit { get; set; } = 500;

            /// <summary>table, bar, line or pie.</summary>
            public string Chart { get; set; } = "table";
        }

        public sealed record ResultColumn(string Label, string Kind);

        public sealed class ReportResult
        {
            public string? Error { get; set; }
            public string Title { get; set; } = string.Empty;
            public bool Grouped { get; set; }
            public List<ResultColumn> Columns { get; set; } = new();
            public List<object?[]> Rows { get; set; } = new();
            public bool Truncated { get; set; }
            public decimal? Total { get; set; }
        }

        public async Task<ReportResult> RunAsync(ClaimsPrincipal user, ReportDefinition definition)
        {
            var type = await AllowedEntityAsync(user, definition.Source);
            if (type == null)
                return new ReportResult { Error = "Choose a data source you have access to." };

            try
            {
                var method = typeof(ReportEngine).GetMethod(nameof(RunTypedAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(type.ClrType);
                var result = await (Task<ReportResult>)method.Invoke(this, new object[] { type, definition })!;
                result.Title = SourceLabels.TryGetValue(type.ClrType.Name, out var label) ? label : Humanize(type.ClrType.Name);
                return result;
            }
            catch (ReportException ex)
            {
                return new ReportResult { Error = ex.Message };
            }
        }

        private sealed class ReportException : Exception
        {
            public ReportException(string message) : base(message) { }
        }

        private async Task<ReportResult> RunTypedAsync<T>(IEntityType type, ReportDefinition definition) where T : class
        {
            var catalog = Catalog(type);
            var parameter = Expression.Parameter(typeof(T), "x");
            var query = _db.Set<T>().AsNoTracking().AsQueryable();

            foreach (var filter in definition.Filters.Where(f => !string.IsNullOrWhiteSpace(f.Field)).Take(15))
            {
                var field = Field(catalog, filter.Field);
                var member = Path(parameter, field.Path);
                query = query.Where(Expression.Lambda<Func<T, bool>>(Condition(member, field, filter), parameter));
            }

            if (!string.IsNullOrWhiteSpace(definition.GroupBy) ||
                (definition.Measure != "count" && !string.IsNullOrWhiteSpace(definition.MeasureField) && definition.Columns.Count == 0))
                return await GroupedAsync(query, parameter, catalog, definition);

            // ---- a list of records
            var columns = definition.Columns.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => Field(catalog, c)).Take(30).ToList();
            if (columns.Count == 0)
                columns = catalog.Values.Where(f => !f.Path.Contains('.')).Take(8).ToList();

            if (!string.IsNullOrWhiteSpace(definition.SortBy))
            {
                var sortField = Field(catalog, definition.SortBy);
                var key = Path(parameter, sortField.Path);
                var lambda = Expression.Lambda(key, parameter);
                var sort = typeof(Queryable).GetMethods()
                    .First(m => m.Name == (definition.SortDesc ? "OrderByDescending" : "OrderBy") && m.GetParameters().Length == 2)
                    .MakeGenericMethod(typeof(T), key.Type);
                query = (IQueryable<T>)sort.Invoke(null, new object[] { query, lambda })!;
            }

            var limit = Math.Clamp(definition.Limit, 1, MaxDetailRows);
            var projection = Expression.Lambda<Func<T, object?[]>>(
                Expression.NewArrayInit(typeof(object), columns.Select(c => Boxed(Path(parameter, c.Path)))), parameter);
            var rows = await query.Select(projection).Take(limit + 1).ToListAsync();

            return new ReportResult
            {
                Columns = columns.Select(c => new ResultColumn(c.Label, c.Kind)).ToList(),
                Rows = rows.Take(limit).Select(r => r.Select(Simplify).ToArray()).ToList(),
                Truncated = rows.Count > limit
            };
        }

        private sealed class Pair
        {
            public object? Key { get; set; }
            public decimal? Value { get; set; }
        }

        private async Task<ReportResult> GroupedAsync<T>(IQueryable<T> query, ParameterExpression parameter,
            Dictionary<string, FieldInfo> catalog, ReportDefinition definition)
        {
            var measure = (definition.Measure ?? "count").ToLowerInvariant();
            if (measure is not ("count" or "sum" or "avg" or "min" or "max"))
                throw new ReportException("Choose count, total, average, lowest or highest.");

            FieldInfo? groupField = string.IsNullOrWhiteSpace(definition.GroupBy) ? null : Field(catalog, definition.GroupBy);
            FieldInfo? valueField = null;
            if (measure != "count")
            {
                valueField = Field(catalog, definition.MeasureField ?? string.Empty);
                if (valueField.Kind != "number")
                    throw new ReportException($"\"{valueField.Label}\" is not a number, so it cannot be totalled.");
            }

            Expression key = groupField == null ? Expression.Constant(null, typeof(object)) : Boxed(Path(parameter, groupField.Path));
            Expression value = valueField == null ? Expression.Constant(null, typeof(decimal?)) : Expression.Convert(Path(parameter, valueField.Path), typeof(decimal?));
            var projection = Expression.Lambda<Func<T, Pair>>(
                Expression.MemberInit(Expression.New(typeof(Pair)),
                    Expression.Bind(typeof(Pair).GetProperty(nameof(Pair.Key))!, key),
                    Expression.Bind(typeof(Pair).GetProperty(nameof(Pair.Value))!, value)),
                parameter);

            var pairs = await query.Select(projection).Take(MaxGroupedInputRows + 1).ToListAsync();
            var truncated = pairs.Count > MaxGroupedInputRows;
            if (truncated) pairs = pairs.Take(MaxGroupedInputRows).ToList();

            decimal? Compute(IEnumerable<Pair> items)
            {
                var list = items.ToList();
                var numbers = list.Where(x => x.Value.HasValue).Select(x => x.Value!.Value).ToList();
                return measure switch
                {
                    "count" => list.Count,
                    "sum" => numbers.Sum(),
                    "avg" => numbers.Count == 0 ? null : Math.Round(numbers.Average(), 2),
                    "min" => numbers.Count == 0 ? null : numbers.Min(),
                    _ => numbers.Count == 0 ? null : numbers.Max()
                };
            }

            var measureLabel = measure switch
            {
                "count" => "Count",
                "sum" => "Total " + LowerFirst(valueField!.Label),
                "avg" => "Average " + LowerFirst(valueField!.Label),
                "min" => "Lowest " + LowerFirst(valueField!.Label),
                _ => "Highest " + LowerFirst(valueField!.Label)
            };

            if (groupField == null)
            {
                var total = Compute(pairs);
                return new ReportResult
                {
                    Grouped = true,
                    Columns = new() { new ResultColumn("", "text"), new ResultColumn(measureLabel, "number") },
                    Rows = new() { new object?[] { "All records", total } },
                    Truncated = truncated,
                    Total = total
                };
            }

            var dateGroup = groupField.Kind == "date" ? (definition.DateGroup ?? "month").ToLowerInvariant() : null;
            var groups = pairs
                .GroupBy(p => GroupKey(p.Key, dateGroup))
                .Select(g => new { g.Key.Sort, g.Key.Label, Value = Compute(g) })
                .ToList();

            var sortByValue = definition.SortBy == "value" || (definition.SortBy != "group" && dateGroup == null && groupField.Kind != "number");
            var ordered = sortByValue
                ? (definition.SortDesc || definition.SortBy != "value" ? groups.OrderByDescending(x => x.Value) : groups.OrderBy(x => x.Value))
                : (definition.SortDesc ? groups.OrderByDescending(x => x.Sort) : groups.OrderBy(x => x.Sort));
            var limit = Math.Clamp(definition.Limit, 1, 500);
            var shown = ordered.Take(limit).ToList();

            return new ReportResult
            {
                Grouped = true,
                Columns = new() { new ResultColumn(groupField.Label + (dateGroup != null ? $" ({dateGroup})" : string.Empty), "text"), new ResultColumn(measureLabel, "number") },
                Rows = shown.Select(x => new object?[] { x.Label, x.Value }).ToList(),
                Truncated = truncated || groups.Count > limit,
                Total = measure is "count" or "sum" ? groups.Sum(x => x.Value ?? 0m) : null
            };
        }

        /// <summary>"Annual rent" becomes "annual rent", but "VAT amount" stays as it is.</summary>
        private static string LowerFirst(string label) =>
            label.Length > 1 && char.IsUpper(label[1]) ? label : char.ToLowerInvariant(label[0]) + label[1..];

        private static (string Sort, string Label) GroupKey(object? key, string? dateGroup)
        {
            if (key == null)
                return ("~", "(blank)");

            if (key is DateTime d && dateGroup != null)
            {
                return dateGroup switch
                {
                    "day" => (d.ToString("yyyy-MM-dd"), d.ToString("dd MMM yyyy")),
                    "year" => (d.ToString("yyyy"), d.ToString("yyyy")),
                    "quarter" => ($"{d.Year}-Q{(d.Month - 1) / 3 + 1}", $"Q{(d.Month - 1) / 3 + 1} {d.Year}"),
                    _ => (d.ToString("yyyy-MM"), d.ToString("MMM yyyy"))
                };
            }

            if (key is bool b)
                return (b ? "1" : "0", b ? "Yes" : "No");

            if (key is decimal or int or long or double)
            {
                var number = Convert.ToDecimal(key, CultureInfo.InvariantCulture);
                return (number.ToString("000000000000.0000", CultureInfo.InvariantCulture), number.ToString("N2"));
            }

            var text = key.ToString() ?? string.Empty;
            return (text.ToLowerInvariant(), text.Length == 0 ? "(blank)" : text);
        }

        // =====================================================================
        //  EXPRESSIONS
        // =====================================================================

        private static FieldInfo Field(Dictionary<string, FieldInfo> catalog, string path) =>
            catalog.TryGetValue(path, out var field)
                ? field
                : throw new ReportException($"The field \"{path}\" is not available in this data source.");

        private static Expression Path(Expression root, string path)
        {
            Expression current = root;
            foreach (var part in path.Split('.'))
                current = Expression.Property(current, part);
            return current;
        }

        /// <summary>A value boxed for the result; values reached through a link may be missing, so they become nullable first.</summary>
        private static Expression Boxed(Expression member)
        {
            if (member.Type.IsValueType && Nullable.GetUnderlyingType(member.Type) == null)
                member = Expression.Convert(member, typeof(Nullable<>).MakeGenericType(member.Type));
            return Expression.Convert(member, typeof(object));
        }

        private static Expression Condition(Expression member, FieldInfo field, ReportFilter filter)
        {
            var op = (filter.Op ?? "eq").ToLowerInvariant();
            var type = member.Type;
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            var nullable = !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

            if (op == "empty" || op == "notempty")
            {
                Expression isEmpty = nullable ? Expression.Equal(member, Expression.Constant(null, type)) : Expression.Constant(false);
                if (type == typeof(string))
                    isEmpty = Expression.OrElse(isEmpty, Expression.Equal(member, Expression.Constant(string.Empty)));
                return op == "empty" ? isEmpty : Expression.Not(isEmpty);
            }

            if (op is "contains" or "startswith" or "notcontains")
            {
                if (type != typeof(string))
                    throw new ReportException($"\"{field.Label}\" is not text; use equals, greater or less than.");
                var method = typeof(string).GetMethod(op == "startswith" ? nameof(string.StartsWith) : nameof(string.Contains), new[] { typeof(string) })!;
                var call = Expression.AndAlso(
                    Expression.NotEqual(member, Expression.Constant(null, typeof(string))),
                    Expression.Call(member, method, Expression.Constant(filter.Value ?? string.Empty)));
                return op == "notcontains" ? Expression.Not(call) : call;
            }

            if (op == "in")
            {
                var values = (filter.Value ?? string.Empty).Split(new[] { ',', '|' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(50).ToList();
                if (values.Count == 0) throw new ReportException($"Give the values for \"{field.Label}\", separated by commas.");
                return values.Select(v => (Expression)Expression.Equal(member, Constant(v, type, field))).Aggregate(Expression.OrElse);
            }

            // A date is compared by whole days, whatever time of day was stored.
            if (underlying == typeof(DateTime) && op is "eq" or "ne" or "between" or "lte")
            {
                var from = (DateTime)Constant(filter.Value, underlying, field).Value!;
                var to = op == "between" ? (DateTime)Constant(filter.Value2, underlying, field).Value! : from;
                var lower = Expression.Constant(from.Date, type);
                var upper = Expression.Constant(to.Date.AddDays(1), type);
                if (op == "lte") return Expression.LessThan(member, upper);
                var within = Expression.AndAlso(Expression.GreaterThanOrEqual(member, lower), Expression.LessThan(member, upper));
                return op == "ne" ? Expression.Not(within) : within;
            }

            return op switch
            {
                "eq" => Expression.Equal(member, Constant(filter.Value, type, field)),
                "ne" => Expression.NotEqual(member, Constant(filter.Value, type, field)),
                "gt" => Expression.GreaterThan(member, Constant(filter.Value, type, field)),
                "gte" => Expression.GreaterThanOrEqual(member, Constant(filter.Value, type, field)),
                "lt" => Expression.LessThan(member, Constant(filter.Value, type, field)),
                "lte" => Expression.LessThanOrEqual(member, Constant(filter.Value, type, field)),
                "between" => Expression.AndAlso(
                    Expression.GreaterThanOrEqual(member, Constant(filter.Value, type, field)),
                    Expression.LessThanOrEqual(member, Constant(filter.Value2, type, field))),
                _ => throw new ReportException($"Unknown condition \"{op}\".")
            };
        }

        private static ConstantExpression Constant(string? raw, Type type, FieldInfo field)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (type.IsValueType && Nullable.GetUnderlyingType(type) == null)
                    throw new ReportException($"Give a value for \"{field.Label}\".");
                return Expression.Constant(null, type);
            }

            try
            {
                object value = underlying == typeof(string) ? raw.Trim()
                    : underlying == typeof(DateTime) ? DateTime.Parse(raw, CultureInfo.InvariantCulture)
                    : underlying == typeof(bool) ? raw.Trim().ToLowerInvariant() is "true" or "yes" or "1"
                    : underlying == typeof(Guid) ? Guid.Parse(raw)
                    : underlying.IsEnum ? Enum.Parse(underlying, raw, true)
                    : Convert.ChangeType(raw.Replace(",", string.Empty), underlying, CultureInfo.InvariantCulture);
                return Expression.Constant(value, type);
            }
            catch (Exception)
            {
                throw new ReportException($"\"{raw}\" is not a valid value for \"{field.Label}\".");
            }
        }

        private static object? Simplify(object? value) => value switch
        {
            DateTime d when d.TimeOfDay == TimeSpan.Zero => d.ToString("yyyy-MM-dd"),
            DateTime d => d.ToString("yyyy-MM-dd HH:mm"),
            string s when s.Length > 500 => s[..500] + "…",
            _ => value
        };
    }
}
