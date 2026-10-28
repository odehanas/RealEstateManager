using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Works out each revenue and expense account's income statement line: its own
    /// setting, else the nearest parent group's, else the standard default.
    /// </summary>
    public static class StatementLineResolver
    {
        private sealed record Node(int Id, string Code, string AccountType, string ReportingGroup, string? StatementLine, int? ParentId);

        /// <summary>The line of every revenue and expense account of the company, by account id.</summary>
        public static async Task<Dictionary<int, string>> ResolveAllAsync(ApplicationDbContext db, int companyId)
        {
            var nodes = await db.Accounts.AsNoTracking()
                .Where(x => x.CompanyId == companyId && (x.AccountType == "Revenue" || x.AccountType == "Expense"))
                .Select(x => new Node(x.Id, x.Code, x.AccountType, x.ReportingGroup, x.StatementLine, x.ParentAccountId))
                .ToDictionaryAsync(x => x.Id);

            return nodes.Values.ToDictionary(n => n.Id, n => Resolve(n, nodes));
        }

        /// <summary>What an account's line is when its own setting is left empty.</summary>
        public static async Task<string?> InheritedAsync(ApplicationDbContext db, Account account)
        {
            if (account.AccountType is not ("Revenue" or "Expense"))
                return null;
            var nodes = await db.Accounts.AsNoTracking()
                .Where(x => x.CompanyId == account.CompanyId && (x.AccountType == "Revenue" || x.AccountType == "Expense"))
                .Select(x => new Node(x.Id, x.Code, x.AccountType, x.ReportingGroup, x.StatementLine, x.ParentAccountId))
                .ToDictionaryAsync(x => x.Id);
            var self = new Node(account.Id, account.Code, account.AccountType, account.ReportingGroup, null, account.ParentAccountId);
            return Resolve(self, nodes);
        }

        private static string Resolve(Node node, Dictionary<int, Node> nodes)
        {
            var valid = node.AccountType == "Revenue" ? StatementLines.Revenue : StatementLines.Costs;
            var current = node;
            for (var depth = 0; current != null && depth < 20; depth++)
            {
                if (current.StatementLine != null && valid.Contains(current.StatementLine))
                    return current.StatementLine;
                current = current.ParentId.HasValue && nodes.TryGetValue(current.ParentId.Value, out var parent) ? parent : null;
            }

            return StatementLines.Default(node.Code, node.AccountType, node.ReportingGroup);
        }
    }
}
