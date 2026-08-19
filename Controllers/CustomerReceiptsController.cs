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
        private readonly AccountingPostingService _postingService;

        public CustomerReceiptsController(ApplicationDbContext db, AccountingPostingService postingService)
        {
            _db = db;
            _postingService = postingService;
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
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
            {
                ModelState.AddModelError(string.Empty, "No company record found.");
            }

            var invoice = await _db.CustomerInvoices
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                .FirstOrDefaultAsync(x => x.Id == model.CustomerInvoiceId);

            if (invoice == null)
            {
                ModelState.AddModelError(nameof(model.CustomerInvoiceId), "Selected invoice was not found.");
            }

            if (model.Amount <= 0)
            {
                ModelState.AddModelError(nameof(model.Amount), "Receipt amount must be greater than zero.");
            }

            if (invoice != null)
            {
                var outstanding = invoice.TotalAmount - invoice.AmountPaid;
                if (model.Amount > outstanding)
                {
                    ModelState.AddModelError(nameof(model.Amount), "Receipt amount cannot exceed the outstanding invoice balance.");
                }
            }

            if (!string.IsNullOrWhiteSpace(model.ReceiptNumber))
            {
                var receiptNumberExists = await _db.CustomerReceipts.AnyAsync(x => x.ReceiptNumber == model.ReceiptNumber);
                if (receiptNumberExists)
                {
                    ModelState.AddModelError(nameof(model.ReceiptNumber), "Receipt number already exists.");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // The receipt and its journal must succeed or fail together. Without this
            // transaction a posting failure left the receipt saved but unposted, and
            // because the invoice's AmountPaid is only updated by a successful posting,
            // the "cannot exceed outstanding" check above would not stop the user
            // entering the same payment again - producing duplicate orphan receipts.
            // PdcReceiptsController already works this way; this brings receipts in line.
            try
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();

                if (!invoice!.JournalEntryId.HasValue)
                {
                    await _postingService.PostCustomerInvoiceAsync(invoice.Id);
                }

                var receipt = new CustomerReceipt
                {
                    CompanyId = companyId,
                    CustomerInvoiceId = invoice.Id,
                    ReceiptNumber = string.IsNullOrWhiteSpace(model.ReceiptNumber)
                        ? await GenerateReceiptNumberAsync()
                        : model.ReceiptNumber.Trim(),
                    ReceiptDate = model.ReceiptDate,
                    Amount = model.Amount,
                    PaymentMethod = model.PaymentMethod
                };

                _db.CustomerReceipts.Add(receipt);
                await _db.SaveChangesAsync();

                await _postingService.PostCustomerReceiptAsync(receipt.Id);

                await transaction.CommitAsync();
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
                .Where(x => x.TotalAmount > x.AmountPaid)
                .OrderByDescending(x => x.InvoiceDate)
                .ToListAsync();

            ViewBag.Invoices = invoices;
            ViewBag.SelectedInvoiceId = selectedInvoiceId;
        }

        private async Task<string> GenerateReceiptNumberAsync()
        {
            var today = DateTime.Today;
            var count = await _db.CustomerReceipts.CountAsync(x => x.ReceiptDate.Date == today) + 1;
            return $"REC-{today:yyyyMMdd}-{count:D3}";
        }
    }
}
