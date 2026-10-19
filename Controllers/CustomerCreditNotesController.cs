using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Credit notes against customer invoices, on the same Draft / Post / Reset to Draft
    /// lifecycle as every other document. Under e-invoicing a credit note is the only way
    /// to reduce an invoice that has been reported.
    /// </summary>
    public class CustomerCreditNotesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly CreditNoteService _creditNoteService;
        private readonly AccountingPostingService _postingService;
        private readonly JournalWorkflowService _journalWorkflowService;

        public CustomerCreditNotesController(
            ApplicationDbContext db,
            CreditNoteService creditNoteService,
            AccountingPostingService postingService,
            JournalWorkflowService journalWorkflowService)
        {
            _db = db;
            _creditNoteService = creditNoteService;
            _postingService = postingService;
            _journalWorkflowService = journalWorkflowService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var notes = await _db.CustomerCreditNotes
                .AsNoTracking()
                .OrderByDescending(x => x.CreditNoteDate)
                .ThenByDescending(x => x.Id)
                .Select(x => new CreditNoteListItemViewModel
                {
                    Id = x.Id,
                    CreditNoteNumber = x.CreditNoteNumber,
                    CreditNoteDate = x.CreditNoteDate,
                    CustomerInvoiceId = x.CustomerInvoiceId,
                    InvoiceNumber = x.CustomerInvoice!.TaxInvoiceNumber ?? x.CustomerInvoice.InvoiceNumber,
                    TenantName = x.CustomerInvoice.Lease!.Tenant!.FullName,
                    ReasonCode = x.ReasonCode,
                    Subtotal = x.Subtotal,
                    VatAmount = x.VatAmount,
                    TotalAmount = x.TotalAmount,
                    IsPosted = x.JournalEntry != null && x.JournalEntry.IsPosted,
                    EInvoiceStatus = x.EInvoiceStatus
                })
                .ToListAsync();

            return View(notes);
        }

        [HttpGet]
        public async Task<IActionResult> Create(int invoiceId)
        {
            var model = await _creditNoteService.BuildNewAsync(invoiceId);
            if (model == null)
                return NotFound();

            if (model.Outstanding <= 0m)
            {
                TempData["InvoiceError"] =
                    $"Invoice {model.InvoiceNumber} has nothing outstanding to credit. Amounts already paid are refunded, not credited.";
                return RedirectToAction("Index", "CustomerInvoices");
            }

            return View("Edit", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreditNoteEditViewModel model)
        {
            model.Id = 0;
            return await SaveAsync(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var model = await _creditNoteService.BuildEditAsync(id);
            if (model == null)
                return NotFound();

            var posted = await _db.CustomerCreditNotes
                .AnyAsync(x => x.Id == id && x.JournalEntry != null && x.JournalEntry.IsPosted);

            if (posted)
            {
                TempData["CreditNoteError"] = "This credit note is posted. Reset it to draft before changing it.";
                return RedirectToAction(nameof(Index));
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(CreditNoteEditViewModel model)
        {
            if (model.Id == 0)
                return NotFound();

            return await SaveAsync(model);
        }

        private async Task<IActionResult> SaveAsync(CreditNoteEditViewModel model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var note = await _creditNoteService.SaveAsync(ModelState, companyId, User.GetAppUserId(), model);

            if (note == null)
            {
                await _creditNoteService.RefreshContextAsync(model);
                return View("Edit", model);
            }

            if (model.PostNow)
                return await PostInternalAsync(note.Id);

            TempData["CreditNoteSuccess"] =
                $"Credit note {note.CreditNoteNumber} saved as a draft. Post it to reduce the invoice and reverse the VAT.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Post(int id) => await PostInternalAsync(id);

        private async Task<IActionResult> PostInternalAsync(int id)
        {
            try
            {
                var journal = await _postingService.PostCustomerCreditNoteAsync(id);
                var result = await _journalWorkflowService.PostAsync(journal.Id);

                TempData[result.Success ? "CreditNoteSuccess" : "CreditNoteError"] = result.Success
                    ? "Credit note posted. The invoice balance and the output VAT have been reduced."
                    : result.Message;
            }
            catch (Exception ex)
            {
                TempData["CreditNoteError"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetToDraft(int id)
        {
            var note = await _db.CustomerCreditNotes.FirstOrDefaultAsync(x => x.Id == id);
            if (note?.JournalEntryId == null)
            {
                TempData["CreditNoteError"] = "This credit note has no journal entry to reset.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _journalWorkflowService.ResetToDraftAsync(note.JournalEntryId.Value);
            TempData[result.Success ? "CreditNoteSuccess" : "CreditNoteError"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        /// <summary>The printable credit note.</summary>
        [HttpGet]
        public async Task<IActionResult> Document(int id)
        {
            var model = await _creditNoteService.BuildDocumentAsync(id);
            return model == null ? NotFound() : View(model);
        }
    }
}
