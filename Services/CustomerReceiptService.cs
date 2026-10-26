using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Recording a customer receipt against a posted invoice: the checks and the
    /// save-and-post, shared by the Customer Receipts screen and the AI assistant.
    /// </summary>
    public class CustomerReceiptService
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _postingService;
        private readonly JournalWorkflowService _journalWorkflowService;

        public CustomerReceiptService(
            ApplicationDbContext db,
            AccountingPostingService postingService,
            JournalWorkflowService journalWorkflowService)
        {
            _db = db;
            _postingService = postingService;
            _journalWorkflowService = journalWorkflowService;
        }

        public sealed record ValidationError(string Field, string Message);

        public sealed record CreateResult(CustomerReceipt Receipt, string? NotPostedReason);

        /// <summary>Everything that stops the receipt being recorded, by form field.</summary>
        public async Task<List<ValidationError>> ValidateAsync(int companyId, CustomerReceiptCreateViewModel model)
        {
            var errors = new List<ValidationError>();

            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
            {
                errors.Add(new(string.Empty, "No company record found."));
            }

            var invoice = await LoadInvoiceAsync(model.CustomerInvoiceId);

            if (invoice == null)
            {
                errors.Add(new(nameof(model.CustomerInvoiceId), "Selected invoice was not found."));
            }
            else if (invoice.JournalEntry == null || !invoice.JournalEntry.IsPosted)
            {
                // A receipt credits Accounts Receivable. A draft invoice has not debited it,
                // so receiving against one would reduce a receivable that was never raised.
                errors.Add(new(nameof(model.CustomerInvoiceId),
                    $"Invoice {invoice.InvoiceNumber} has not been posted yet. Post it from Customer Invoices first."));
            }

            if (model.Amount <= 0)
            {
                errors.Add(new(nameof(model.Amount), "Receipt amount must be greater than zero."));
            }

            if (invoice != null)
            {
                var outstanding = invoice.TotalAmount - invoice.AmountPaid;
                if (model.Amount > outstanding)
                {
                    errors.Add(new(nameof(model.Amount), "Receipt amount cannot exceed the outstanding invoice balance."));
                }
            }

            if (!string.IsNullOrWhiteSpace(model.ReceiptNumber))
            {
                var receiptNumberExists = await _db.CustomerReceipts.AnyAsync(x => x.ReceiptNumber == model.ReceiptNumber);
                if (receiptNumberExists)
                {
                    errors.Add(new(nameof(model.ReceiptNumber), "Receipt number already exists."));
                }
            }

            return errors;
        }

        public Task<CustomerInvoice?> LoadInvoiceAsync(int invoiceId) =>
            _db.CustomerInvoices
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                .Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == invoiceId);

        /// <summary>
        /// Saves the receipt and its journal, then posts it. Call <see cref="ValidateAsync"/>
        /// first. Throws if the journal cannot be built, in which case nothing is saved.
        /// </summary>
        public async Task<CreateResult> CreateAsync(int companyId, CustomerReceiptCreateViewModel model)
        {
            // The receipt and its journal must succeed or fail together. Without this
            // transaction a posting failure left the receipt saved but unposted, and
            // because the invoice's AmountPaid is only updated by a successful posting,
            // the "cannot exceed outstanding" check would not stop the user entering the
            // same payment again - producing duplicate orphan receipts.
            await using var transaction = await _db.Database.BeginTransactionAsync();

            // The invoice is guaranteed posted by the validation. This used to build
            // its journal on the fly, which under draft-first produced only a draft.

            var receipt = new CustomerReceipt
            {
                CompanyId = companyId,
                CustomerInvoiceId = model.CustomerInvoiceId,
                ReceiptNumber = string.IsNullOrWhiteSpace(model.ReceiptNumber)
                    ? await GenerateReceiptNumberAsync()
                    : model.ReceiptNumber.Trim(),
                ReceiptDate = model.ReceiptDate,
                Amount = model.Amount,
                PaymentMethod = model.PaymentMethod
            };

            _db.CustomerReceipts.Add(receipt);
            await _db.SaveChangesAsync();

            var journal = await _postingService.PostCustomerReceiptAsync(receipt.Id);

            await transaction.CommitAsync();

            // "Save and Post" posts: the receipt goes into the accounts now, through the
            // same checks as Journal Inquiry. If one of them refuses, it stays a draft.
            var notPosted = await _journalWorkflowService.PostNewEntryAsync(journal.Id);
            return new CreateResult(receipt, notPosted);
        }

        private async Task<string> GenerateReceiptNumberAsync()
        {
            var today = DateTime.Today;
            var count = await _db.CustomerReceipts.CountAsync(x => x.ReceiptDate.Date == today) + 1;
            return $"REC-{today:yyyyMMdd}-{count:D3}";
        }
    }
}
