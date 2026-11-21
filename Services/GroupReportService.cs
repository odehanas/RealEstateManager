using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The financial statements of a group: the mother company and its branches.
    ///
    /// Combined: each company's own statements side by side and added together, nothing
    /// removed. Consolidated: the same, less what the companies of the group did with one
    /// another - the balances they owe each other and the income and expense they charged
    /// each other - so the group is shown as the single entity it is to an outsider (the
    /// full elimination of intragroup balances and transactions that IFRS 10 asks for).
    ///
    /// What is intra-group is what was posted to an account marked intra-group in the
    /// chart of accounts. The eliminations only cancel out when both sides were posted:
    /// what one company shows as receivable from the group, another must show as payable.
    /// A difference is reported, never hidden.
    ///
    /// Every company's figures come from the same FinancialReportService that produces
    /// its own statements, so a column here always agrees with that company's reports.
    /// </summary>
    public class GroupReportService
    {
        private readonly ApplicationDbContext _db;
        private readonly IServiceScopeFactory _scopeFactory;

        public GroupReportService(ApplicationDbContext db, IServiceScopeFactory scopeFactory)
        {
            _db = db;
            _scopeFactory = scopeFactory;
        }

        /// <summary>The mother company first, then its branches by name.</summary>
        public async Task<List<GroupMemberDto>> GetMembersAsync(int companyId)
        {
            var motherId = await _db.Companies.IgnoreQueryFilters()
                .Where(x => x.Id == companyId)
                .Select(x => x.ParentCompanyId ?? x.Id)
                .FirstOrDefaultAsync();

            var members = await _db.Companies.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Id == motherId || x.ParentCompanyId == motherId)
                .Select(x => new GroupMemberDto
                {
                    CompanyId = x.Id,
                    Name = x.Name,
                    IsMother = x.Id == motherId,
                    BaseCurrency = x.BaseCurrency,
                    FiscalYearStartMonth = x.FiscalYearStartMonth
                })
                .ToListAsync();

            return members.OrderByDescending(x => x.IsMother).ThenBy(x => x.Name).ToList();
        }

        /// <summary>Whether the company is a mother company with branches, or a branch.</summary>
        public Task<bool> IsGroupAsync(int companyId) =>
            _db.Companies.IgnoreQueryFilters().AnyAsync(x =>
                (x.Id == companyId && x.ParentCompanyId != null) || x.ParentCompanyId == companyId);

        /// <param name="startDate">Start of the income statement period; null for the start of the financial year.</param>
        public async Task<GroupStatementsDto> BuildAsync(
            int companyId, int? userId, bool isSystemAdmin, DateTime? startDate, DateTime endDate, bool consolidated)
        {
            var members = await GetMembersAsync(companyId);
            if (members.Count < 2)
                throw new InvalidOperationException("This company has no branches, so there is nothing to add together. Its own statements are under Financial Reports.");

            // A group report with a company left out would be wrong without looking wrong,
            // so it is only produced for someone who may see every company in it.
            if (!isSystemAdmin)
            {
                var ids = members.Select(x => x.CompanyId).ToList();
                var accessible = await _db.AppUserCompanies.IgnoreQueryFilters()
                    .Where(x => x.AppUserId == userId && ids.Contains(x.CompanyId))
                    .Select(x => x.CompanyId)
                    .ToListAsync();
                var missing = members.Where(x => !accessible.Contains(x.CompanyId)).Select(x => x.Name).ToList();
                if (missing.Count > 0)
                    throw new InvalidOperationException(
                        "The group report covers every company of the group, and you have no access to: " +
                        string.Join(", ", missing) + ". Ask the company administrator for access to them.");
            }

            var mother = members[0];
            var different = members
                .Where(x => !string.Equals(x.BaseCurrency, mother.BaseCurrency, StringComparison.OrdinalIgnoreCase)
                            || x.FiscalYearStartMonth != mother.FiscalYearStartMonth)
                .Select(x => x.Name)
                .ToList();
            if (different.Count > 0)
                throw new InvalidOperationException(
                    $"These companies do not use the currency and financial year of {mother.Name}, so they cannot be added to it: " +
                    string.Join(", ", different) + ".");

            var yearStart = endDate.Month >= mother.FiscalYearStartMonth
                ? new DateTime(endDate.Year, mother.FiscalYearStartMonth, 1)
                : new DateTime(endDate.Year - 1, mother.FiscalYearStartMonth, 1);
            var from = (startDate ?? yearStart).Date;
            if (from > endDate)
                from = yearStart;

            var report = new GroupStatementsDto
            {
                IsConsolidated = consolidated,
                StartDate = from,
                EndDate = endDate,
                Currency = mother.BaseCurrency,
                Members = members
            };

            var n = members.Count;
            var revenue = new GroupSectionBuilder(n);
            var expenses = new GroupSectionBuilder(n);
            var assets = new GroupSectionBuilder(n);
            var liabilities = new GroupSectionBuilder(n);
            var equity = new GroupSectionBuilder(n);
            var currentYearProfit = new GroupRowDto { AccountCode = string.Empty, AccountName = "Profit or loss for the current year", Amounts = new decimal[n] };

            for (var i = 0; i < n; i++)
            {
                var member = members[i];

                // A scope of its own, filtered to this company exactly as if its user were signed in to it.
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<CompanyContextOverride>();
                context.CompanyId = member.CompanyId;
                context.UserId = userId;
                var statements = scope.ServiceProvider.GetRequiredService<FinancialReportService>();

                var intraGroup = (await _db.Accounts.IgnoreQueryFilters()
                        .Where(x => x.CompanyId == member.CompanyId && x.IsIntraGroup)
                        .Select(x => x.Code)
                        .ToListAsync())
                    .ToHashSet();

                var income = await statements.GetProfitAndLossAsync(member.CompanyId, from, endDate);
                foreach (var row in income.Revenue)
                    revenue.Add(i, row.AccountCode, row.AccountName, row.Amount, intraGroup.Contains(row.AccountCode));
                foreach (var row in income.Expenses)
                    expenses.Add(i, row.AccountCode, row.AccountName, row.Amount, intraGroup.Contains(row.AccountCode));

                var balance = await statements.GetBalanceSheetAsync(member.CompanyId, endDate);
                foreach (var row in balance.Assets)
                    assets.Add(i, row.AccountCode, row.AccountName, row.Amount, intraGroup.Contains(row.AccountCode));
                foreach (var row in balance.Liabilities)
                    liabilities.Add(i, row.AccountCode, row.AccountName, row.Amount, intraGroup.Contains(row.AccountCode));
                if (balance.ShareCapital != 0m)
                    equity.Add(i, "31100", "Share Capital", balance.ShareCapital, intraGroup.Contains("31100"));
                if (balance.RetainedEarningsPriorYears != 0m)
                    equity.Add(i, "32100", "Retained Earnings - Prior Years", balance.RetainedEarningsPriorYears, intraGroup.Contains("32100"));
                foreach (var row in balance.OtherEquityRows)
                    equity.Add(i, row.AccountCode, row.AccountName, row.Amount, intraGroup.Contains(row.AccountCode));

                currentYearProfit.Amounts[i] = balance.CurrentYearProfitOrLoss;

                // The balance sheet carries the profit of the financial year to date, which is
                // not always the period of the income statement above, so the intra-group
                // income and expense inside it are measured over that same year to date.
                var yearToDate = from == yearStart
                    ? income
                    : await statements.GetProfitAndLossAsync(member.CompanyId, yearStart, endDate);
                var intraGroupResult =
                    yearToDate.Revenue.Where(x => intraGroup.Contains(x.AccountCode)).Sum(x => x.Amount) -
                    yearToDate.Expenses.Where(x => intraGroup.Contains(x.AccountCode)).Sum(x => x.Amount);
                currentYearProfit.Eliminations -= intraGroupResult;
                if (intraGroupResult != 0m)
                    currentYearProfit.IsIntraGroup = true;
            }

            report.Revenue = revenue.Build();
            report.Expenses = expenses.Build();
            report.Assets = assets.Build();
            report.Liabilities = liabilities.Build();
            report.Equity = equity.Build();
            report.Equity.Add(currentYearProfit);

            if (!consolidated)
            {
                foreach (var row in report.AllRows)
                {
                    row.Eliminations = 0m;
                    row.IsIntraGroup = false;
                }
            }

            return report;
        }

        /// <summary>Gathers one section's accounts across the companies, matched by account code.</summary>
        private sealed class GroupSectionBuilder
        {
            private readonly int _members;
            private readonly Dictionary<string, GroupRowDto> _rows = new(StringComparer.OrdinalIgnoreCase);

            public GroupSectionBuilder(int members) => _members = members;

            public void Add(int member, string code, string name, decimal amount, bool isIntraGroup)
            {
                if (!_rows.TryGetValue(code, out var row))
                {
                    row = new GroupRowDto { AccountCode = code, AccountName = name, Amounts = new decimal[_members] };
                    _rows[code] = row;
                }

                row.Amounts[member] += amount;
                if (isIntraGroup)
                {
                    row.Eliminations -= amount;
                    row.IsIntraGroup = true;
                }
            }

            public List<GroupRowDto> Build() =>
                _rows.Values.OrderBy(x => x.AccountCode, StringComparer.Ordinal).ToList();
        }
    }

    public class GroupMemberDto
    {
        public int CompanyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsMother { get; set; }
        public string BaseCurrency { get; set; } = string.Empty;
        public int FiscalYearStartMonth { get; set; }
    }

    /// <summary>One account across the group. Amounts are in the account's natural sign.</summary>
    public class GroupRowDto
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;

        /// <summary>One amount per company, in the order of the report's Members.</summary>
        public decimal[] Amounts { get; set; } = Array.Empty<decimal>();

        /// <summary>What consolidation removes from this account (negative of its intra-group amounts).</summary>
        public decimal Eliminations { get; set; }

        /// <summary>The account is marked intra-group in at least one company.</summary>
        public bool IsIntraGroup { get; set; }

        public decimal Combined => Amounts.Sum();
        public decimal Consolidated => Combined + Eliminations;
    }

    /// <summary>A line of totals: one figure per company, then combined, eliminations, consolidated.</summary>
    public class GroupTotalsDto
    {
        public decimal[] Amounts { get; set; } = Array.Empty<decimal>();
        public decimal Eliminations { get; set; }
        public decimal Combined => Amounts.Sum();
        public decimal Consolidated => Combined + Eliminations;

        public static GroupTotalsDto Of(IEnumerable<GroupRowDto> rows, int members)
        {
            var totals = new GroupTotalsDto { Amounts = new decimal[members] };
            foreach (var row in rows)
            {
                for (var i = 0; i < members; i++)
                    totals.Amounts[i] += row.Amounts[i];
                totals.Eliminations += row.Eliminations;
            }

            return totals;
        }

        public GroupTotalsDto Plus(GroupTotalsDto other) => Combine(other, 1m);
        public GroupTotalsDto Minus(GroupTotalsDto other) => Combine(other, -1m);

        private GroupTotalsDto Combine(GroupTotalsDto other, decimal sign) => new()
        {
            Amounts = Amounts.Select((x, i) => x + sign * other.Amounts[i]).ToArray(),
            Eliminations = Eliminations + sign * other.Eliminations
        };
    }

    public class GroupStatementsDto
    {
        public bool IsConsolidated { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Currency { get; set; } = string.Empty;
        public List<GroupMemberDto> Members { get; set; } = new();

        /// <summary>Set instead of the figures when the report cannot be produced.</summary>
        public string? ErrorMessage { get; set; }

        public List<GroupRowDto> Revenue { get; set; } = new();
        public List<GroupRowDto> Expenses { get; set; } = new();
        public List<GroupRowDto> Assets { get; set; } = new();
        public List<GroupRowDto> Liabilities { get; set; } = new();
        public List<GroupRowDto> Equity { get; set; } = new();

        public IEnumerable<GroupRowDto> AllRows => Revenue.Concat(Expenses).Concat(Assets).Concat(Liabilities).Concat(Equity);

        private GroupTotalsDto Total(IEnumerable<GroupRowDto> rows) => GroupTotalsDto.Of(rows, Members.Count);

        public GroupTotalsDto TotalRevenue => Total(Revenue);
        public GroupTotalsDto TotalExpenses => Total(Expenses);
        public GroupTotalsDto NetProfit => TotalRevenue.Minus(TotalExpenses);
        public GroupTotalsDto TotalAssets => Total(Assets);
        public GroupTotalsDto TotalLiabilities => Total(Liabilities);
        public GroupTotalsDto TotalEquity => Total(Equity);
        public GroupTotalsDto TotalLiabilitiesAndEquity => TotalLiabilities.Plus(TotalEquity);

        // ---- Whether the two sides of what the companies did with one another agree ----

        /// <summary>Intra-group income removed from the income statement of the period.</summary>
        public decimal IntraGroupRevenue => -Revenue.Sum(x => x.Eliminations);

        /// <summary>Intra-group expense removed from the income statement of the period.</summary>
        public decimal IntraGroupExpenses => -Expenses.Sum(x => x.Eliminations);

        /// <summary>Intra-group receivables and other intra-group assets removed.</summary>
        public decimal IntraGroupAssets => -Assets.Sum(x => x.Eliminations);

        /// <summary>Intra-group payables, and intra-group equity such as a head office account, removed.</summary>
        public decimal IntraGroupLiabilitiesAndEquity =>
            -Liabilities.Sum(x => x.Eliminations) - Equity.Where(x => x.AccountCode.Length > 0).Sum(x => x.Eliminations);

        /// <summary>Income one company charged that no other company of the group recorded as its expense, or the reverse.</summary>
        public decimal IncomeDifference => IntraGroupRevenue - IntraGroupExpenses;

        /// <summary>
        /// What keeps the consolidated balance sheet from balancing: the intra-group
        /// amounts on the two sides of the group do not cancel. Zero when every
        /// intra-group entry was posted in both companies.
        /// </summary>
        public decimal UnreconciledDifference => TotalAssets.Consolidated - TotalLiabilitiesAndEquity.Consolidated;

        public bool HasEliminations => AllRows.Any(x => x.Eliminations != 0m);
    }
}
