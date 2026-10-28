using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Petty cash float control.
    ///
    /// A petty cash float is an ordinary asset account in the chart of accounts
    /// flagged with <see cref="Account.IsPettyCashAccount"/>. Flagging more than one
    /// account gives you several independent floats - one per office or custodian -
    /// each with its own opening balance and running balance.
    ///
    /// Everything posts through the normal general ledger, so a petty cash voucher is
    /// a real journal entry: it appears in Journal Inquiry, in the General Ledger and
    /// in the Trial Balance like any other. There is no separate ledger to reconcile.
    ///
    ///   Expense      Dr expense account   /  Cr petty cash    (float goes down)
    ///   Replenish    Dr petty cash        /  Cr bank or cash  (float goes back up)
    /// </summary>
    public class PettyCashService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentCompanyAccessor _currentCompanyAccessor;

        private readonly OwnerRechargeService _recharges;

        public PettyCashService(ApplicationDbContext db, ICurrentCompanyAccessor currentCompanyAccessor, OwnerRechargeService recharges)
        {
            _db = db;
            _currentCompanyAccessor = currentCompanyAccessor;
            _recharges = recharges;
        }

        // =====================================================================
        //  READ
        // =====================================================================

        /// <summary>
        /// Builds the petty cash screen: every float with its balance, plus the
        /// vouchers posted in the requested period and a summary by expense account.
        /// </summary>
        public async Task<PettyCashDashboardViewModel> GetDashboardAsync(
            int companyId,
            DateTime startDate,
            DateTime endDate,
            int? pettyCashAccountId = null)
        {
            var floatAccounts = await _db.Accounts
                .AsNoTracking()
                .Where(x => x.IsPettyCashAccount)
                .OrderBy(x => x.Code)
                .Select(x => new { x.Id, x.Code, x.Name, x.OpeningBalance, x.IsActive })
                .ToListAsync();

            var model = new PettyCashDashboardViewModel
            {
                StartDate = startDate,
                EndDate = endDate,
                SelectedPettyCashAccountId = pettyCashAccountId,
                HasFloats = floatAccounts.Count > 0
            };

            if (floatAccounts.Count == 0)
                return model;

            var floatIds = floatAccounts.Select(x => x.Id).ToList();

            // Every line ever posted to a float, up to the end of the period. The
            // running balance needs history, not just the period.
            var floatLines = await _db.JournalEntryLines
                .AsNoTracking()
                .Where(x =>
                    x.JournalEntry != null &&
                    x.JournalEntry.CompanyId == companyId &&
                    x.JournalEntry.IsPosted &&
                    x.JournalEntry.EntryDate <= endDate &&
                    floatIds.Contains(x.AccountId))
                .Select(x => new
                {
                    x.JournalEntryId,
                    x.AccountId,
                    EntryDate = x.JournalEntry!.EntryDate,
                    EntryNumber = x.JournalEntry.EntryNumber,
                    DocumentNumber = x.JournalEntry.DocumentNumber,
                    ReferenceNumber = x.JournalEntry.ReferenceNumber,
                    JournalDescription = x.JournalEntry.Description,
                    LineDescription = x.Description,
                    x.Debit,
                    x.Credit
                })
                .ToListAsync();

            foreach (var account in floatAccounts)
            {
                var prior = floatLines.Where(l => l.AccountId == account.Id && l.EntryDate < startDate);
                var period = floatLines.Where(l => l.AccountId == account.Id && l.EntryDate >= startDate).ToList();

                // Petty cash is an asset: debit increases, credit decreases.
                var opening = account.OpeningBalance + prior.Sum(l => l.Debit - l.Credit);
                var replenished = period.Sum(l => l.Debit);
                var spent = period.Sum(l => l.Credit);

                model.Floats.Add(new PettyCashFloatViewModel
                {
                    AccountId = account.Id,
                    AccountCode = account.Code,
                    AccountName = account.Name,
                    IsActive = account.IsActive,
                    OpeningBalance = opening,
                    Replenishments = replenished,
                    Expenses = spent,
                    ClosingBalance = opening + replenished - spent
                });
            }

            // The expense side of each voucher: the counterpart lines on the same
            // journal entries, which is what the custodian actually spent money on.
            var voucherJournalIds = floatLines
                .Where(l => l.EntryDate >= startDate)
                .Select(l => l.JournalEntryId)
                .Distinct()
                .ToList();

            var counterpartLines = await _db.JournalEntryLines
                .AsNoTracking()
                .Where(x => voucherJournalIds.Contains(x.JournalEntryId) && !floatIds.Contains(x.AccountId))
                .Select(x => new
                {
                    x.JournalEntryId,
                    x.AccountId,
                    AccountCode = x.Account!.Code,
                    AccountName = x.Account.Name,
                    AccountType = x.Account.AccountType,
                    x.Debit,
                    x.Credit
                })
                .ToListAsync();

            var periodFloatLines = floatLines
                .Where(l => l.EntryDate >= startDate)
                .Where(l => !pettyCashAccountId.HasValue || l.AccountId == pettyCashAccountId.Value)
                .OrderBy(l => l.EntryDate)
                .ThenBy(l => l.EntryNumber)
                .ToList();

            foreach (var line in periodFloatLines)
            {
                var counterparts = counterpartLines.Where(c => c.JournalEntryId == line.JournalEntryId).ToList();
                var floatAccount = floatAccounts.First(f => f.Id == line.AccountId);

                model.Vouchers.Add(new PettyCashVoucherViewModel
                {
                    JournalEntryId = line.JournalEntryId,
                    EntryDate = line.EntryDate,
                    EntryNumber = line.EntryNumber,
                    VoucherNumber = line.DocumentNumber,
                    ReferenceNumber = line.ReferenceNumber,
                    Description = string.IsNullOrWhiteSpace(line.LineDescription)
                        ? line.JournalDescription
                        : line.LineDescription,
                    PettyCashAccountDisplay = $"{floatAccount.Code} - {floatAccount.Name}",
                    CounterpartDisplay = counterparts.Count == 0
                        ? "-"
                        : string.Join(", ", counterparts.Select(c => $"{c.AccountCode} {c.AccountName}")),
                    ExpenseAmount = line.Credit,
                    ReplenishmentAmount = line.Debit
                });
            }

            // Summary by expense account - only the counterparts of money going out.
            var expenseJournalIds = periodFloatLines
                .Where(l => l.Credit > 0m)
                .Select(l => l.JournalEntryId)
                .ToHashSet();

            model.ExpenseSummary = counterpartLines
                .Where(c => expenseJournalIds.Contains(c.JournalEntryId))
                .GroupBy(c => new { c.AccountId, c.AccountCode, c.AccountName })
                .Select(g => new PettyCashExpenseSummaryViewModel
                {
                    AccountId = g.Key.AccountId,
                    AccountCode = g.Key.AccountCode,
                    AccountName = g.Key.AccountName,
                    Amount = g.Sum(x => x.Debit - x.Credit),
                    VoucherCount = g.Select(x => x.JournalEntryId).Distinct().Count()
                })
                .Where(x => x.Amount != 0m)
                .OrderByDescending(x => x.Amount)
                .ToList();

            return model;
        }

        /// <summary>Accounts flagged as petty cash floats.</summary>
        public async Task<List<Account>> GetFloatAccountsAsync()
        {
            return await _db.Accounts
                .AsNoTracking()
                .Where(x => x.IsPettyCashAccount && x.IsActive)
                .OrderBy(x => x.Code)
                .ToListAsync();
        }

        /// <summary>Postable expense accounts, for the expense selector.</summary>
        public async Task<List<Account>> GetExpenseAccountsAsync()
        {
            return await _db.Accounts
                .AsNoTracking()
                .Where(x => x.AccountType == "Expense" && x.IsPostable && x.IsActive && !x.IsGroup)
                .OrderBy(x => x.Code)
                .ToListAsync();
        }

        /// <summary>Accounts a float can be topped up from - cash and bank.</summary>
        public async Task<List<Account>> GetFundingAccountsAsync()
        {
            return await _db.Accounts
                .AsNoTracking()
                .Where(x => x.AccountType == "Asset" && x.IsPostable && x.IsActive && !x.IsGroup && !x.IsPettyCashAccount)
                .OrderBy(x => x.Code)
                .ToListAsync();
        }

        // =====================================================================
        //  POST
        // =====================================================================

        /// <summary>
        /// Posts a petty cash expense voucher: Dr expense account, Cr petty cash.
        /// </summary>
        public async Task<int> PostExpenseAsync(PettyCashExpenseViewModel model)
        {
            var companyId = _currentCompanyAccessor.CompanyId
                ?? throw new InvalidOperationException("No company is selected for the current user.");

            if (model.Amount <= 0m)
                throw new InvalidOperationException("Amount must be greater than zero.");

            var pettyCashAccount = await _db.Accounts
                .FirstOrDefaultAsync(x => x.Id == model.PettyCashAccountId && x.IsPettyCashAccount)
                ?? throw new InvalidOperationException("The selected petty cash account was not found, or it is not flagged as a petty cash float.");

            var expenseAccount = await _db.Accounts
                .FirstOrDefaultAsync(x => x.Id == model.ExpenseAccountId)
                ?? throw new InvalidOperationException("The selected expense account was not found.");

            if (!expenseAccount.IsPostable || expenseAccount.IsGroup)
                throw new InvalidOperationException("The selected expense account is a heading and cannot be posted to.");

            if (expenseAccount.IsPettyCashAccount)
                throw new InvalidOperationException("The expense account cannot be the petty cash float itself.");

            // Guard the float against going negative - a physical cash box cannot hold
            // less than nothing, so this almost always means a missing replenishment.
            var currentBalance = await GetFloatBalanceAsync(companyId, pettyCashAccount.Id, model.VoucherDate);
            if (model.Amount > currentBalance)
            {
                throw new InvalidOperationException(
                    $"This voucher would take the float below zero. Balance on {model.VoucherDate:dd/MM/yyyy} is " +
                    $"{currentBalance:N2} and the voucher is {model.Amount:N2}. Record a replenishment first.");
            }

            // Charged on to the owner of a managed property: the cost plus 5% VAT goes to Due
            // to Landlords on a recharge tax invoice, not to the expense account.
            Property? rechargeProperty = null;
            var rechargeVat = 0m;
            int? debitAccountId = expenseAccount.Id;
            if (model.RechargeToOwner)
            {
                rechargeProperty = model.PropertyId.HasValue ? await _db.Properties.AsNoTracking().FirstOrDefaultAsync(x => x.Id == model.PropertyId.Value) : null;
                var cannot = OwnerRechargeService.CannotRecharge(rechargeProperty);
                if (cannot != null)
                    throw new InvalidOperationException(cannot);
                var settings = await _db.CompanyAccountSettings.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId);
                debitAccountId = settings?.DueToLandlordsAccountId
                                 ?? throw new InvalidOperationException("Due to Landlords is not mapped in Account Mapping.");
                rechargeVat = OwnerRechargeService.VatOn(model.Amount);
            }

            var lines = new List<JournalEntryLine>
            {
                new()
                {
                    AccountId = debitAccountId.Value,
                    Debit = model.Amount + rechargeVat,
                    Credit = 0m,
                    Description = rechargeVat == 0m
                        ? BuildLineDescription(model.Description, model.PaidTo)
                        : $"{BuildLineDescription(model.Description, model.PaidTo)} - charged to the owner (incl. VAT {rechargeVat:N2})",
                    PropertyId = model.PropertyId,
                    UnitId = model.UnitId
                },
                new()
                {
                    AccountId = pettyCashAccount.Id,
                    Debit = 0m,
                    Credit = model.Amount,
                    Description = BuildLineDescription(model.Description, model.PaidTo),
                    PropertyId = model.PropertyId,
                    UnitId = model.UnitId
                }
            };

            if (rechargeVat != 0m)
            {
                var vatPayableId = await _db.CompanyAccountSettings.AsNoTracking().Where(x => x.CompanyId == companyId)
                    .Select(x => x.VatPayableAccountId).FirstOrDefaultAsync()
                    ?? throw new InvalidOperationException("VAT Payable is not mapped in Account Mapping.");
                lines.Add(new JournalEntryLine
                {
                    AccountId = vatPayableId,
                    Debit = 0m,
                    Credit = rechargeVat,
                    Description = $"Output VAT on {model.Description} recharged to the owner",
                    PropertyId = model.PropertyId
                });
            }

            var journalId = await CreateJournalAsync(
                companyId,
                model.VoucherDate,
                description: $"Petty cash expense - {model.Description}",
                sourceType: "PettyCashExpense",
                referenceNumber: model.ReferenceNumber,
                lines: lines);

            if (rechargeProperty != null)
                await _recharges.RecordAsync(companyId, OwnerRechargeSources.PettyCash, journalId, model.ReferenceNumber, rechargeProperty.Id, model.UnitId,
                    model.VoucherDate, $"Petty cash: {model.Description}" + (model.PaidTo != null ? $" (paid to {model.PaidTo})" : string.Empty),
                    model.Amount, journalId);

            return journalId;
        }

        /// <summary>
        /// Posts a replenishment: Dr petty cash, Cr the funding account.
        /// </summary>
        public async Task<int> PostReplenishmentAsync(PettyCashReplenishmentViewModel model)
        {
            var companyId = _currentCompanyAccessor.CompanyId
                ?? throw new InvalidOperationException("No company is selected for the current user.");

            if (model.Amount <= 0m)
                throw new InvalidOperationException("Amount must be greater than zero.");

            var pettyCashAccount = await _db.Accounts
                .FirstOrDefaultAsync(x => x.Id == model.PettyCashAccountId && x.IsPettyCashAccount)
                ?? throw new InvalidOperationException("The selected petty cash account was not found, or it is not flagged as a petty cash float.");

            var fundingAccount = await _db.Accounts
                .FirstOrDefaultAsync(x => x.Id == model.FundingAccountId)
                ?? throw new InvalidOperationException("The selected funding account was not found.");

            if (fundingAccount.Id == pettyCashAccount.Id)
                throw new InvalidOperationException("The funding account cannot be the petty cash float itself.");

            var lines = new List<JournalEntryLine>
            {
                new()
                {
                    AccountId = pettyCashAccount.Id,
                    Debit = model.Amount,
                    Credit = 0m,
                    Description = model.Description
                },
                new()
                {
                    AccountId = fundingAccount.Id,
                    Debit = 0m,
                    Credit = model.Amount,
                    Description = model.Description
                }
            };

            return await CreateJournalAsync(
                companyId,
                model.ReplenishmentDate,
                description: $"Petty cash replenishment - {model.Description}",
                sourceType: "PettyCashReplenishment",
                referenceNumber: model.ReferenceNumber,
                lines: lines);
        }

        // =====================================================================
        //  INTERNALS
        // =====================================================================

        private async Task<decimal> GetFloatBalanceAsync(int companyId, int accountId, DateTime asOfDate)
        {
            var opening = await _db.Accounts
                .Where(x => x.Id == accountId)
                .Select(x => x.OpeningBalance)
                .FirstAsync();

            var movement = await _db.JournalEntryLines
                .Where(x =>
                    x.JournalEntry != null &&
                    x.JournalEntry.CompanyId == companyId &&
                    x.JournalEntry.IsPosted &&
                    x.JournalEntry.EntryDate <= asOfDate &&
                    x.AccountId == accountId)
                .SumAsync(x => (decimal?)(x.Debit - x.Credit)) ?? 0m;

            return opening + movement;
        }

        private static string BuildLineDescription(string description, string? paidTo)
        {
            return string.IsNullOrWhiteSpace(paidTo)
                ? description
                : $"{description} (paid to {paidTo})";
        }

        /// <summary>
        /// Writes a balanced, posted journal after checking the accounting period is
        /// open. Mirrors ManualJournalService so petty cash behaves like every other
        /// posting route in the system.
        /// </summary>
        private async Task<int> CreateJournalAsync(
            int companyId,
            DateTime entryDate,
            string description,
            string sourceType,
            string? referenceNumber,
            List<JournalEntryLine> lines)
        {
            var date = entryDate.Date;

            var period = await _db.AccountingPeriods.FirstOrDefaultAsync(x =>
                x.CompanyId == companyId &&
                x.StartDate <= date &&
                x.EndDate >= date);

            if (period == null)
                throw new InvalidOperationException("No accounting period found for the selected date.");

            if (period.IsClosed)
                throw new InvalidOperationException("The accounting period is closed. The voucher cannot be posted.");

            var totalDebit = lines.Sum(x => x.Debit);
            var totalCredit = lines.Sum(x => x.Credit);

            if (totalDebit != totalCredit)
                throw new InvalidOperationException("Journal entry is not balanced.");

            var voucherNumber = await GenerateVoucherNumberAsync(companyId, date);

            var entry = new JournalEntry
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                EntryNumber = voucherNumber,
                EntryDate = date,
                JournalType = "PettyCash",
                Status = "Draft",
                Description = description,
                DocumentNumber = voucherNumber,
                ReferenceNumber = string.IsNullOrWhiteSpace(referenceNumber) ? null : referenceNumber.Trim(),
                SourceType = sourceType,
                IsPosted = false,
                CreatedAt = DateTime.UtcNow,
                PostedAt = null
            };

            foreach (var line in lines)
                entry.Lines.Add(line);

            _db.JournalEntries.Add(entry);
            await _db.SaveChangesAsync();

            return entry.Id;
        }

        private async Task<string> GenerateVoucherNumberAsync(int companyId, DateTime date)
        {
            var prefix = $"PC-{date:yyyyMM}-";

            var lastNumber = await _db.JournalEntries
                .Where(x => x.CompanyId == companyId && x.EntryNumber.StartsWith(prefix))
                .OrderByDescending(x => x.EntryNumber)
                .Select(x => x.EntryNumber)
                .FirstOrDefaultAsync();

            var next = 1;
            if (!string.IsNullOrEmpty(lastNumber) &&
                int.TryParse(lastNumber[prefix.Length..], out var parsed))
            {
                next = parsed + 1;
            }

            return $"{prefix}{next:D4}";
        }
    }
}
