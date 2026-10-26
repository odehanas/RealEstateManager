using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RealEstateManager.Data;

namespace RealEstateManager.Services.Assistant
{
    /// <summary>
    /// Questions the curated tools do not cover: filter, sort and count any whitelisted
    /// record type. Queries are built as LINQ over ApplicationDbContext - never SQL - so
    /// the company filter applies exactly as on the screens, and each entity carries the
    /// permission of its own screen.
    /// </summary>
    public static class AssistantQueryTools
    {
        private const int MaxRows = 100;
        private const int MaxAggregateRows = 20000;

        /// <summary>Entity name → the screen whose permission reading it needs.</summary>
        public static readonly IReadOnlyDictionary<string, (string Controller, string Action)> Queryable =
            new Dictionary<string, (string Controller, string Action)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Property"] = ("Properties", "Index"),
                ["Unit"] = ("Units", "Index"),
                ["Tenant"] = ("Tenants", "Index"),
                ["Vendor"] = ("Vendors", "Index"),
                ["Lease"] = ("Leases", "Index"),
                ["CustomerInvoice"] = ("CustomerInvoices", "Index"),
                ["CustomerReceipt"] = ("CustomerReceipts", "Index"),
                ["CustomerCreditNote"] = ("CustomerCreditNotes", "Index"),
                ["PdcReceipt"] = ("PdcReceipts", "Index"),
                ["SecurityDeposit"] = ("SecurityDeposits", "Index"),
                ["VendorBill"] = ("VendorBills", "Index"),
                ["VendorPayment"] = ("VendorPayments", "Index"),
                ["JournalEntry"] = ("Journal", "Index"),
                ["JournalEntryLine"] = ("Journal", "Index"),
                ["Account"] = ("ChartOfAccounts", "Index"),
                ["AccountingPeriod"] = ("Closing", "Index"),
                ["FixedAsset"] = ("FixedAssets", "Index"),
                ["TenantServiceRequest"] = ("ServiceRequests", "Index"),
                ["ApprovalRequest"] = ("Approvals", "Index"),
                ["OwnerSettlement"] = ("OwnerSettlements", "Index"),
                ["UnitSale"] = ("UnitSales", "Index"),
                ["DevelopmentProject"] = ("DevelopmentProjects", "Index"),
                ["Employee"] = ("Employees", "Index"),
                ["LeaveRequest"] = ("Leave", "Index"),
                ["EmployeeAdvance"] = ("EmployeeAdvances", "Index"),
                ["PayrollRun"] = ("Payroll", "Index"),
                ["PayrollRunLine"] = ("Payroll", "Index"),
                ["AttendanceRecord"] = ("Attendance", "Index"),
                ["PaymentApproval"] = ("PaymentApprovals", "Index")
            };

        public static bool IsQueryable(string entity) => Queryable.ContainsKey(entity);

        /// <summary>Fields never returned: credentials and bulky documents.</summary>
        public static bool IsHiddenField(string name) =>
            name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Xml", StringComparison.OrdinalIgnoreCase);

        public static IEnumerable<AssistantTool> All()
        {
            JsonObject Filters() => Schema.Array("Conditions, all must hold",
                Schema.Object(
                    ("field", Schema.Str("Field name from describe_data_model"), true),
                    ("op", Schema.Enum("Operator", "eq", "ne", "gt", "gte", "lt", "lte", "contains", "between", "in"), true),
                    ("value", Schema.Str("Value; for between give 'from|to', for in give 'a|b|c'"), false)));


            yield return new AssistantTool
            {
                Name = "query_records",
                Description = "Find records of any type the curated tools do not cover, e.g. Lease, Unit, CustomerInvoice, Employee. Call describe_data_model first for field names. Only the user's company is ever searched.",
                Parameters = Schema.Object(
                    ("entity", Schema.Str("Entity name"), true),
                    ("filters", Filters(), false),
                    ("order_by", Schema.Str("Field to sort by"), false),
                    ("descending", Schema.Bool("Sort high to low / newest first"), false),
                    ("take", Schema.Int("Rows, at most 100"), false)),
                Run = (ctx, args) => RunGeneric(nameof(QueryAsync), ctx, args)
            };

            yield return new AssistantTool
            {
                Name = "aggregate_records",
                Description = "Count, total or average records, optionally grouped, e.g. total rent of active leases by property, number of units by status.",
                Parameters = Schema.Object(
                    ("entity", Schema.Str("Entity name"), true),
                    ("function", Schema.Enum("What to compute", "count", "sum", "avg", "min", "max"), true),
                    ("field", Schema.Str("Numeric field for sum/avg/min/max"), false),
                    ("group_by", Schema.Str("Field to group by"), false),
                    ("filters", Filters(), false)),
                Run = (ctx, args) => RunGeneric(nameof(AggregateAsync), ctx, args)
            };
        }

