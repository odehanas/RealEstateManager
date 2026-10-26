using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Receiving a post-dated cheque: the checks and the save-and-post of its memorandum
    /// entry, shared by the PDC Receipts screen and the AI assistant.
    /// </summary>
    public class PdcReceiptEntryService
    {
        private readonly ApplicationDbContext _db;
        private readonly PdcService _pdcService;
        private readonly JournalWorkflowService _journalWorkflowService;

        public PdcReceiptEntryService(ApplicationDbContext db, PdcService pdcService, JournalWorkflowService journalWorkflowService)
        {
            _db = db;
            _pdcService = pdcService;
            _journalWorkflowService = journalWorkflowService;
        }

        public sealed record ValidationError(string Field, string Message);

        public sealed record CreateResult(PdcReceipt Pdc, JournalEntry Journal, string? NotPostedReason);

        /// <summary>Everything that stops the cheque being recorded, by form field.</summary>
        public async Task<List<ValidationError>> ValidateAsync(int companyId, PdcReceiptCreateViewModel model)
        {
            var errors = new List<ValidationError>();

            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
            {
                errors.Add(new(string.Empty, "No company record found."));
            }

            if (!await _db.Leases.AnyAsync(x => x.Id == model.LeaseId))
            {
                errors.Add(new(nameof(model.LeaseId), "Selected lease was not found."));
            }

            CustomerInvoice? invoice = null;

            if (model.CustomerInvoiceId.HasValue)
            {
                invoice = await _db.CustomerInvoices
                    .Include(x => x.Lease)
                    .FirstOrDefaultAsync(x => x.Id == model.CustomerInvoiceId.Value);

                if (invoice == null)
                {
                    errors.Add(new(nameof(model.CustomerInvoiceId), "Selected invoice was not found."));
                }
            }

            if (string.IsNullOrWhiteSpace(model.ChequeNumber))
            {
                errors.Add(new(nameof(model.ChequeNumber), "Cheque number is required."));
            }

            if (model.Amount <= 0m)
            {
                errors.Add(new(nameof(model.Amount), "PDC amount must be greater than zero."));
            }

            // The received date is the ACCOUNTING date - the memorandum journal is posted
            // on it. A future received date would post the entry into a future period,
            // where it is invisible in today's Trial Balance and Balance Sheet. The
            // classic mistake is typing the cheque's future date into this field, so it
            // is rejected rather than silently accepted.
            if (model.ReceivedDate.Date > DateTime.Today)
            {
                errors.Add(new(nameof(model.ReceivedDate),
                    "The received date cannot be in the future - it is the date the accounting entry is posted. " +
                    "Enter the date the cheque was physically handed over. The future date written on the cheque goes in Cheque Date."));
            }

            if (model.ChequeDate.Date < model.ReceivedDate.Date)
            {
                errors.Add(new(nameof(model.ChequeDate),
                    "The cheque date is earlier than the received date. Check the two dates - a post-dated cheque is dated on or after the day you receive it."));
            }

            if (model.SettlementMode != "Advance" && model.SettlementMode != "Receivable")
            {
                errors.Add(new(nameof(model.SettlementMode), "Settlement mode must be Advance or Receivable."));
            }

            if (model.SettlementMode == "Receivable" && !model.CustomerInvoiceId.HasValue)
            {
                errors.Add(new(nameof(model.CustomerInvoiceId), "Invoice is required when settlement mode is Receivable."));
            }

            if (invoice != null && invoice.LeaseId != model.LeaseId)
            {
                errors.Add(new(nameof(model.CustomerInvoiceId), "Selected invoice does not belong to the selected lease."));
            }

            if (invoice != null && model.SettlementMode == "Receivable")
            {
                var outstanding = invoice.TotalAmount - invoice.AmountPaid;
                if (model.Amount > outstanding)
                {
                    errors.Add(new(nameof(model.Amount), "PDC amount cannot exceed the outstanding invoice balance."));
                }
            }

            if (!string.IsNullOrWhiteSpace(model.ChequeNumber) && company != null)
            {
                var chequeExists = await _db.PdcReceipts.AnyAsync(x =>
                    x.CompanyId == companyId &&
                    x.ChequeNumber == model.ChequeNumber.Trim());

                if (chequeExists)
                {
                    errors.Add(new(nameof(model.ChequeNumber), "Cheque number already exists."));
                }
            }

            return errors;
        }

        /// <summary>
        /// Saves the cheque and its memorandum entry, then posts it. Call
        /// <see cref="ValidateAsync"/> first. Throws if the entry cannot be built, in
        /// which case nothing is saved.
        /// </summary>
        public async Task<CreateResult> CreateAsync(int companyId, PdcReceiptCreateViewModel model)
        {
            var lease = await _db.Leases.FirstAsync(x => x.Id == model.LeaseId);

            await using var transaction = await _db.Database.BeginTransactionAsync();

            var pdc = new PdcReceipt
            {
                CompanyId = companyId,
                LeaseId = model.LeaseId,
                TenantId = lease.TenantId,
                CustomerInvoiceId = model.SettlementMode == "Receivable"
                    ? model.CustomerInvoiceId
                    : null,
                ChequeNumber = model.ChequeNumber.Trim(),
                BankName = string.IsNullOrWhiteSpace(model.BankName)
                    ? null
                    : model.BankName.Trim(),
                ChequeDate = model.ChequeDate.Date,
                ReceivedDate = model.ReceivedDate.Date,
                Amount = model.Amount,
                SettlementMode = model.SettlementMode,
                Status = "Draft",
                Notes = string.IsNullOrWhiteSpace(model.Notes)
                    ? null
                    : model.Notes.Trim()
            };

            _db.PdcReceipts.Add(pdc);
            await _db.SaveChangesAsync();

            var journal = await _pdcService.ReceivePdcAsync(pdc.Id);

            await transaction.CommitAsync();

            // The cheque is recorded as received at once, through the same checks as
            // Journal Inquiry; if one refuses, the memorandum entry stays a draft.
            var notPosted = await _journalWorkflowService.PostNewEntryAsync(journal.Id);
            return new CreateResult(pdc, journal, notPosted);
        }
    }
}
