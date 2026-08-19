using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Statement of Cash Flows, IAS 7, DIRECT method.
    ///
    /// The direct method is used deliberately rather than the indirect one. IAS 7.19
    /// encourages it, and here it is also far more reliable: every cash movement is
    /// derived from the ledger itself and classified by looking at what the other side
    /// of the journal was. The statement therefore reconciles by construction -
    /// opening cash plus the movements always equals closing cash - instead of relying
    /// on a chain of working-capital adjustments that can silently fail to tie out.
    ///
    /// Classification of each movement comes from the counterpart account:
    ///
    ///   Equity accounts                         -> Financing   (capital, owner drawings)
    ///   Long-term asset accounts (15000+)       -> Investing   (property, fixed assets)
    ///   Everything else                         -> Operating   (rent, expenses, VAT, deposits)
    ///
    /// A journal whose only lines are cash accounts - a petty cash top-up out of the
    /// bank, for example - is a transfer between two pockets of cash, not a cash flow,
    /// and is excluded. Including it would show a matching inflow and outflow and
    /// overstate both.
    /// </summary>
    public class CashFlowService
    {
        private readonly ApplicationDbContext _db;

        /// <summary>
        /// Asset codes at or above this are treated as long-term and therefore
        /// investing: investment property, fixed assets, accumulated depreciation and
        /// the asset clearing account.
        /// </summary>
        private const string LongTermAssetCodeFloor = "15000";

        public CashFlowService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<CashFlowReportDto> GetCashFlowAsync(int companyId, DateTime startDate, DateTime endDate)
        {
            var company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId);
            var settings = await _db.CompanyAccountSettings.AsNoTracking()
                .FirstOrDefaultAsync(x => x.CompanyId == companyId);

            // What counts as cash: the mapped bank/cash account plus every petty cash
            // float. PDC On Hand is deliberately NOT cash - a cheque in the drawer has
            // not been banked, and IAS 7 requires cash equivalents to be readily
            // convertible with insignificant risk of change in value.
            var cashAccounts = await _db.Accounts
                .AsNoTracking()
                .Where(x => x.IsPettyCashAccount
                            || (settings != null && x.Id == settings.DefaultCashAccountId))
                .Select(x => new { x.Id, x.Code, x.Name, x.OpeningBalance })
                .ToListAsync();

            var report = new CashFlowReportDto
            {
                StartDate = startDate,
                EndDate = endDate,
                CompanyName = company.Name,
                Currency = company.BaseCurrency,
                HasCashAccounts = cashAccounts.Count > 0
            };

            if (cashAccounts.Count == 0)
                return report;

            var cashAccountIds = cashAccounts.Select(x => x.Id).ToHashSet();

            report.CashAccounts = cashAccounts
                .Select(x => new CashFlowAccountDto { AccountCode = x.Code, AccountName = x.Name })
                .OrderBy(x => x.AccountCode)
                .ToList();

            // ----- OPENING CASH ------------------------------------------------
            var priorMovement = await _db.JournalEntryLines
                .AsNoTracking()
                .Where(x => x.JournalEntry != null
                            && x.JournalEntry.CompanyId == companyId
                            && x.JournalEntry.IsPosted
                            && x.JournalEntry.EntryDate < startDate
                            && cashAccountIds.Contains(x.AccountId))
                .SumAsync(x => (decimal?)(x.Debit - x.Credit)) ?? 0m;

            report.OpeningCash = cashAccounts.Sum(x => x.OpeningBalance) + priorMovement;

            // ----- MOVEMENTS IN THE PERIOD -------------------------------------
            // Every journal that touched cash, with all of its lines, so each movement
            // can be explained by its counterpart.
            var cashJournalIds = await _db.JournalEntryLines
                .AsNoTracking()
                .Where(x => x.JournalEntry != null
                            && x.JournalEntry.CompanyId == companyId
                            && x.JournalEntry.IsPosted
                            && x.JournalEntry.EntryDate >= startDate
                            && x.JournalEntry.EntryDate <= endDate
                            && cashAccountIds.Contains(x.AccountId))
                .Select(x => x.JournalEntryId)
                .Distinct()
                .ToListAsync();

            var lines = await _db.JournalEntryLines
                .AsNoTracking()
                .Where(x => cashJournalIds.Contains(x.JournalEntryId))
                .Select(x => new
                {
                    x.JournalEntryId,
                    x.AccountId,
                    AccountCode = x.Account!.Code,
                    AccountName = x.Account.Name,
                    x.Account.AccountType,
                    x.Debit,
                    x.Credit
                })
                .ToListAsync();

            var buckets = new Dictionary<(string Section, string Code), CashFlowLineDto>();

            foreach (var journalGroup in lines.GroupBy(x => x.JournalEntryId))
            {
                var journalLines = journalGroup.ToList();

                // Net cash effect of this journal. Debit increases cash.
                var netCash = journalLines
                    .Where(x => cashAccountIds.Contains(x.AccountId))
                    .Sum(x => x.Debit - x.Credit);

                if (netCash == 0m)
                    continue; // transfer between cash accounts - not a cash flow

                var counterparts = journalLines
                    .Where(x => !cashAccountIds.Contains(x.AccountId))
                    .ToList();

                if (counterparts.Count == 0)
                    continue;

                // Allocate the cash movement across the counterparts in proportion to
                // their size, so a journal with several counterpart accounts is split
                // rather than arbitrarily assigned to the first one.
                var counterpartTotal = counterparts.Sum(x => Math.Abs(x.Debit - x.Credit));
                if (counterpartTotal == 0m)
                    continue;

                foreach (var counterpart in counterparts)
                {
                    var weight = Math.Abs(counterpart.Debit - counterpart.Credit) / counterpartTotal;
                    var amount = Math.Round(netCash * weight, 2);

                    if (amount == 0m)
                        continue;

                    var section = ClassifySection(counterpart.AccountType, counterpart.AccountCode);
                    var key = (section, counterpart.AccountCode);

                    if (!buckets.TryGetValue(key, out var bucket))
                    {
                        bucket = new CashFlowLineDto
                        {
                            Section = section,
                            AccountCode = counterpart.AccountCode,
                            AccountName = counterpart.AccountName
                        };
                        buckets[key] = bucket;
                    }

                    bucket.Amount += amount;
                    bucket.MovementCount++;
                }
            }

            var allLines = buckets.Values
                .Where(x => x.Amount != 0m)
                .OrderBy(x => x.AccountCode)
                .ToList();

            report.Operating = allLines.Where(x => x.Section == CashFlowSections.Operating).ToList();
            report.Investing = allLines.Where(x => x.Section == CashFlowSections.Investing).ToList();
            report.Financing = allLines.Where(x => x.Section == CashFlowSections.Financing).ToList();

            // ----- CLOSING CASH, CHECKED AGAINST THE LEDGER --------------------
            var periodMovement = await _db.JournalEntryLines
                .AsNoTracking()
                .Where(x => x.JournalEntry != null
                            && x.JournalEntry.CompanyId == companyId
                            && x.JournalEntry.IsPosted
                            && x.JournalEntry.EntryDate >= startDate
                            && x.JournalEntry.EntryDate <= endDate
                            && cashAccountIds.Contains(x.AccountId))
                .SumAsync(x => (decimal?)(x.Debit - x.Credit)) ?? 0m;

            report.ActualNetMovement = periodMovement;
            report.ClosingCash = report.OpeningCash + periodMovement;

            return report;
        }

        /// <summary>
        /// Which section of the statement a cash movement belongs in, judged by the
        /// account on the other side of the journal.
        /// </summary>
        private static string ClassifySection(string accountType, string accountCode)
        {
            // Capital introduced, drawings, dividends.
            if (accountType == "Equity")
                return CashFlowSections.Financing;

            // Buying or selling long-lived assets.
            if (accountType == "Asset" &&
                string.CompareOrdinal(accountCode, LongTermAssetCodeFloor) >= 0)
            {
                return CashFlowSections.Investing;
            }

            // Trading: rent received, suppliers paid, VAT, deposits, working capital.
            return CashFlowSections.Operating;
        }
    }

    public static class CashFlowSections
    {
        public const string Operating = "Operating";
        public const string Investing = "Investing";
        public const string Financing = "Financing";
    }

    public class CashFlowAccountDto
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
    }

    public class CashFlowLineDto
    {
        public string Section { get; set; } = string.Empty;
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;

        /// <summary>Positive is cash in, negative is cash out.</summary>
        public decimal Amount { get; set; }

        public int MovementCount { get; set; }
        public bool IsInflow => Amount > 0m;
    }

    public class CashFlowReportDto
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public bool HasCashAccounts { get; set; }

        public List<CashFlowAccountDto> CashAccounts { get; set; } = new();

        public List<CashFlowLineDto> Operating { get; set; } = new();
        public List<CashFlowLineDto> Investing { get; set; } = new();
        public List<CashFlowLineDto> Financing { get; set; } = new();

        public decimal OperatingTotal => Operating.Sum(x => x.Amount);
        public decimal InvestingTotal => Investing.Sum(x => x.Amount);
        public decimal FinancingTotal => Financing.Sum(x => x.Amount);

        public decimal NetMovement => OperatingTotal + InvestingTotal + FinancingTotal;

        public decimal OpeningCash { get; set; }
        public decimal ClosingCash { get; set; }

        /// <summary>
        /// Movement taken straight from the cash accounts. Compared against the sum of
        /// the classified sections as a self-check: rounding aside, they must agree.
        /// </summary>
        public decimal ActualNetMovement { get; set; }

        public decimal ReconcilingDifference => NetMovement - ActualNetMovement;

        /// <summary>Allows for rounding when the allocation splits across counterparts.</summary>
        public bool IsReconciled => Math.Abs(ReconcilingDifference) < 0.05m;

        public bool HasActivity => Operating.Count > 0 || Investing.Count > 0 || Financing.Count > 0;
    }
}