        private static async Task<object?> RunGeneric(string method, AssistantToolContext ctx, JsonElement args)
        {
            var name = Args.Required(args, "entity");
            if (!Queryable.TryGetValue(name, out var screen))
                return new { error = $"'{name}' cannot be queried. Queryable: {string.Join(", ", Queryable.Keys)}." };

            var permissions = PermissionCatalog.ResolvePermissions(screen.Controller, screen.Action);
            if (!await ctx.Get<PermissionService>().HasAnyPermissionAsync(ctx.User, permissions.ToArray()))
                return new { error = $"You do not have access to {name} records (the {screen.Controller} screen)." };

            var entityType = ctx.Db.Model.GetEntityTypes().FirstOrDefault(x => string.Equals(x.ClrType.Name, name, StringComparison.OrdinalIgnoreCase));

            // Belt and braces: never query a table that is not filtered to the company.
            if (entityType == null || entityType.GetQueryFilter() == null)
                return new { error = $"'{name}' cannot be queried." };

            var generic = typeof(AssistantQueryTools).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(entityType.ClrType);
            return await (Task<object?>)generic.Invoke(null, new object[] { ctx.Db, entityType, args })!;
        }

        private static async Task<object?> QueryAsync<T>(ApplicationDbContext db, IEntityType entityType, JsonElement args) where T : class
        {
            var query = ApplyFilters(db.Set<T>().AsNoTracking(), entityType, args);

            var orderBy = Args.Str(args, "order_by");
            if (orderBy != null)
            {
                var property = Field(entityType, orderBy);
                var parameter = Expression.Parameter(typeof(T), "x");
                var key = Expression.Lambda(Expression.Property(parameter, property), parameter);
                var sort = typeof(System.Linq.Queryable).GetMethods()
                    .First(m => m.Name == (Args.Bool(args, "descending") ? "OrderByDescending" : "OrderBy") && m.GetParameters().Length == 2)
                    .MakeGenericMethod(typeof(T), property.PropertyType);
                query = (IQueryable<T>)sort.Invoke(null, new object[] { query, key })!;
            }

            var take = Math.Clamp(Args.Int(args, "take") ?? 25, 1, MaxRows);
            var rows = await query.Take(take + 1).ToListAsync();
            var fields = entityType.GetProperties()
                .Where(p => p.PropertyInfo != null && !IsHiddenField(p.Name))
                .Select(p => p.PropertyInfo!)
                .ToList();

            return new
            {
                entity = entityType.ClrType.Name,
                count = Math.Min(rows.Count, take),
                more = rows.Count > take,
                rows = rows.Take(take).Select(row => fields.ToDictionary(f => f.Name, f => Simplify(f.GetValue(row)))).ToList()
            };
        }

        private sealed class GroupValue
        {
            public object? Key { get; set; }
            public decimal? Value { get; set; }
        }

        private static async Task<object?> AggregateAsync<T>(ApplicationDbContext db, IEntityType entityType, JsonElement args) where T : class
        {
            var query = ApplyFilters(db.Set<T>().AsNoTracking(), entityType, args);
            var function = Args.Required(args, "function").ToLowerInvariant();
            var fieldName = Args.Str(args, "field");
            var groupName = Args.Str(args, "group_by");

            if (function != "count" && fieldName == null)
                throw new AssistantArgumentException($"'{function}' needs a numeric field.");

            var parameter = Expression.Parameter(typeof(T), "x");
            Expression keyExpr = Expression.Constant(null, typeof(object));
            if (groupName != null)
                keyExpr = Expression.Convert(Expression.Property(parameter, Field(entityType, groupName)), typeof(object));

            Expression valueExpr = Expression.Constant(null, typeof(decimal?));
            if (fieldName != null)
            {
                var field = Field(entityType, fieldName);
                var underlying = Nullable.GetUnderlyingType(field.PropertyType) ?? field.PropertyType;
                if (!(underlying == typeof(decimal) || underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(double)))
                    throw new AssistantArgumentException($"'{fieldName}' is not a number.");
                valueExpr = Expression.Convert(Expression.Property(parameter, field), typeof(decimal?));
            }

            var projection = Expression.Lambda<Func<T, GroupValue>>(
                Expression.MemberInit(Expression.New(typeof(GroupValue)),
                    Expression.Bind(typeof(GroupValue).GetProperty(nameof(GroupValue.Key))!, keyExpr),
                    Expression.Bind(typeof(GroupValue).GetProperty(nameof(GroupValue.Value))!, valueExpr)),
                parameter);

            var values = await query.Select(projection).Take(MaxAggregateRows + 1).ToListAsync();
            var truncated = values.Count > MaxAggregateRows;

            decimal? Compute(IEnumerable<GroupValue> items)
            {
                var list = items.ToList();
                var numbers = list.Where(x => x.Value.HasValue).Select(x => x.Value!.Value).ToList();
                return function switch
                {
                    "count" => list.Count,
                    "sum" => numbers.Sum(),
                    "avg" => numbers.Count == 0 ? null : Math.Round(numbers.Average(), 2),
                    "min" => numbers.Count == 0 ? null : numbers.Min(),
                    "max" => numbers.Count == 0 ? null : numbers.Max(),
                    _ => throw new AssistantArgumentException("function must be count, sum, avg, min or max.")
                };
            }

            if (groupName == null)
                return new { entity = entityType.ClrType.Name, function, field = fieldName, result = Compute(values), truncated };

            var groups = values.GroupBy(x => Simplify(x.Key)?.ToString() ?? "(none)")
                .Select(g => new { group = g.Key, result = Compute(g) })
                .OrderByDescending(x => x.result)
                .Take(50)
                .ToList();
            return new { entity = entityType.ClrType.Name, function, field = fieldName, groupBy = groupName, groups, truncated };
        }

