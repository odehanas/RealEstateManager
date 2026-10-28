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

        /// <summary>
        /// Why a cheque cannot be cleared on a date, by form field; empty when it can.
        /// Shared by the Clear screen and the AI assistant.
        /// </summary>
        public static List<(string Field, string Message)> CheckClearance(PdcReceipt pdc, DateTime clearedDate)
        {
            var errors = new List<(string Field, string Message)>();

            if (pdc.Status == PdcStatuses.Cleared)
            {
                errors.Add((string.Empty, "This PDC is already cleared."));
            }
            else if (pdc.Status != PdcStatuses.Received && pdc.Status != PdcStatuses.Deposited)
            {
                errors.Add((string.Empty, $"A cheque that is {pdc.Status.ToLowerInvariant()} cannot be cleared."));
            }

            if (pdc.DepositedDate.HasValue && clearedDate.Date < pdc.DepositedDate.Value.Date)
            {
                errors.Add(("ClearedDate", $"The cheque was deposited on {pdc.DepositedDate:dd/MM/yyyy}; it cannot clear before that."));
            }

            if (clearedDate.Date < pdc.ReceivedDate.Date)
            {
                errors.Add(("ClearedDate", "Cleared date cannot be earlier than received date."));
            }

            // The cleared date is the accounting date of the cash movement, so it cannot
            // be in the future either.
            if (clearedDate.Date > DateTime.Today)
            {
                errors.Add(("ClearedDate",
                    "The cleared date cannot be in the future - it is the date the cash entry is posted. " +
                    "Clear the cheque on the day the bank actually honours it."));
            }

            return errors;
        }

        /// <summary>
        /// Clearing before the date written on the cheque is usually a mistake - a bank
        /// will not honour a post-dated cheque early. It is not forbidden, because the
        /// parties can agree to present it early, so this is a warning to confirm, not a refusal.
        /// </summary>
        public static string? EarlyClearanceWarning(PdcReceipt pdc, DateTime clearedDate)
        {
            if (clearedDate.Date >= pdc.ChequeDate.Date)
                return null;

            var daysEarly = (int)(pdc.ChequeDate.Date - clearedDate.Date).TotalDays;
            return $"This would clear the cheque {daysEarly} day(s) before its cheque date of " +
                   $"{pdc.ChequeDate:dd/MM/yyyy}. A bank will not normally honour a post-dated cheque early.";
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

            // Deposited first: the tenant was credited then, so clearing only moves the
            // cheque from collection into the bank.
            if (pdc.WentThroughCollection)
            {
                var collection = await UnderCollectionAccountAsync(pdc.CompanyId, settings);
                var collected = await CreateOrRebuildAsync(
                    pdc.ClearanceJournalEntryId,
                    pdc,
                    clearedDate.Date,
                    $"PDC clearance {pdc.ChequeNumber}",
                    "PdcClearance",
                    new List<JournalEntryLine>
                    {
                        Line(pdc, cashAccount.Id, pdc.Amount, 0m, $"PDC cleared to bank {pdc.ChequeNumber}"),
                        Line(pdc, collection.Id, 0m, pdc.Amount, $"PDC collected from bank {pdc.ChequeNumber}")
                    });

                pdc.ClearanceJournalEntryId = collected.Id;
                await _db.SaveChangesAsync();
                return collected;
            }

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

        // =====================================================================
        //  DEPOSIT, BOUNCE, WITHDRAWAL
        //
        //  On hand     Dr PDC On Hand                Cr PDC Contra               (memorandum)
        //  Deposited   Dr PDC Contra                 Cr PDC On Hand              (memorandum reversed)
        //              Dr Cheques Under Collection   Cr Receivable / Advances    (tenant credited)
        //  Cleared     Dr Bank                       Cr Cheques Under Collection
        //  Bounced     Dr Receivable / Advances      Cr Cheques Under Collection (charged back)
        //  Withdrawn   Dr PDC Contra                 Cr PDC On Hand              (memorandum reversed)
        //
        //  Each builds a draft journal; the cheque's status moves only when that
        //  journal is posted (JournalWorkflowService), and back when it is reset.
        // =====================================================================

        public const string DepositSourceType = "PdcDeposit";
        public const string BounceSourceType = "PdcBounce";
        public const string WithdrawalSourceType = "PdcWithdrawal";

        /// <summary>Why a cheque cannot be deposited on a date; empty when it can.</summary>
        public static List<string> CheckDeposit(PdcReceipt pdc, DateTime date)
        {
            var errors = new List<string>();
            if (pdc.Status != PdcStatuses.Received)
                errors.Add(pdc.Status == PdcStatuses.Draft
                    ? "The cheque's receipt is not posted yet. Post it first."
                    : $"Only a cheque on hand can be deposited; this one is {PdcStatuses.Describe(pdc.Status).ToLowerInvariant()}.");
            if (date.Date < pdc.ReceivedDate.Date)
                errors.Add("The deposit date cannot be earlier than the date the cheque was received.");
            if (date.Date > DateTime.Today)
                errors.Add("The deposit date cannot be in the future - it is the date the entry is posted.");
            return errors;
        }

        /// <summary>Why a cheque cannot be recorded as bounced on a date; empty when it can.</summary>
        public static List<string> CheckBounce(PdcReceipt pdc, DateTime date)
        {
            var errors = new List<string>();
            if (pdc.Status != PdcStatuses.Deposited && pdc.Status != PdcStatuses.Received)
                errors.Add($"Only a cheque at the bank (or on hand) can bounce; this one is {PdcStatuses.Describe(pdc.Status).ToLowerInvariant()}.");
            var from = pdc.DepositedDate ?? pdc.ReceivedDate;
            if (date.Date < from.Date)
                errors.Add($"The bounce date cannot be earlier than {from:dd/MM/yyyy}.");
            if (date.Date > DateTime.Today)
                errors.Add("The bounce date cannot be in the future - it is the date the entry is posted.");
            return errors;
        }

        /// <summary>Why a cheque cannot be handed back on a date; empty when it can.</summary>
        public static List<string> CheckWithdrawal(PdcReceipt pdc, DateTime date)
        {
            var errors = new List<string>();
            if (pdc.Status != PdcStatuses.Received)
                errors.Add(pdc.Status == PdcStatuses.Deposited
                    ? "This cheque is already at the bank, so it cannot be withdrawn. If the bank returns it, record it as bounced."
                    : $"Only a cheque on hand can be withdrawn; this one is {PdcStatuses.Describe(pdc.Status).ToLowerInvariant()}.");
            if (date.Date < pdc.ReceivedDate.Date)
                errors.Add("The withdrawal date cannot be earlier than the date the cheque was received.");
            if (date.Date > DateTime.Today)
                errors.Add("The withdrawal date cannot be in the future - it is the date the entry is posted.");
            return errors;
        }

        /// <summary>The journals a step created: the cheque's own, and the charge invoice's when one was raised.</summary>
        public sealed record StepResult(JournalEntry Journal, JournalEntry? ChargeJournal, CustomerInvoice? ChargeInvoice);

        /// <summary>Sends the cheque to the bank for collection; the tenant is credited now.</summary>
        public async Task<JournalEntry> DepositPdcAsync(int pdcReceiptId, DateTime depositDate)
        {
            var pdc = await LoadAsync(pdcReceiptId);
            ThrowIfAny(CheckDeposit(pdc, depositDate));
            await ThrowIfInvoiceNotPostedAsync(pdc);

            var settings = await GetCompanyAccountSettingAsync(pdc.CompanyId);
            var onHand = await GetMappedAccountAsync(pdc.CompanyId, settings.PdcOnHandAccountId, "PDC On Hand");
            var contra = await GetMappedAccountAsync(pdc.CompanyId, settings.PdcContraAccountId, "PDC Contra");
            var collection = await UnderCollectionAccountAsync(pdc.CompanyId, settings);
            var customer = await SettlementAccountAsync(pdc, settings);

            var journal = await CreateOrRebuildAsync(
                pdc.DepositJournalEntryId,
                pdc,
                depositDate.Date,
                $"PDC deposited for collection {pdc.ChequeNumber}",
                DepositSourceType,
                new List<JournalEntryLine>
                {
                    Line(pdc, contra.Id, pdc.Amount, 0m, $"Reverse PDC memorandum {pdc.ChequeNumber}"),
                    Line(pdc, onHand.Id, 0m, pdc.Amount, $"PDC sent to bank {pdc.ChequeNumber}"),
                    Line(pdc, collection.Id, pdc.Amount, 0m, $"Cheque under collection {pdc.ChequeNumber}"),
                    Line(pdc, customer.Id, 0m, pdc.Amount, pdc.SettlementMode == "Receivable"
                        ? $"PDC deposited against receivable {pdc.ChequeNumber}"
                        : $"PDC deposited as customer advance {pdc.ChequeNumber}")
                });

            pdc.DepositJournalEntryId = journal.Id;
            await _db.SaveChangesAsync();
            return journal;
        }

        /// <summary>
        /// The bank returned the cheque unpaid. A deposited cheque is charged back to the
        /// tenant; one never deposited only leaves the drawer. An optional charge is
        /// raised to the tenant as a tax invoice.
        /// </summary>
        public async Task<StepResult> BouncePdcAsync(int pdcReceiptId, DateTime bounceDate, string? reason, decimal charge)
        {
            var pdc = await LoadAsync(pdcReceiptId);
            ThrowIfAny(CheckBounce(pdc, bounceDate));
            if (charge < 0m) throw new InvalidOperationException("The charge cannot be negative.");

            var settings = await GetCompanyAccountSettingAsync(pdc.CompanyId);
            List<JournalEntryLine> lines;

            if (pdc.Status == PdcStatuses.Deposited)
            {
                var collection = await UnderCollectionAccountAsync(pdc.CompanyId, settings);
                var customer = await SettlementAccountAsync(pdc, settings);
                lines = new List<JournalEntryLine>
                {
                    Line(pdc, customer.Id, pdc.Amount, 0m, $"Returned cheque charged back to tenant {pdc.ChequeNumber}"),
                    Line(pdc, collection.Id, 0m, pdc.Amount, $"Cheque returned unpaid by bank {pdc.ChequeNumber}")
                };
            }
            else
            {
                lines = await MemorandumReversalAsync(pdc, settings, "returned unpaid");
            }

            var journal = await CreateOrRebuildAsync(
                pdc.BounceJournalEntryId, pdc, bounceDate.Date, $"PDC bounced {pdc.ChequeNumber}", BounceSourceType, lines);

            pdc.BounceJournalEntryId = journal.Id;
            pdc.BounceReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            await _db.SaveChangesAsync();

            var (invoice, chargeJournal) = await RaiseChargeAsync(pdc, InvoiceChargeTypes.PdcBounce, bounceDate.Date, charge);
            return new StepResult(journal, chargeJournal, invoice);
        }

        /// <summary>
        /// The tenant takes the cheque back before it is deposited. It stays in the
        /// register as withdrawn, and the tenant is charged for the withdrawal.
        /// </summary>
        public async Task<StepResult> WithdrawPdcAsync(int pdcReceiptId, DateTime withdrawalDate, decimal charge, string? note)
        {
            var pdc = await LoadAsync(pdcReceiptId);
            ThrowIfAny(CheckWithdrawal(pdc, withdrawalDate));
            if (charge < 0m) throw new InvalidOperationException("The charge cannot be negative.");

            var settings = await GetCompanyAccountSettingAsync(pdc.CompanyId);
            var journal = await CreateOrRebuildAsync(
                pdc.WithdrawalJournalEntryId, pdc, withdrawalDate.Date, $"PDC withdrawn by tenant {pdc.ChequeNumber}",
                WithdrawalSourceType, await MemorandumReversalAsync(pdc, settings, "handed back to tenant"));

            pdc.WithdrawalJournalEntryId = journal.Id;
            AppendNote(pdc, note);
            await _db.SaveChangesAsync();

            var (invoice, chargeJournal) = await RaiseChargeAsync(pdc, InvoiceChargeTypes.PdcWithdrawal, withdrawalDate.Date, charge);
            return new StepResult(journal, chargeJournal, invoice);
        }

        /// <summary>
        /// A bounced or withdrawn cheque is closed off as replaced - by a new cheque or a
        /// payment - so the tenant is no longer held as having a returned cheque.
        /// </summary>
        public async Task MarkReplacedAsync(int pdcReceiptId, string? note)
        {
            var pdc = await _db.PdcReceipts.FirstOrDefaultAsync(x => x.Id == pdcReceiptId)
                ?? throw new InvalidOperationException("PDC receipt was not found.");
            if (pdc.Status != PdcStatuses.Bounced && pdc.Status != PdcStatuses.Withdrawn)
                throw new InvalidOperationException("Only a bounced or withdrawn cheque can be marked as replaced.");

            pdc.Status = PdcStatuses.Replaced;
            AppendNote(pdc, note);
            await _db.SaveChangesAsync();
        }

        /// <summary>A charge before VAT, its VAT and total: 5% when the company is VAT registered.</summary>
        public static (decimal Net, decimal Vat, decimal Total, string Category) PriceCharge(Company company, decimal net)
        {
            var category = company.IsVatRegistered ? VatCategories.Standard : VatCategories.Exempt;
            var vat = VatDeterminationService.CalculateTax(net, category);
            return (net, vat, net + vat, category);
        }

        private static void AppendNote(PdcReceipt pdc, string? note)
        {
            if (string.IsNullOrWhiteSpace(note)) return;
            var combined = string.IsNullOrWhiteSpace(pdc.Notes) ? note.Trim() : $"{pdc.Notes} | {note.Trim()}";
            pdc.Notes = combined.Length > 1000 ? combined[..1000] : combined;
        }

        /// <summary>
        /// Raises the charge as a tax invoice to the tenant, so it is on their statement,
        /// collected like any invoice and declared on the VAT return.
        /// </summary>
        private async Task<(CustomerInvoice? Invoice, JournalEntry? Journal)> RaiseChargeAsync(
            PdcReceipt pdc, string chargeType, DateTime date, decimal net)
        {
            if (net <= 0m || pdc.ChargeInvoiceId.HasValue)
                return (null, null);

            var company = await _db.Companies.FirstAsync(x => x.Id == pdc.CompanyId);
            var price = PriceCharge(company, Math.Round(net, 2));
            var prefix = $"CHG-{date:yyyy}-";
            var used = await _db.CustomerInvoices.CountAsync(x => x.CompanyId == pdc.CompanyId && x.InvoiceNumber.StartsWith(prefix));

            var invoice = new CustomerInvoice
            {
                CompanyId = pdc.CompanyId,
                LeaseId = pdc.LeaseId,
                InvoiceNumber = $"{prefix}{used + 1:D5}",
                InvoiceDate = date,
                DueDate = date,
                DateOfSupply = date,
                ChargeType = chargeType,
                VatCategory = price.Category,
                VatRate = VatDeterminationService.RateFor(price.Category),
                Subtotal = price.Net,
                VatAmount = price.Vat,
                TotalAmount = price.Total,
                Status = "Draft"
            };
            _db.CustomerInvoices.Add(invoice);
            await _db.SaveChangesAsync();

            pdc.ChargeInvoiceId = invoice.Id;
            await _db.SaveChangesAsync();

            var journal = await _accountingPostingService.PostCustomerInvoiceAsync(invoice.Id);
            return (invoice, journal);
        }

        private async Task<PdcReceipt> LoadAsync(int pdcReceiptId) =>
            await _db.PdcReceipts
                .Include(x => x.Lease)!.ThenInclude(x => x!.Unit)!.ThenInclude(x => x!.Property)
                .Include(x => x.Lease)!.ThenInclude(x => x!.Tenant)
                .FirstOrDefaultAsync(x => x.Id == pdcReceiptId)
            ?? throw new InvalidOperationException("PDC receipt was not found.");

        private static void ThrowIfAny(List<string> errors)
        {
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        }

        /// <summary>A cheque settling an invoice credits Accounts Receivable, so the invoice must be in the accounts.</summary>
        private async Task ThrowIfInvoiceNotPostedAsync(PdcReceipt pdc)
        {
            if (!pdc.CustomerInvoiceId.HasValue || pdc.SettlementMode != "Receivable")
                return;

            var invoice = await _db.CustomerInvoices.Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == pdc.CustomerInvoiceId.Value);
            if (invoice != null && (invoice.JournalEntry == null || !invoice.JournalEntry.IsPosted))
                throw new InvalidOperationException(
                    $"Invoice {invoice.InvoiceNumber} has not been posted yet. Post it from Customer Invoices, then deposit this cheque.");
        }

        private async Task<Account> UnderCollectionAccountAsync(int companyId, CompanyAccountSetting settings)
        {
            // Mapped in Account Mappings; a company set up before the mapping existed uses 11450.
            var id = settings.ChequesUnderCollectionAccountId
                     ?? await _db.Accounts.Where(x => x.CompanyId == companyId && x.Code == "11450").Select(x => (int?)x.Id).FirstOrDefaultAsync();
            return await GetMappedAccountAsync(companyId, id, "Cheques Under Collection");
        }

        private async Task<Account> SettlementAccountAsync(PdcReceipt pdc, CompanyAccountSetting settings) =>
            pdc.SettlementMode == "Receivable"
                ? await GetMappedAccountAsync(pdc.CompanyId, settings.AccountsReceivableAccountId, "Accounts Receivable")
                : await GetMappedAccountAsync(pdc.CompanyId, settings.AdvancesFromCustomersAccountId, "Advances From Customers");

        private async Task<List<JournalEntryLine>> MemorandumReversalAsync(PdcReceipt pdc, CompanyAccountSetting settings, string why)
        {
            var onHand = await GetMappedAccountAsync(pdc.CompanyId, settings.PdcOnHandAccountId, "PDC On Hand");
            var contra = await GetMappedAccountAsync(pdc.CompanyId, settings.PdcContraAccountId, "PDC Contra");
            return new List<JournalEntryLine>
            {
                Line(pdc, contra.Id, pdc.Amount, 0m, $"Reverse PDC memorandum {pdc.ChequeNumber}"),
                Line(pdc, onHand.Id, 0m, pdc.Amount, $"PDC {why} {pdc.ChequeNumber}")
            };
        }

        private static JournalEntryLine Line(PdcReceipt pdc, int accountId, decimal debit, decimal credit, string description) => new()
        {
            AccountId = accountId,
            Debit = debit,
            Credit = credit,
            Description = description,
            PropertyId = pdc.Lease!.Unit!.PropertyId,
            UnitId = pdc.Lease.UnitId,
            TenantId = pdc.Lease.TenantId,
            LeaseId = pdc.LeaseId,
            CustomerInvoiceId = pdc.CustomerInvoiceId
        };

        /// <summary>
        /// A step taken again after its journal was reset to draft rebuilds that draft
        /// rather than leaving it behind beside a new one. A posted journal is returned as it is.
        /// </summary>
        private async Task<JournalEntry> CreateOrRebuildAsync(
            int? existingId, PdcReceipt pdc, DateTime date, string description, string sourceType, List<JournalEntryLine> lines)
        {
            if (existingId.HasValue)
            {
                var existing = await _db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == existingId.Value);
                if (existing is { IsPosted: true })
                    return existing;

                if (existing != null)
                {
                    var period = await ResolveAccountingPeriodAsync(pdc.CompanyId, date)
                                 ?? throw new InvalidOperationException("No accounting period found for the entry date.");
                    if (period.IsClosed)
                        throw new InvalidOperationException("The accounting period is closed. Posting is not allowed.");

                    _db.JournalEntryLines.RemoveRange(existing.Lines);
                    existing.Lines = lines;
                    existing.EntryDate = date;
                    existing.AccountingPeriodId = period.Id;
                    existing.Description = description;
                    await _db.SaveChangesAsync();
                    return existing;
                }
            }

            return await CreateJournalEntryAsync(pdc.CompanyId, date, description, sourceType, pdc.Id, lines,
                journalType: "PDC", documentNumber: pdc.ChequeNumber, referenceNumber: pdc.ChequeNumber, remarks: pdc.Notes);
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