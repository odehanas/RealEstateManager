using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    public class FinancialReportService
    {
        private readonly ApplicationDbContext _db;
        private readonly OpeningBalanceService _openingBalanceService;

        public FinancialReportService(ApplicationDbContext db, OpeningBalanceService openingBalanceService)
        {
            _db = db;
            _openingBalanceService = openingBalanceService;
        }

        public async Task<TrialBalanceReportDto> GetTrialBalanceAsync(int companyId, DateTime asOfDate)
        {
            var openingSnapshot = await _openingBalanceService.BuildSnapshotAsync();

            var movementRows = await _db.JournalEntryLines
                .Include(x => x.Account)
                .Include(x => x.JournalEntry)
                .Where(x => x.JournalEntry != null && x.JournalEntry.CompanyId == companyId && x.JournalEntry.IsPosted && x.JournalEntry.EntryDate <= asOfDate)
                .GroupBy(x => new
                {
                    x.AccountId,
                    x.Account!.Code,
                    x.Account.Name,
                    x.Account.AccountType
                })
                .Select(g => new
                {
                    g.Key.AccountId,
                    g.Key.Code,
                    g.Key.Name,
                    g.Key.AccountType,
                    TotalDebit = g.Sum(x => x.Debit),
                    TotalCredit = g.Sum(x => x.Credit)
                })
                .ToListAsync();

            // Combine accounts that have either an opening balance or any movement.
            var openingAccounts = await _db.Accounts
                .AsNoTracking()
                .ToListAsync();

            var movementByAccount = movementRows.ToDictionary(x => x.AccountId);

            var resultRows = new List<TrialBalanceRowDto>();

            foreach (var account in openingAccounts)
            {
                var openingBalance = openingSnapshot.GetEffectiveOpeningBalance(account.Id);
                movementByAccount.TryGetValue(account.Id, out var movement);

                var totalDebit = movement?.TotalDebit ?? 0m;
                var totalCredit = movement?.TotalCredit ?? 0m;

                // Closing natural balance = opening (natural) + movement (natural).
                var movementNatural = GetNaturalBalance(account.AccountType, totalDebit, totalCredit);
                var natural = openingBalance + movementNatural;

                var debitBalance = 0m;
                var creditBalance = 0m;

                if (IsDebitNature(account.AccountType))
                {
                    if (natural > 0m) debitBalance = natural;
                    else if (natural < 0m) creditBalance = -natural;
                }
                else
                {
                    if (natural > 0m) creditBalance = natural;
                    else if (natural < 0m) debitBalance = -natural;
                }

                if (debitBalance == 0m && creditBalance == 0m)
                    continue;

                resultRows.Add(new TrialBalanceRowDto
                {
                    AccountId = account.Id,
                    AccountCode = account.Code,
                    AccountName = account.Name,
                    AccountType = account.AccountType,
                    NetBalance = natural,
                    DebitBalance = debitBalance,
                    CreditBalance = creditBalance
                });
            }

            resultRows = resultRows.OrderBy(x => x.AccountCode).ToList();

            return new TrialBalanceReportDto
            {
                AsOfDate = asOfDate,
                Rows = resultRows,
                TotalDebit = resultRows.Sum(x => x.DebitBalance),
                TotalCredit = resultRows.Sum(x => x.CreditBalance)
            };
        }

        public async Task<ProfitAndLossReportDto> GetProfitAndLossAsync(int companyId, DateTime startDate, DateTime endDate)
        {
            var rows = await _db.JournalEntryLines
                .Include(x => x.Account)
                .Include(x => x.JournalEntry)
                .Where(x =>
                    x.JournalEntry != null &&
                    x.JournalEntry.CompanyId == companyId &&
                    x.JournalEntry.IsPosted &&
                    // The year-end closing entry empties the result accounts into retained
                    // earnings; counting it would show every closed year as nil.
                    x.JournalEntry.JournalType != PeriodClosingService.ClosingJournalType &&
                    x.JournalEntry.EntryDate >= startDate &&
                    x.JournalEntry.EntryDate <= endDate)
                .Where(x => x.Account != null && (x.Account.AccountType == "Revenue" || x.Account.AccountType == "Expense"))
                .GroupBy(x => new
                {
                    x.AccountId,
                    x.Account!.Code,
                    x.Account.Name,
                    x.Account.AccountType
                })
                .Select(g => new
                {
                    g.Key.AccountId,
                    g.Key.Code,
                    g.Key.Name,
                    g.Key.AccountType,
                    TotalDebit = g.Sum(x => x.Debit),
                    TotalCredit = g.Sum(x => x.Credit)
                })
                .OrderBy(x => x.Code)
                .ToListAsync();

            // Each account's income statement line: its own, its group's, or the default.
            var lines = await StatementLineResolver.ResolveAllAsync(_db, companyId);
            string LineOf(int accountId, string type) =>
                lines.TryGetValue(accountId, out var line) ? line : type == "Revenue" ? StatementLines.OtherIncome : StatementLines.FixedCosts;

            var revenueRows = rows
                .Where(x => x.AccountType == "Revenue")
                .Select(x => new ProfitAndLossRowDto
                {
                    AccountId = x.AccountId,
                    AccountCode = x.Code,
                    AccountName = x.Name,
                    StatementLine = LineOf(x.AccountId, x.AccountType),
                    Amount = x.TotalCredit - x.TotalDebit
                })
                .Where(x => x.Amount != 0m)
                .ToList();

            var expenseRows = rows
                .Where(x => x.AccountType == "Expense")
                .Select(x => new ProfitAndLossRowDto
                {
                    AccountId = x.AccountId,
                    AccountCode = x.Code,
                    AccountName = x.Name,
                    StatementLine = LineOf(x.AccountId, x.AccountType),
                    Amount = x.TotalDebit - x.TotalCredit
                })
                .Where(x => x.Amount != 0m)
                .ToList();

            var totalRevenue = revenueRows.Sum(x => x.Amount);
            var totalExpense = expenseRows.Sum(x => x.Amount);

            return new ProfitAndLossReportDto
            {
                StartDate = startDate,
                EndDate = endDate,
                Revenue = revenueRows,
                Expenses = expenseRows,
                TotalRevenue = totalRevenue,
                TotalExpenses = totalExpense,
                NetProfitOrLoss = totalRevenue - totalExpense
            };
        }

        public async Task<BalanceSheetReportDto> GetBalanceSheetAsync(int companyId, DateTime asOfDate)
        {
            var openingSnapshot = await _openingBalanceService.BuildSnapshotAsync();

            var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                throw new InvalidOperationException("Company record was not found.");

            var startMonth = company.FiscalYearStartMonth;
            var yearStart = asOfDate.Month >= startMonth
                ? new DateTime(asOfDate.Year, startMonth, 1)
                : new DateTime(asOfDate.Year - 1, startMonth, 1);

            var movements = await _db.JournalEntryLines
                .Include(x => x.Account)
                .Include(x => x.JournalEntry)
                .Where(x => x.JournalEntry != null && x.JournalEntry.CompanyId == companyId && x.JournalEntry.IsPosted && x.JournalEntry.EntryDate <= asOfDate)
                // This year's own closing entry is left out: its result is shown as the
                // current year's profit below, which the P&L computes without it. Earlier
                // years' closing entries stay - they are what put those results into
                // retained earnings.
                .Where(x => x.JournalEntry!.JournalType != PeriodClosingService.ClosingJournalType || x.JournalEntry.EntryDate < yearStart)
                .Where(x => x.Account != null && (
                    x.Account.AccountType == "Asset" ||
                    x.Account.AccountType == "Liability" ||
                    x.Account.AccountType == "Equity"))
                .GroupBy(x => new
                {
                    x.AccountId,
                    x.Account!.Code,
                    x.Account.Name,
                    x.Account.AccountType
                })
                .Select(g => new
                {
                    g.Key.AccountId,
                    g.Key.Code,
                    g.Key.Name,
                    g.Key.AccountType,
                    TotalDebit = g.Sum(x => x.Debit),
                    TotalCredit = g.Sum(x => x.Credit)
                })
                .ToListAsync();

            // Build a unified per-account map that includes accounts having either
            // an opening balance or any posted movement up to the as-of date.
            var balanceSheetAccounts = await _db.Accounts
                .AsNoTracking()
                .Where(x => x.AccountType == "Asset" || x.AccountType == "Liability" || x.AccountType == "Equity")
                .ToListAsync();

            var movementByAccount = movements.ToDictionary(x => x.AccountId);

            var combined = balanceSheetAccounts.Select(account =>
            {
                var openingNatural = openingSnapshot.GetEffectiveOpeningBalance(account.Id);
                movementByAccount.TryGetValue(account.Id, out var movement);
                var movementNatural = GetNaturalBalance(account.AccountType, movement?.TotalDebit ?? 0m, movement?.TotalCredit ?? 0m);
                var natural = openingNatural + movementNatural;

                return new
                {
                    AccountId = account.Id,
                    Code = account.Code,
                    Name = account.Name,
                    AccountType = account.AccountType,
                    NaturalBalance = natural
                };
            }).ToList();

            var assets = combined
                .Where(x => x.AccountType == "Asset")
                .Select(x => new BalanceSheetRowDto
                {
                    AccountId = x.AccountId,
                    AccountCode = x.Code,
                    AccountName = x.Name,
                    Amount = x.NaturalBalance
                })
                .Where(x => x.Amount != 0m)
                .OrderBy(x => x.AccountCode)
                .ToList();

            var liabilities = combined
                .Where(x => x.AccountType == "Liability")
                .Select(x => new BalanceSheetRowDto
                {
                    AccountId = x.AccountId,
                    AccountCode = x.Code,
                    AccountName = x.Name,
                    Amount = x.NaturalBalance
                })
                .Where(x => x.Amount != 0m)
                .OrderBy(x => x.AccountCode)
                .ToList();

            var equityRows = combined
                .Where(x => x.AccountType == "Equity")
                .Select(x => new BalanceSheetRowDto
                {
                    AccountId = x.AccountId,
                    AccountCode = x.Code,
                    AccountName = x.Name,
                    Amount = x.NaturalBalance
                })
                .Where(x => x.Amount != 0m)
                .OrderBy(x => x.AccountCode)
                .ToList();

            var currentYearPnL = await GetProfitAndLossAsync(companyId, yearStart, asOfDate);

            var shareCapital = equityRows
                .Where(x => x.AccountCode == "31100")
                .Sum(x => x.Amount);

            var retainedEarningsPriorYears = equityRows
                .Where(x => x.AccountCode == "32100")
                .Sum(x => x.Amount);

            var otherEquityRows = equityRows
                .Where(x => x.AccountCode != "31100" && x.AccountCode != "32100")
                .ToList();

            var otherEquity = otherEquityRows.Sum(x => x.Amount);
            var currentYearProfitOrLoss = currentYearPnL.NetProfitOrLoss;

            return new BalanceSheetReportDto
            {
                AsOfDate = asOfDate,
                Assets = assets,
                Liabilities = liabilities,
                OtherEquityRows = otherEquityRows,
                ShareCapital = shareCapital,
                RetainedEarningsPriorYears = retainedEarningsPriorYears,
                OtherEquity = otherEquity,
                CurrentYearProfitOrLoss = currentYearProfitOrLoss,
                TotalAssets = assets.Sum(x => x.Amount),
                TotalLiabilities = liabilities.Sum(x => x.Amount),
                TotalEquityBeforeCurrentYear = shareCapital + retainedEarningsPriorYears + otherEquity,
                TotalEquity = shareCapital + retainedEarningsPriorYears + otherEquity + currentYearProfitOrLoss
            };
        }

        internal static bool IsDebitNature(string accountType)
        {
            return accountType == "Asset" || accountType == "Expense";
        }

        private static decimal GetNaturalBalance(string accountType, decimal totalDebit, decimal totalCredit)
        {
            return IsDebitNature(accountType)
                ? totalDebit - totalCredit
                : totalCredit - totalDebit;
        }
    }

    public class TrialBalanceReportDto
    {
        public DateTime AsOfDate { get; set; }
        public List<TrialBalanceRowDto> Rows { get; set; } = new();
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
    }

    public class TrialBalanceRowDto
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public decimal NetBalance { get; set; }
        public decimal DebitBalance { get; set; }
        public decimal CreditBalance { get; set; }
    }

    public class ProfitAndLossReportDto
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<ProfitAndLossRowDto> Revenue { get; set; } = new();
        public List<ProfitAndLossRowDto> Expenses { get; set; } = new();
        public decimal TotalRevenue { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetProfitOrLoss { get; set; }

        // ---- The income statement layout, by statement line ----------------

        public IEnumerable<ProfitAndLossRowDto> RowsOf(string line) =>
            (StatementLines.IsRevenueLine(line) ? Revenue : Expenses).Where(x => x.StatementLine == line);

        public decimal TotalOf(string line) => RowsOf(line).Sum(x => x.Amount);

        public decimal CostOfRevenue => TotalOf(StatementLines.CostOfRevenue);
        public decimal GrossProfit => TotalRevenue - CostOfRevenue;
        public decimal OperatingCosts => TotalOf(StatementLines.FixedCosts) + TotalOf(StatementLines.RecurringCosts);
        public decimal OperatingProfit => GrossProfit - OperatingCosts;
        public decimal FinanceCosts => TotalOf(StatementLines.FinanceCosts);
    }

    public class ProfitAndLossRowDto
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;

        /// <summary>The income statement line (see StatementLines).</summary>
        public string StatementLine { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class BalanceSheetReportDto
    {
        public DateTime AsOfDate { get; set; }
        public List<BalanceSheetRowDto> Assets { get; set; } = new();
        public List<BalanceSheetRowDto> Liabilities { get; set; } = new();
        public List<BalanceSheetRowDto> OtherEquityRows { get; set; } = new();
        public decimal ShareCapital { get; set; }
        public decimal RetainedEarningsPriorYears { get; set; }
        public decimal OtherEquity { get; set; }
        public decimal CurrentYearProfitOrLoss { get; set; }
        public decimal TotalAssets { get; set; }
        public decimal TotalLiabilities { get; set; }
        public decimal TotalEquityBeforeCurrentYear { get; set; }
        public decimal TotalEquity { get; set; }
        public decimal TotalLiabilitiesAndEquity => TotalLiabilities + TotalEquity;
    }

    public class BalanceSheetRowDto
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}
