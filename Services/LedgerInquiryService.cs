using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    public class LedgerInquiryService
    {
        private readonly ApplicationDbContext _db;
        private readonly OpeningBalanceService _openingBalanceService;

        public LedgerInquiryService(ApplicationDbContext db, OpeningBalanceService openingBalanceService)
        {
            _db = db;
            _openingBalanceService = openingBalanceService;
        }

        public async Task<JournalInquiryViewModel> GetJournalInquiryAsync(
            DateTime? fromDate,
            DateTime? toDate,
            string? sourceType,
            bool postedOnly)
        {
            var query = _db.JournalEntries
                .Include(x => x.Lines)
                    .ThenInclude(x => x.Account)
                // Needed so the inquiry can show period status and decide whether the
                // Post / Reset to Draft buttons apply.
                .Include(x => x.AccountingPeriod)
                .AsQueryable();

            if (fromDate.HasValue)
            {
                query = query.Where(x => x.EntryDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(x => x.EntryDate <= toDate.Value);
            }

            if (!string.IsNullOrWhiteSpace(sourceType))
            {
                query = query.Where(x => x.SourceType == sourceType);
            }

            if (postedOnly)
            {
                query = query.Where(x => x.IsPosted);
            }

            var entries = await query
                .OrderByDescending(x => x.EntryDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return new JournalInquiryViewModel
            {
                FromDate = fromDate,
                ToDate = toDate,
                SourceType = sourceType,
                PostedOnly = postedOnly,
                Entries = entries.Select(x => new JournalInquiryItemViewModel
                {
                    JournalEntryId = x.Id,
                    EntryNumber = x.EntryNumber,
                    EntryDate = x.EntryDate,
                    Description = x.Description,
                    SourceType = x.SourceType,
                    SourceId = x.SourceId,
                    IsPosted = x.IsPosted,
                    PeriodName = x.AccountingPeriod != null ? x.AccountingPeriod.Name : null,
                    PeriodClosed = x.AccountingPeriod != null && x.AccountingPeriod.IsClosed,
                    TotalDebit = x.Lines.Sum(l => l.Debit),
                    TotalCredit = x.Lines.Sum(l => l.Credit),
                    Lines = x.Lines
                        .OrderBy(l => l.Account!.Code)
                        .Select(l => new JournalInquiryLineViewModel
                        {
                            AccountCode = l.Account?.Code ?? string.Empty,
                            AccountName = l.Account?.Name ?? string.Empty,
                            Description = l.Description,
                            Debit = l.Debit,
                            Credit = l.Credit
                        })
                        .ToList()
                }).ToList()
            };
        }

        public async Task<GeneralLedgerViewModel> GetGeneralLedgerAsync(int accountId, DateTime fromDate, DateTime toDate)
        {
            var account = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == accountId);
            if (account == null)
                throw new InvalidOperationException("Account was not found.");

            // Effective opening balance set on the GL account / sub-ledger
            // (zero for accounts that have no opening balance configured).
            var openingBalanceFromSetup = await _openingBalanceService.GetEffectiveOpeningBalanceAsync(accountId);

            // Plus all posted journal movement strictly before the from-date.
            var openingLines = await _db.JournalEntryLines
                .Include(x => x.JournalEntry)
                .Where(x =>
                    x.AccountId == accountId &&
                    x.JournalEntry != null &&
                    x.JournalEntry.IsPosted &&
                    x.JournalEntry.EntryDate < fromDate)
                .ToListAsync();

            var openingDebit = openingLines.Sum(x => x.Debit);
            var openingCredit = openingLines.Sum(x => x.Credit);
            var openingBalance = openingBalanceFromSetup
                + GetNaturalBalance(account.AccountType, openingDebit, openingCredit);

            var periodLines = await _db.JournalEntryLines
                .Include(x => x.JournalEntry)
                .Where(x =>
                    x.AccountId == accountId &&
                    x.JournalEntry != null &&
                    x.JournalEntry.IsPosted &&
                    x.JournalEntry.EntryDate >= fromDate &&
                    x.JournalEntry.EntryDate <= toDate)
                .OrderBy(x => x.JournalEntry!.EntryDate)
                .ThenBy(x => x.JournalEntry!.Id)
                .ThenBy(x => x.Id)
                .ToListAsync();

            var rows = new List<GeneralLedgerRowViewModel>();
            var runningBalance = openingBalance;

            foreach (var line in periodLines)
            {
                var movement = GetNaturalBalance(account.AccountType, line.Debit, line.Credit);
                runningBalance += movement;

                rows.Add(new GeneralLedgerRowViewModel
                {
                    JournalEntryId = line.JournalEntry!.Id,
                    EntryDate = line.JournalEntry.EntryDate,
                    EntryNumber = line.JournalEntry.EntryNumber,
                    SourceType = line.JournalEntry.SourceType,
                    Description = string.IsNullOrWhiteSpace(line.Description)
                        ? line.JournalEntry.Description
                        : line.Description!,
                    Debit = line.Debit,
                    Credit = line.Credit,
                    RunningBalance = runningBalance
                });
            }

            return new GeneralLedgerViewModel
            {
                AccountId = account.Id,
                AccountCode = account.Code,
                AccountName = account.Name,
                AccountType = account.AccountType,
                FromDate = fromDate,
                ToDate = toDate,
                OpeningBalance = openingBalance,
                TotalDebit = periodLines.Sum(x => x.Debit),
                TotalCredit = periodLines.Sum(x => x.Credit),
                ClosingBalance = runningBalance,
                Rows = rows
            };
        }

        private static bool IsDebitNature(string accountType)
        {
            return accountType == "Asset" || accountType == "Expense";
        }

        private static decimal GetNaturalBalance(string accountType, decimal debit, decimal credit)
        {
            return IsDebitNature(accountType)
                ? debit - credit
                : credit - debit;
        }
    }
}