        private static IQueryable<T> ApplyFilters<T>(IQueryable<T> query, IEntityType entityType, JsonElement args)
        {
            if (!args.TryGetProperty("filters", out var filters) || filters.ValueKind != JsonValueKind.Array)
                return query;

            foreach (var filter in filters.EnumerateArray().Take(10))
            {
                var parameter = Expression.Parameter(typeof(T), "x");
                var property = Field(entityType, Args.Required(filter, "field"));
                var member = Expression.Property(parameter, property);
                var op = Args.Required(filter, "op").ToLowerInvariant();
                var raw = Args.Str(filter, "value");

                Expression body = op switch
                {
                    "eq" => Expression.Equal(member, Constant(raw, property)),
                    "ne" => Expression.NotEqual(member, Constant(raw, property)),
                    "gt" => Expression.GreaterThan(member, Constant(raw, property)),
                    "gte" => Expression.GreaterThanOrEqual(member, Constant(raw, property)),
                    "lt" => Expression.LessThan(member, Constant(raw, property)),
                    "lte" => Expression.LessThanOrEqual(member, Constant(raw, property)),
                    "contains" when property.PropertyType == typeof(string) =>
                        Expression.AndAlso(
                            Expression.NotEqual(member, Expression.Constant(null, typeof(string))),
                            Expression.Call(member, typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!,
                                Expression.Constant(raw ?? string.Empty))),
                    "contains" => throw new AssistantArgumentException($"'contains' only works on text fields, not {property.Name}."),
                    "between" => Between(member, raw, property),
                    "in" => In(member, raw, property),
                    _ => throw new AssistantArgumentException($"Unknown operator '{op}'.")
                };

                query = query.Where(Expression.Lambda<Func<T, bool>>(body, parameter));
            }

            return query;
        }

        private static Expression Between(MemberExpression member, string? raw, PropertyInfo property)
        {
            var parts = (raw ?? string.Empty).Split('|');
            if (parts.Length != 2)
                throw new AssistantArgumentException("between needs 'from|to'.");
            return Expression.AndAlso(
                Expression.GreaterThanOrEqual(member, Constant(parts[0], property)),
                Expression.LessThanOrEqual(member, Constant(parts[1], property)));
        }

        private static Expression In(MemberExpression member, string? raw, PropertyInfo property)
        {
            var parts = (raw ?? string.Empty).Split('|', StringSplitOptions.TrimEntries).Take(50).ToList();
            if (parts.Count == 0)
                throw new AssistantArgumentException("in needs 'a|b|c'.");
            return parts.Select(p => (Expression)Expression.Equal(member, Constant(p, property))).Aggregate(Expression.OrElse);
        }

        private static PropertyInfo Field(IEntityType entityType, string name)
        {
            var property = entityType.GetProperties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (property?.PropertyInfo == null || IsHiddenField(property.Name))
                throw new AssistantArgumentException($"{entityType.ClrType.Name} has no field '{name}'. Use describe_data_model.");
            return property.PropertyInfo;
        }

        private static ConstantExpression Constant(string? raw, PropertyInfo property)
        {
            var type = property.PropertyType;
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (raw == null || raw.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                if (type.IsValueType && Nullable.GetUnderlyingType(type) == null)
                    throw new AssistantArgumentException($"{property.Name} cannot be empty.");
                return Expression.Constant(null, type);
            }

            object value;
            try
            {
                value = underlying == typeof(string) ? raw
                    : underlying == typeof(DateTime) ? DateTime.Parse(raw, CultureInfo.InvariantCulture)
                    : underlying == typeof(bool) ? bool.Parse(raw)
                    : underlying == typeof(Guid) ? Guid.Parse(raw)
                    : underlying.IsEnum ? Enum.Parse(underlying, raw, true)
                    : Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                throw new AssistantArgumentException($"'{raw}' is not a valid {underlying.Name} for {property.Name}.");
            }

            return Expression.Constant(value, type);
        }

        public static object? Simplify(object? value) => value switch
        {
            DateTime d when d.TimeOfDay == TimeSpan.Zero => d.ToString("yyyy-MM-dd"),
            DateTime d => d.ToString("yyyy-MM-ddTHH:mm"),
            string s when s.Length > 300 => s[..300] + "…",
            _ => value
        };
    }
}
