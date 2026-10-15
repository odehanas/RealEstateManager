using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    public class PdcService
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _accountingPostingService;

        public PdcService(ApplicationDbContext db, AccountingPostingService accountingPostingService)
        {
            _db = db;
            _accountingPostingService = accountingPostingService;
        }

        public async Task<JournalEntry> ReceivePdcAsync(int pdcReceiptId)
        {
            var pdc = await _db.PdcReceipts
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                .Include(x => x.CustomerInvoice)
                .Include(x => x.MemorandumJournalEntry)
                .FirstOrDefaultAsync(x => x.Id == pdcReceiptId);

            if (pdc == null)
                throw new InvalidOperationException("PDC receipt was not found.");

            if (pdc.MemorandumJournalEntryId.HasValue)
            {
                return await _db.JournalEntries
                    .Include(x => x.Lines)
                    .FirstAsync(x => x.Id == pdc.MemorandumJournalEntryId.Value);
            }

            var settings = await GetCompanyAccountSettingAsync(pdc.CompanyId);

            var pdcOnHandAccount = await GetMappedAccountAsync(
                pdc.CompanyId,
                settings.PdcOnHandAccountId,
                "PDC On Hand");

            var pdcContraAccount = await GetMappedAccountAsync(
                pdc.CompanyId,
                settings.PdcContraAccountId,
                "PDC Contra");

            var lease = pdc.Lease!;
            var tenant = lease.Tenant!;
            var property = lease.Unit!.Property!;
            var unit = lease.Unit;

            var lines = new List<JournalEntryLine>
            {
                new()
                {
                    AccountId = pdcOnHandAccount.Id,
                    Debit = pdc.Amount,
                    Credit = 0m,
                    Description = $"PDC received {pdc.ChequeNumber}",
                    PropertyId = property.Id,
                    UnitId = unit.Id,
                    TenantId = tenant.Id,
                    LeaseId = lease.Id,
                    CustomerInvoiceId = pdc.CustomerInvoiceId
                },
                new()
                {
                    AccountId = pdcContraAccount.Id,
                    Debit = 0m,
                    Credit = pdc.Amount,
                    Description = $"PDC memorandum control {pdc.ChequeNumber}",
                    PropertyId = property.Id,
                    UnitId = unit.Id,
                    TenantId = tenant.Id,
                    LeaseId = lease.Id,
                    CustomerInvoiceId = pdc.CustomerInvoiceId
                }
            };

            var journalEntry = await CreateJournalEntryAsync(
                pdc.CompanyId,
                pdc.ReceivedDate,
                $"PDC receipt memorandum {pdc.ChequeNumber}",
                "PdcReceiptMemo",
                pdc.Id,
                lines,
                journalType: "PDC",
                documentNumber: pdc.ChequeNumber,
                referenceNumber: pdc.ChequeNumber,
                remarks: pdc.Notes);

            // Status becomes Received when the memorandum journal is posted.
            pdc.MemorandumJournalEntryId = journalEntry.Id;

            if (!pdc.TenantId.HasValue)
            {
                pdc.TenantId = tenant.Id;
            }

            await _db.SaveChangesAsync();
            return journalEntry;
        }

        public async Task<JournalEntry> ClearPdcAsync(int pdcReceiptId, DateTime clearedDate)
        {
            var pdc = await _db.PdcReceipts
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                .Include(x => x.CustomerInvoice)
                .Include(x => x.ClearanceJournalEntry)
                .FirstOrDefaultAsync(x => x.Id == pdcReceiptId);

            if (pdc == null)
                throw new InvalidOperationException("PDC receipt was not found.");

            if (pdc.Status == "Cleared" && pdc.ClearanceJournalEntryId.HasValue)
            {
                return await _db.JournalEntries
                    .Include(x => x.Lines)
                    .FirstAsync(x => x.Id == pdc.ClearanceJournalEntryId.Value);
            }

            if (!pdc.MemorandumJournalEntryId.HasValue)
            {
                await ReceivePdcAsync(pdc.Id);
            }

            if (pdc.CustomerInvoiceId.HasValue && pdc.SettlementMode == "Receivable")
            {
                var invoice = await _db.CustomerInvoices
                    .Include(x => x.JournalEntry)
                    .FirstOrDefaultAsync(x => x.Id == pdc.CustomerInvoiceId.Value);

                // Clearing in Receivable mode credits Accounts Receivable, so the invoice it
                // settles must already have debited it. This used to build the invoice's
                // journal on the fly, which under draft-first produced only a draft and let
                // the clearance settle a receivable that was not in the accounts.
                if (invoice != null && (invoice.JournalEntry == null || !invoice.JournalEntry.IsPosted))
                {
                    throw new InvalidOperationException(
                        $"Invoice {invoice.InvoiceNumber} has not been posted yet. Post it from Customer Invoices, " +
                        "then clear this cheque.");
                }
            }

            var settings = await GetCompanyAccountSettingAsync(pdc.CompanyId);

            var pdcOnHandAccount = await GetMappedAccountAsync(
                pdc.CompanyId,
                settings.PdcOnHandAccountId,
                "PDC On Hand");

            var pdcContraAccount = await GetMappedAccountAsync(
                pdc.CompanyId,
                settings.PdcContraAccountId,
                "PDC Contra");

            var cashAccount = await GetMappedAccountAsync(
                pdc.CompanyId,
                settings.DefaultCashAccountId,
                "Default Cash");

            Account settlementCreditAccount;

            if (pdc.SettlementMode == "Receivable")
            {
                settlementCreditAccount = await GetMappedAccountAsync(
                    pdc.CompanyId,
                    settings.AccountsReceivableAccountId,
                    "Accounts Receivable");
            }
            else
            {
                settlementCreditAccount = await GetMappedAccountAsync(
                    pdc.CompanyId,
                    settings.AdvancesFromCustomersAccountId,
                    "Advances From Customers");
            }

            var lease = pdc.Lease!;
            var tenant = lease.Tenant!;
            var property = lease.Unit!.Property!;
            var unit = lease.Unit;

            var lines = new List<JournalEntryLine>
            {
                // Reverse memorandum
                new()
                {
                    AccountId = pdcContraAccount.Id,
                    Debit = pdc.Amount,
                    Credit = 0m,
                    Description = $"Reverse PDC memorandum {pdc.ChequeNumber}",
                    PropertyId = property.Id,
                    UnitId = unit.Id,
                    TenantId = tenant.Id,
                    LeaseId = lease.Id,
                    CustomerInvoiceId = pdc.CustomerInvoiceId
                },
                new()
                {
                    AccountId = pdcOnHandAccount.Id,
                    Debit = 0m,
                    Credit = pdc.Amount,
                    Description = $"Reverse PDC on hand {pdc.ChequeNumber}",
                    PropertyId = property.Id,
                    UnitId = unit.Id,
                    TenantId = tenant.Id,
                    LeaseId = lease.Id,
                    CustomerInvoiceId = pdc.CustomerInvoiceId
                },

                // Real clearance
                new()
                {
                    AccountId = cashAccount.Id,
                    Debit = pdc.Amount,
                    Credit = 0m,
                    Description = $"PDC cleared to bank {pdc.ChequeNumber}",
                    PropertyId = property.Id,
                    UnitId = unit.Id,
                    TenantId = tenant.Id,
                    LeaseId = lease.Id,
                    CustomerInvoiceId = pdc.CustomerInvoiceId
                },
                new()
                {
                    AccountId = settlementCreditAccount.Id,
                    Debit = 0m,
                    Credit = pdc.Amount,
                    Description = pdc.SettlementMode == "Receivable"
                        ? $"PDC settled against receivable {pdc.ChequeNumber}"
                        : $"PDC cleared as customer advance {pdc.ChequeNumber}",
                    PropertyId = property.Id,
                    UnitId = unit.Id,
                    TenantId = tenant.Id,
                    LeaseId = lease.Id,
                    CustomerInvoiceId = pdc.CustomerInvoiceId
                }
            };

            var journalEntry = await CreateJournalEntryAsync(
                pdc.CompanyId,
                clearedDate.Date,
                $"PDC clearance {pdc.ChequeNumber}",
                "PdcClearance",
                pdc.Id,
                lines,
                journalType: "PDC",
                documentNumber: pdc.ChequeNumber,
                referenceNumber: pdc.ChequeNumber,
                remarks: pdc.Notes);

            // Cleared status, the cleared date and settlement of the invoice are all
            // applied when the clearance journal is posted - a draft clearance must not
            // move cash or settle a receivable.
            pdc.ClearanceJournalEntryId = journalEntry.Id;

            await _db.SaveChangesAsync();
            return journalEntry;
        }

        /// <summary>
        /// Corrects data-entry mistakes on a PDC that has already been recorded.
        ///
        /// Only fields with no effect on the amounts or accounts of the posted journal
        /// can be changed here: the dates, the cheque number, the bank and the notes.
        /// Amount, lease, invoice and settlement mode all determine what was posted and
        /// to where, so changing them would need the journal reversed and re-posted -
        /// deliberately not something a correction screen does quietly.
        ///
        /// Changing the received date RE-DATES THE MEMORANDUM JOURNAL to match, and
        /// moves it to the accounting period containing the new date. Updating one
        /// without the other is exactly how the register and the ledger drift apart.
        /// </summary>
        public async Task UpdateReceiptDetailsAsync(
            int pdcReceiptId,
            DateTime receivedDate,
            DateTime chequeDate,
            string chequeNumber,
            string? bankName,
            string? notes)
        {
            var pdc = await _db.PdcReceipts
                .Include(x => x.MemorandumJournalEntry)
                    .ThenInclude(x => x!.Lines)
                .FirstOrDefaultAsync(x => x.Id == pdcReceiptId)
                ?? throw new InvalidOperationException("PDC receipt was not found.");

            var newReceived = receivedDate.Date;
            var newCheque = chequeDate.Date;
            var trimmedNumber = (chequeNumber ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(trimmedNumber))
                throw new InvalidOperationException("Cheque number is required.");

            if (newReceived > DateTime.Today)
                throw new InvalidOperationException(
                    "The received date cannot be in the future - it is the date the memorandum entry is posted.");

            if (newCheque < newReceived)
                throw new InvalidOperationException(
                    "The cheque date cannot be earlier than the received date.");

            // A cleared cheque already has a second journal dated on the clearance. The
            // received date must stay on or before it, or the two entries invert.
            if (pdc.ClearedDate.HasValue && newReceived > pdc.ClearedDate.Value.Date)
            {
                throw new InvalidOperationException(
                    $"This cheque was cleared on {pdc.ClearedDate.Value:dd/MM/yyyy}, so the received date " +
                    "cannot be later than that.");
            }

            var duplicate = await _db.PdcReceipts.AnyAsync(x =>
                x.CompanyId == pdc.CompanyId &&
                x.ChequeNumber == trimmedNumber &&
                x.Id != pdc.Id);

            if (duplicate)
                throw new InvalidOperationException($"Cheque number {trimmedNumber} is already used by another PDC.");

            await using var transaction = await _db.Database.BeginTransactionAsync();

            var memo = pdc.MemorandumJournalEntry;
            var receivedDateChanged = pdc.ReceivedDate.Date != newReceived;

            if (memo != null && receivedDateChanged)
            {
                // Moving an entry OUT of a closed period would silently change figures
                // that have already been reported and locked.
                var currentPeriod = await _db.AccountingPeriods
                    .FirstOrDefaultAsync(x => x.Id == memo.AccountingPeriodId);

                if (currentPeriod is { IsClosed: true })
                {
                    throw new InvalidOperationException(
                        $"The memorandum entry sits in {currentPeriod.Name}, which is closed. " +
                        "Reopen that period before changing the received date.");
                }

                var targetPeriod = await ResolveAccountingPeriodAsync(pdc.CompanyId, newReceived)
                    ?? throw new InvalidOperationException(
                        $"No accounting period exists for {newReceived:dd/MM/yyyy}.");

                if (targetPeriod.IsClosed)
                {
                    throw new InvalidOperationException(
                        $"{targetPeriod.Name} is closed, so the memorandum entry cannot be dated into it.");
                }

                memo.EntryDate = newReceived;
                memo.AccountingPeriodId = targetPeriod.Id;
            }

            if (memo != null && pdc.ChequeNumber != trimmedNumber)
            {
                memo.Description = $"PDC receipt memorandum {trimmedNumber}";
                memo.DocumentNumber = trimmedNumber;
                memo.ReferenceNumber = trimmedNumber;

                foreach (var line in memo.Lines)
                {
                    if (string.IsNullOrEmpty(line.Description))
                        continue;

                    line.Description = line.Description.Replace(pdc.ChequeNumber, trimmedNumber);
                }
            }

            pdc.ReceivedDate = newReceived;
            pdc.ChequeDate = newCheque;
            pdc.ChequeNumber = trimmedNumber;
            pdc.BankName = string.IsNullOrWhiteSpace(bankName) ? null : bankName.Trim();
            pdc.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        private async Task<CompanyAccountSetting> GetCompanyAccountSettingAsync(int companyId)
        {
            var setting = await _db.CompanyAccountSettings.FirstOrDefaultAsync(x => x.CompanyId == companyId);

            if (setting == null)
                throw new InvalidOperationException("Company account mapping setup was not found.");

            return setting;
        }

        private async Task<Account> GetMappedAccountAsync(int companyId, int? accountId, string mappingName)
        {
            if (!accountId.HasValue)
                throw new InvalidOperationException($"Account mapping for '{mappingName}' is not configured.");

            var account = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == accountId.Value);

            if (account == null)
                throw new InvalidOperationException($"Mapped account for '{mappingName}' was not found.");

            if (!account.IsPostable)
                throw new InvalidOperationException($"Mapped account for '{mappingName}' must be a postable account.");

            if (!account.IsActive)
                throw new InvalidOperationException($"Mapped account for '{mappingName}' is inactive.");

            return account;
        }

        private async Task<AccountingPeriod?> ResolveAccountingPeriodAsync(int companyId, DateTime entryDate)
        {
            return await _db.AccountingPeriods.FirstOrDefaultAsync(x =>
                x.CompanyId == companyId &&
                x.StartDate <= entryDate.Date &&
                x.EndDate >= entryDate.Date);
        }

        private async Task<JournalEntry> CreateJournalEntryAsync(
            int companyId,
            DateTime entryDate,
            string description,
            string sourceType,
            int sourceId,
            List<JournalEntryLine> lines,
            string journalType = "Auto",
            string? documentNumber = null,
            string? referenceNumber = null,
            string? remarks = null)
        {
            if (lines.Count < 2)
                throw new InvalidOperationException("A journal entry must contain at least two lines.");

            var totalDebit = lines.Sum(x => x.Debit);
            var totalCredit = lines.Sum(x => x.Credit);

            if (Math.Abs(totalDebit - totalCredit) != 0m)
                throw new InvalidOperationException("Journal entry is not balanced.");

            var period = await ResolveAccountingPeriodAsync(companyId, entryDate);

            if (period == null)
                throw new InvalidOperationException("No accounting period found for the entry date.");

            if (period.IsClosed)
                throw new InvalidOperationException("The accounting period is closed. Posting is not allowed.");

            var journalEntry = new JournalEntry
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                EntryNumber = await GenerateEntryNumberAsync(sourceType),
                EntryDate = entryDate.Date,
                JournalType = journalType,
                // Draft on creation - see the note in AccountingPostingService.
                Status = "Draft",
                Description = description,
                DocumentNumber = documentNumber,
                ReferenceNumber = referenceNumber,
                Remarks = remarks,
                SourceType = sourceType,
                SourceId = sourceId,
                IsPosted = false,
                CreatedAt = DateTime.UtcNow,
                PostedAt = null,
                Lines = lines
            };

            _db.JournalEntries.Add(journalEntry);
            await _db.SaveChangesAsync();

            return journalEntry;
        }

        private async Task<string> GenerateEntryNumberAsync(string sourceType)
        {
            var prefix = sourceType.Length >= 3
                ? sourceType[..3].ToUpperInvariant()
                : sourceType.ToUpperInvariant();

            var count = await _db.JournalEntries.CountAsync() + 1;
            return $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{count:D6}";
        }
    }
}