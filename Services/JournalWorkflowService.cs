using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Draft / Posted state machine for journal entries, modelled on Odoo's
    /// account.move workflow: an entry is a draft with no effect on the accounts until
    /// it is posted, and a posted entry can be reset back to draft.
    ///
    /// Two things make this safe rather than dangerous:
    ///
    /// 1. RESET UNWINDS THE DOCUMENT, NOT JUST THE JOURNAL. Posting a receipt does more
    ///    than write a journal - it also increases the invoice's AmountPaid and moves its
    ///    status. Removing the journal alone would leave the invoice claiming to be paid
    ///    with nothing behind it. Every source type therefore has an explicit rollback.
    ///
    /// 2. RESET IS REFUSED WHEN SOMETHING DEPENDS ON IT. An invoice with receipts against
    ///    it, or a PDC that has already cleared, cannot be reset - the later transaction
    ///    would be left dangling. Odoo does the same for reconciled entries.
    ///
    /// Closed periods are immovable in both directions: nothing can be posted into one
    /// and nothing can be reset out of one, because those figures have been reported.
    /// </summary>
    public class JournalWorkflowService
    {
        private readonly ApplicationDbContext _db;

        public JournalWorkflowService(ApplicationDbContext db)
        {
            _db = db;
        }

        // =====================================================================
        //  STATE
        // =====================================================================

        /// <summary>
        /// What the user is allowed to do with this entry, and why not when they are not.
        /// Drives the buttons so the UI never offers an action that will fail.
        /// </summary>
        public async Task<JournalWorkflowState> GetStateAsync(int journalEntryId)
        {
            var entry = await _db.JournalEntries
                .AsNoTracking()
                .Include(x => x.AccountingPeriod)
                .FirstOrDefaultAsync(x => x.Id == journalEntryId);

            if (entry == null)
                return new JournalWorkflowState { Exists = false };

            var state = new JournalWorkflowState
            {
                Exists = true,
                JournalEntryId = entry.Id,
                EntryNumber = entry.EntryNumber,
                EntryDate = entry.EntryDate,
                IsPosted = entry.IsPosted,
                SourceType = entry.SourceType,
                PeriodName = entry.AccountingPeriod?.Name,
                PeriodClosed = entry.AccountingPeriod?.IsClosed ?? false
            };

            if (entry.IsPosted)
            {
                var reason = await GetResetBlockReasonAsync(entry);
                state.CanReset = reason == null;
                state.BlockReason = reason;
            }
            else
            {
                var reason = GetPostBlockReason(entry) ?? await GetDependencyBlockReasonAsync(entry);
                state.CanPost = reason == null;
                state.BlockReason = reason;
            }

            return state;
        }

        // =====================================================================
        //  POST
        // =====================================================================

        public async Task<JournalWorkflowResult> PostAsync(int journalEntryId)
        {
            var entry = await LoadEntryAsync(journalEntryId);
            if (entry == null)
                return JournalWorkflowResult.Failed("Journal entry was not found.");

            if (entry.IsPosted)
                return JournalWorkflowResult.Failed("This entry is already posted.");

            var blocked = GetPostBlockReason(entry) ?? await GetDependencyBlockReasonAsync(entry);
            if (blocked != null)
                return JournalWorkflowResult.Failed(blocked);

            await using var transaction = await _db.Database.BeginTransactionAsync();

            entry.IsPosted = true;
            entry.Status = "Posted";
            entry.PostedAt = DateTime.UtcNow;

            // Re-apply whatever the document side of the transaction expects, so posting
            // is the exact mirror of resetting.
            await ApplyDocumentEffectsAsync(entry, posting: true);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return JournalWorkflowResult.Succeeded(
                $"Entry {entry.EntryNumber} posted. It now affects the accounts.");
        }

        /// <summary>
        /// Posts every draft in a date range. Batch operations - a month of revenue
        /// recognition, a depreciation run - produce many drafts at once, and posting
        /// them one at a time is not realistic.
        ///
        /// Each entry is posted independently: one failure does not abandon the rest,
        /// and the failures are reported so nothing fails silently.
        /// </summary>
        public async Task<JournalBatchResult> PostAllDraftsAsync(
            DateTime? fromDate,
            DateTime? toDate,
            string? sourceType)
        {
            var query = _db.JournalEntries.Where(x => !x.IsPosted);

            if (fromDate.HasValue)
                query = query.Where(x => x.EntryDate >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(x => x.EntryDate <= toDate.Value);

            if (!string.IsNullOrWhiteSpace(sourceType))
                query = query.Where(x => x.SourceType == sourceType);

            var ids = await query
                .OrderBy(x => x.EntryDate)
                .ThenBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync();

            var batch = new JournalBatchResult { Considered = ids.Count };

            foreach (var id in ids)
            {
                var result = await PostAsync(id);

                if (result.Success)
                    batch.Posted++;
                else
                    batch.Failures.Add(result.Message);
            }

            return batch;
        }

        // =====================================================================
        //  RESET TO DRAFT
        // =====================================================================

        public async Task<JournalWorkflowResult> ResetToDraftAsync(int journalEntryId)
        {
            var entry = await LoadEntryAsync(journalEntryId);
            if (entry == null)
                return JournalWorkflowResult.Failed("Journal entry was not found.");

            if (!entry.IsPosted)
                return JournalWorkflowResult.Failed("This entry is already a draft.");

            var blocked = await GetResetBlockReasonAsync(entry);
            if (blocked != null)
                return JournalWorkflowResult.Failed(blocked);

            await using var transaction = await _db.Database.BeginTransactionAsync();

            await ApplyDocumentEffectsAsync(entry, posting: false);

            entry.IsPosted = false;
            entry.Status = "Draft";
            entry.PostedAt = null;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return JournalWorkflowResult.Succeeded(
                $"Entry {entry.EntryNumber} reset to draft. It no longer affects the accounts.");
        }

        // =====================================================================
        //  GUARDS
        // =====================================================================

        private static string? GetPostBlockReason(JournalEntry entry)
        {
            if (entry.AccountingPeriod == null)
                return $"No accounting period covers {entry.EntryDate:dd/MM/yyyy}.";

            if (entry.AccountingPeriod.IsClosed)
                return $"{entry.AccountingPeriod.Name} is closed. An entry cannot be posted into a closed period.";

            var debit = entry.Lines.Sum(x => x.Debit);
            var credit = entry.Lines.Sum(x => x.Credit);

            if (debit != credit)
                return $"The entry is not balanced: debits {debit:N2} against credits {credit:N2}.";

            if (entry.Lines.Count < 2)
                return "The entry has fewer than two lines.";

            return null;
        }

        /// <summary>
        /// A settlement may only be posted once the document it settles is in the accounts.
        ///
        /// A receipt credits Accounts Receivable and a vendor payment debits Accounts
        /// Payable. If the invoice or bill behind it is still a draft, that balance was
        /// never raised, and posting the settlement first would leave the control account
        /// understated - or negative - with nothing in the sub-ledger to explain it.
        ///
        /// The entry screens now refuse an unposted invoice or bill. This check is the net
        /// beneath them: it also catches drafts saved by older versions of those screens,
        /// which built the invoice or bill journal as a draft on the fly. In a Post All
        /// Drafts run the invoice is normally dated first and therefore posted first, so
        /// the settlement goes through on the same run; if not, it is reported and the
        /// next run picks it up.
        /// </summary>
        private async Task<string?> GetDependencyBlockReasonAsync(JournalEntry entry)
        {
            if (!entry.SourceId.HasValue)
                return null;

            var id = entry.SourceId.Value;

            switch (entry.SourceType)
            {
                case "CustomerReceipt":
                {
                    var invoice = await _db.CustomerReceipts
                        .Where(x => x.Id == id)
                        .Select(x => new
                        {
                            x.CustomerInvoice!.InvoiceNumber,
                            Posted = x.CustomerInvoice.JournalEntry != null && x.CustomerInvoice.JournalEntry.IsPosted
                        })
                        .FirstOrDefaultAsync();

                    return invoice is { Posted: false }
                        ? $"Invoice {invoice.InvoiceNumber} is not posted yet. Post the invoice before this receipt."
                        : null;
                }

                case "VendorPayment":
                {
                    var bill = await _db.VendorPayments
                        .Where(x => x.Id == id)
                        .Select(x => new
                        {
                            x.VendorBill!.BillNumber,
                            Posted = x.VendorBill.JournalEntry != null && x.VendorBill.JournalEntry.IsPosted
                        })
                        .FirstOrDefaultAsync();

                    return bill is { Posted: false }
                        ? $"Bill {bill.BillNumber} is not posted yet. Post the bill before this payment."
                        : null;
                }

                case "CustomerCreditNote":
                {
                    var note = await _db.CustomerCreditNotes
                        .Where(x => x.Id == id)
                        .Select(x => new
                        {
                            x.CreditNoteNumber,
                            x.TotalAmount,
                            x.CustomerInvoice!.InvoiceNumber,
                            Outstanding = x.CustomerInvoice.TotalAmount - x.CustomerInvoice.AmountPaid,
                            Posted = x.CustomerInvoice.JournalEntry != null && x.CustomerInvoice.JournalEntry.IsPosted
                        })
                        .FirstOrDefaultAsync();

                    if (note == null)
                        return null;

                    if (!note.Posted)
                        return $"Invoice {note.InvoiceNumber} is not posted yet. Post the invoice before crediting it.";

                    // Checked again at posting, because a receipt may have been posted
                    // against the invoice since the credit note was drafted.
                    return note.TotalAmount > note.Outstanding
                        ? $"Credit note {note.CreditNoteNumber} is for {note.TotalAmount:N2}, but only " +
                          $"{note.Outstanding:N2} is still outstanding on invoice {note.InvoiceNumber}."
                        : null;
                }

                case "PdcClearance":
                {
                    // Only a cheque settling an invoice touches Accounts Receivable. One
                    // taken as an advance credits Advances from Customers and needs nothing.
                    var pdc = await _db.PdcReceipts
                        .Where(x => x.Id == id && x.SettlementMode == "Receivable" && x.CustomerInvoiceId != null)
                        .Select(x => new
                        {
                            x.ChequeNumber,
                            x.CustomerInvoice!.InvoiceNumber,
                            Posted = x.CustomerInvoice.JournalEntry != null && x.CustomerInvoice.JournalEntry.IsPosted
                        })
                        .FirstOrDefaultAsync();

                    return pdc is { Posted: false }
                        ? $"Invoice {pdc.InvoiceNumber} is not posted yet. Post it before clearing cheque {pdc.ChequeNumber}."
                        : null;
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// Reasons an entry may not be reset. Ordered so the most fundamental objection
        /// is reported first.
        /// </summary>
        private async Task<string?> GetResetBlockReasonAsync(JournalEntry entry)
        {
            if (entry.AccountingPeriod == null)
                return $"No accounting period covers {entry.EntryDate:dd/MM/yyyy}.";

            if (entry.AccountingPeriod.IsClosed)
            {
                return $"{entry.AccountingPeriod.Name} is closed. Entries in a closed period cannot be " +
                       "reset - those figures have already been reported. Post a correcting journal instead.";
            }

            var sourceType = entry.SourceType ?? string.Empty;

            // Some postings create schedules or clear whole years. Unwinding those is not
            // a reset, it is a separate operation, so they are refused outright.
            if (NonResettableSourceTypes.Contains(sourceType))
            {
                return $"A {DescribeSourceType(sourceType)} entry cannot be reset to draft. " +
                       "Post a reversing journal instead.";
            }

            return sourceType switch
            {
                "CustomerInvoice" => await BlockIfReportedAsync(entry.SourceId, isCreditNote: false)
                                     ?? await BlockIfInvoiceHasSettlementsAsync(entry.SourceId),
                "CustomerCreditNote" => await BlockIfReportedAsync(entry.SourceId, isCreditNote: true),
                "VendorBill" => await BlockIfBillHasPaymentsAsync(entry.SourceId),
                "PdcReceiptMemo" => await BlockIfPdcClearedAsync(entry.SourceId),
                _ => null
            };
        }

        private async Task<string?> BlockIfInvoiceHasSettlementsAsync(int? invoiceId)
        {
            if (!invoiceId.HasValue)
                return null;

            var receipts = await _db.CustomerReceipts
                .CountAsync(x => x.CustomerInvoiceId == invoiceId.Value && x.JournalEntryId != null);

            if (receipts > 0)
                return $"This invoice has {receipts} posted receipt(s) against it. Reset those first.";

            var pdcs = await _db.PdcReceipts
                .CountAsync(x => x.CustomerInvoiceId == invoiceId.Value && x.Status == "Cleared");

            if (pdcs > 0)
                return $"This invoice has {pdcs} cleared cheque(s) against it. Reset those first.";

            var creditNotes = await _db.CustomerCreditNotes
                .CountAsync(x => x.CustomerInvoiceId == invoiceId.Value && x.JournalEntry != null && x.JournalEntry.IsPosted);

            if (creditNotes > 0)
                return $"This invoice has {creditNotes} posted credit note(s) against it. Reset those first.";

            return null;
        }

        /// <summary>
        /// A document handed to the e-invoicing provider is part of the FTA's record. It
        /// cannot be withdrawn from the accounts; the law allows only a credit note.
        /// </summary>
        private async Task<string?> BlockIfReportedAsync(int? sourceId, bool isCreditNote)
        {
            if (!sourceId.HasValue)
                return null;

            var status = isCreditNote
                ? await _db.CustomerCreditNotes.Where(x => x.Id == sourceId.Value).Select(x => x.EInvoiceStatus).FirstOrDefaultAsync()
                : await _db.CustomerInvoices.Where(x => x.Id == sourceId.Value).Select(x => x.EInvoiceStatus).FirstOrDefaultAsync();

            if (!EInvoiceStatuses.IsLocked(status))
                return null;

            return isCreditNote
                ? "This credit note has been sent to the FTA through your e-invoicing provider and can no longer be reset. " +
                  "If it was wrong, raise a new invoice for the difference."
                : "This invoice has been sent to the FTA through your e-invoicing provider and can no longer be reset. " +
                  "Correct it with a credit note (Operations ▸ Customer Invoices ▸ Credit Note).";
        }

        private async Task<string?> BlockIfBillHasPaymentsAsync(int? billId)
        {
            if (!billId.HasValue)
                return null;

            var payments = await _db.VendorPayments
                .CountAsync(x => x.VendorBillId == billId.Value && x.JournalEntryId != null);

            return payments > 0
                ? $"This bill has {payments} posted payment(s) against it. Reset those first."
                : null;
        }

        private async Task<string?> BlockIfPdcClearedAsync(int? pdcId)
        {
            if (!pdcId.HasValue)
                return null;

            var cleared = await _db.PdcReceipts
                .AnyAsync(x => x.Id == pdcId.Value && x.ClearanceJournalEntryId != null);

            return cleared
                ? "This cheque has already cleared. Reset the clearance entry first."
                : null;
        }

        // =====================================================================
        //  DOCUMENT SIDE EFFECTS
        // =====================================================================

        /// <summary>
        /// Applies or unwinds the non-journal consequences of a posting. Called with
        /// posting:true when posting and posting:false when resetting, so the two are
        /// exact mirrors of one another.
        ///
        /// NOTE: the document's JournalEntryId link is NOT touched here. The link is set
        /// when the journal is created and survives a reset - the entry still belongs to
        /// the document, it is simply a draft again. Clearing it would orphan the draft
        /// and let the screen create a second journal for the same document.
        /// </summary>
        private async Task ApplyDocumentEffectsAsync(JournalEntry entry, bool posting)
        {
            var sourceType = entry.SourceType ?? string.Empty;
            var sourceId = entry.SourceId;

            if (!sourceId.HasValue)
                return;

            switch (sourceType)
            {
                case "CustomerInvoice":
                {
                    var invoice = await _db.CustomerInvoices
                        .Include(x => x.Lease)!.ThenInclude(x => x!.Tenant)
                        .FirstOrDefaultAsync(x => x.Id == sourceId.Value);
                    if (invoice == null) break;

                    invoice.Status = posting ? "Posted" : "Draft";

                    // Posting is the moment an invoice is issued, so it is the moment its
                    // e-invoicing obligation is decided. Resetting withdraws a file that was
                    // produced but never sent - it must be produced again once re-posted.
                    if (posting)
                    {
                        if (invoice.EInvoiceStatus is EInvoiceStatuses.NotRequired or EInvoiceStatuses.Pending
                            && invoice.Lease?.Tenant != null)
                        {
                            var company = await _db.Companies.FirstAsync(x => x.Id == invoice.CompanyId);
                            invoice.EInvoiceStatus = EInvoiceService.DetermineStatus(
                                company, invoice.Lease.Tenant, EInvoiceService.SupplyDateOf(invoice));
                        }
                    }
                    else if (invoice.EInvoiceStatus == EInvoiceStatuses.Generated)
                    {
                        invoice.EInvoiceStatus = EInvoiceStatuses.Pending;
                    }

                    break;
                }

                case "CustomerCreditNote":
                {
                    var note = await _db.CustomerCreditNotes
                        .Include(x => x.CustomerInvoice)!.ThenInclude(x => x!.Lease)!.ThenInclude(x => x!.Tenant)
                        .FirstOrDefaultAsync(x => x.Id == sourceId.Value);
                    if (note?.CustomerInvoice == null) break;

                    var invoice = note.CustomerInvoice;
                    var sign = posting ? 1m : -1m;

                    // A credit note settles part of the invoice, exactly as a receipt does,
                    // so outstanding stays TotalAmount - AmountPaid everywhere.
                    invoice.AmountPaid += sign * note.TotalAmount;
                    invoice.CreditedAmount += sign * note.TotalAmount;
                    RecomputeInvoiceStatus(invoice);

                    note.Status = posting ? "Posted" : "Draft";

                    if (posting)
                    {
                        if (note.EInvoiceStatus is EInvoiceStatuses.NotRequired or EInvoiceStatuses.Pending
                            && invoice.Lease?.Tenant != null)
                        {
                            var company = await _db.Companies.FirstAsync(x => x.Id == note.CompanyId);
                            note.EInvoiceStatus = EInvoiceService.DetermineCreditNoteStatus(company, invoice.Lease.Tenant, note);
                        }
                    }
                    else if (note.EInvoiceStatus == EInvoiceStatuses.Generated)
                    {
                        note.EInvoiceStatus = EInvoiceStatuses.Pending;
                    }

                    break;
                }

                case "CustomerReceipt":
                {
                    var receipt = await _db.CustomerReceipts
                        .Include(x => x.CustomerInvoice)
                        .FirstOrDefaultAsync(x => x.Id == sourceId.Value);

                    if (receipt == null) break;


                    if (receipt.CustomerInvoice != null)
                    {
                        receipt.CustomerInvoice.AmountPaid += posting ? receipt.Amount : -receipt.Amount;
                        RecomputeInvoiceStatus(receipt.CustomerInvoice);
                    }

                    break;
                }

                case "VendorBill":
                {
                    var bill = await _db.VendorBills.FirstOrDefaultAsync(x => x.Id == sourceId.Value);
                    if (bill == null) break;


                    bill.Status = posting ? "Posted" : "Draft";
                    break;
                }

                case "VendorPayment":
                {
                    var payment = await _db.VendorPayments
                        .Include(x => x.VendorBill)
                        .FirstOrDefaultAsync(x => x.Id == sourceId.Value);

                    if (payment == null) break;


                    if (payment.VendorBill != null)
                    {
                        payment.VendorBill.AmountPaid += posting ? payment.Amount : -payment.Amount;
                        RecomputeBillStatus(payment.VendorBill);
                    }

                    break;
                }

                case "SecurityDeposit":
                {
                    var deposit = await _db.SecurityDeposits.FirstOrDefaultAsync(x => x.Id == sourceId.Value);
                    if (deposit == null) break;


                    break;
                }

                case "RevenueRecognitionSchedule":
                {
                    var schedule = await _db.RevenueRecognitionSchedules
                        .FirstOrDefaultAsync(x => x.Id == sourceId.Value);

                    if (schedule == null) break;


                    schedule.Status = posting ? "Posted" : "Planned";
                    break;
                }

                case "PdcReceiptMemo":
                {
                    var pdc = await _db.PdcReceipts.FirstOrDefaultAsync(x => x.Id == sourceId.Value);
                    if (pdc == null) break;


                    pdc.Status = posting ? "Received" : "Draft";
                    break;
                }

                case "PdcClearance":
                {
                    var pdc = await _db.PdcReceipts
                        .Include(x => x.CustomerInvoice)
                        .FirstOrDefaultAsync(x => x.Id == sourceId.Value);

                    if (pdc == null) break;


                    pdc.ClearedDate = posting ? entry.EntryDate : null;
                    pdc.Status = posting ? "Cleared" : "Received";

                    // A cleared cheque in Receivable mode settles the invoice; an advance
                    // does not touch it.
                    if (pdc.SettlementMode == "Receivable" && pdc.CustomerInvoice != null)
                    {
                        pdc.CustomerInvoice.AmountPaid += posting ? pdc.Amount : -pdc.Amount;
                        RecomputeInvoiceStatus(pdc.CustomerInvoice);
                    }

                    break;
                }

                // ManualJournal, PettyCashExpense, PettyCashReplenishment and anything
                // else have no document state to keep in step.
            }
        }

        private static void RecomputeInvoiceStatus(CustomerInvoice invoice)
        {
            if (invoice.CreditedAmount > 0m && invoice.CreditedAmount >= invoice.TotalAmount)
                invoice.Status = "Credited";
            else if (invoice.AmountPaid <= 0m)
                invoice.Status = invoice.JournalEntryId.HasValue ? "Posted" : "Draft";
            else if (invoice.AmountPaid < invoice.TotalAmount)
                invoice.Status = "PartiallyPaid";
            else
                invoice.Status = "Paid";
        }

        private static void RecomputeBillStatus(VendorBill bill)
        {
            if (bill.AmountPaid <= 0m)
                bill.Status = bill.JournalEntryId.HasValue ? "Posted" : "Draft";
            else if (bill.AmountPaid < bill.TotalAmount)
                bill.Status = "PartiallyPaid";
            else
                bill.Status = "Paid";
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        /// <summary>
        /// Postings that create schedules or close a year. Unwinding these is a distinct
        /// operation, not a reset, so they are refused.
        /// </summary>
        private static readonly HashSet<string> NonResettableSourceTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "FiscalYearClosing",
                "PrepaymentSchedule",
                "PrepaymentRecognition",
                "FixedAssetAcquisition",
                "FixedAssetCapitalization",
                "FixedAssetDepreciation"
            };

        private static string DescribeSourceType(string sourceType) => sourceType switch
        {
            "FiscalYearClosing" => "fiscal year closing",
            "PrepaymentSchedule" => "prepayment set-up",
            "PrepaymentRecognition" => "prepayment amortisation",
            "FixedAssetAcquisition" => "fixed asset acquisition",
            "FixedAssetCapitalization" => "fixed asset capitalisation",
            "FixedAssetDepreciation" => "depreciation",
            _ => sourceType
        };

        private async Task<JournalEntry?> LoadEntryAsync(int journalEntryId)
        {
            return await _db.JournalEntries
                .Include(x => x.Lines)
                .Include(x => x.AccountingPeriod)
                .FirstOrDefaultAsync(x => x.Id == journalEntryId);
        }
    }

    public class JournalWorkflowState
    {
        public bool Exists { get; set; }
        public int JournalEntryId { get; set; }
        public string EntryNumber { get; set; } = string.Empty;
        public DateTime EntryDate { get; set; }
        public bool IsPosted { get; set; }
        public string? SourceType { get; set; }
        public string? PeriodName { get; set; }
        public bool PeriodClosed { get; set; }

        public bool CanPost { get; set; }
        public bool CanReset { get; set; }

        /// <summary>Why the available action is unavailable, for the tooltip.</summary>
        public string? BlockReason { get; set; }
    }

    public class JournalBatchResult
    {
        public int Considered { get; set; }
        public int Posted { get; set; }
        public List<string> Failures { get; set; } = new();

        public int FailedCount => Failures.Count;

        public string Summary => Considered == 0
            ? "There were no draft entries to post."
            : $"Posted {Posted} of {Considered} draft entr{(Considered == 1 ? "y" : "ies")}." +
              (FailedCount > 0 ? $" {FailedCount} could not be posted." : string.Empty);
    }

    public class JournalWorkflowResult
    {
        public bool Success { get; private init; }
        public string Message { get; private init; } = string.Empty;

        public static JournalWorkflowResult Succeeded(string message) =>
            new() { Success = true, Message = message };

        public static JournalWorkflowResult Failed(string message) =>
            new() { Success = false, Message = message };
    }
}
