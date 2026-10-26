using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class CustomerReceiptsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly CustomerReceiptService _receiptService;

        public CustomerReceiptsController(ApplicationDbContext db, CustomerReceiptService receiptService)
        {
            _db = db;
            _receiptService = receiptService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var receipts = await _db.CustomerReceipts
                .Include(x => x.CustomerInvoice)
                    .ThenInclude(x => x!.Lease)
                        .ThenInclude(x => x!.Unit)
                            .ThenInclude(x => x!.Property)
                .Include(x => x.CustomerInvoice)
                    .ThenInclude(x => x!.Lease)
                        .ThenInclude(x => x!.Tenant)
                .OrderByDescending(x => x.ReceiptDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return View(receipts);
        }

        [HttpGet]
        public async Task<IActionResult> Create(int? invoiceId)
        {
            await LoadInvoicesAsync(invoiceId);

            var model = new CustomerReceiptCreateViewModel();

            if (invoiceId.HasValue)
            {
                var invoice = await _db.CustomerInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId.Value);
                if (invoice != null)
                {
                    model.CustomerInvoiceId = invoice.Id;
                    model.Amount = invoice.TotalAmount - invoice.AmountPaid;
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CustomerReceiptCreateViewModel model)
        {
            await LoadInvoicesAsync(model.CustomerInvoiceId);

            var companyId = User.GetRequiredCurrentCompanyId();
            foreach (var error in await _receiptService.ValidateAsync(companyId, model))
            {
                ModelState.AddModelError(error.Field, error.Message);
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var result = await _receiptService.CreateAsync(companyId, model);
                if (result.NotPostedReason != null)
                    TempData["PostingWarning"] = $"Receipt {result.Receipt.ReceiptNumber} was saved as a draft but not posted: {result.NotPostedReason}";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                // Show the reason on the form instead of a blank 500 page. The rollback
                // means nothing was saved, so the user can correct and retry safely.
                ModelState.AddModelError(string.Empty,
                    $"The receipt could not be posted, so nothing was saved. {ex.Message}");
                return View(model);
            }
        }

        private async Task LoadInvoicesAsync(int? selectedInvoiceId = null)
        {
            var invoices = await _db.CustomerInvoices
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                // Posted invoices with a balance. A draft is not yet a receivable.
                .Where(x => x.TotalAmount > x.AmountPaid
                            && x.JournalEntry != null
                            && x.JournalEntry.IsPosted)
                .OrderByDescending(x => x.InvoiceDate)
                .ToListAsync();

            ViewBag.Invoices = invoices;
            ViewBag.SelectedInvoiceId = selectedInvoiceId;
        }
    }
}
