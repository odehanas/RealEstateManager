using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class VendorBillsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _postingService;
        private readonly JournalWorkflowService _journalWorkflowService;

        public VendorBillsController(ApplicationDbContext db, AccountingPostingService postingService,
            JournalWorkflowService journalWorkflowService)
        {
            _db = db;
            _postingService = postingService;
            _journalWorkflowService = journalWorkflowService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var bills = await _db.VendorBills
                .Include(x => x.Vendor)
                .Include(x => x.Property)
                .Include(x => x.Payments)
                .OrderByDescending(x => x.BillDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return View(bills);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await LoadListsAsync();
            return View(new VendorBillCreateViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(VendorBillCreateViewModel model)
        {
            await LoadListsAsync();

            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
            {
                ModelState.AddModelError(string.Empty, "No company record found.");
            }

            if (!await _db.Vendors.AnyAsync(x => x.Id == model.VendorId))
            {
                ModelState.AddModelError(nameof(model.VendorId), "Selected vendor was not found.");
            }

            if (model.PropertyId.HasValue && !await _db.Properties.AnyAsync(x => x.Id == model.PropertyId.Value))
            {
                ModelState.AddModelError(nameof(model.PropertyId), "Selected property was not found.");
            }

            if (await _db.VendorBills.AnyAsync(x => x.BillNumber == model.BillNumber))
            {
                ModelState.AddModelError(nameof(model.BillNumber), "Bill number already exists.");
            }

            if (model.Subtotal < 0 || model.VatAmount < 0)
            {
                ModelState.AddModelError(string.Empty, "Amounts cannot be negative.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var bill = new VendorBill
            {
                CompanyId = companyId,
                VendorId = model.VendorId,
                PropertyId = model.PropertyId,
                BillNumber = model.BillNumber,
                BillDate = model.BillDate,
                DueDate = model.DueDate,
                Subtotal = model.Subtotal,
                VatAmount = model.VatAmount,
                TotalAmount = model.Subtotal + model.VatAmount,
                AmountPaid = 0m,
                ExpenseCategory = model.ExpenseCategory,
                InputTaxAttribution = model.InputTaxAttribution,
                SupplierTrn = string.IsNullOrWhiteSpace(model.SupplierTrn) ? null : model.SupplierTrn.Trim(),
                Status = "Draft"
            };

            _db.VendorBills.Add(bill);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Post(int id)
        {
            // A bill is already a draft DOCUMENT, so Post writes the journal and posts
            // it in one click. Reset to Draft is then available from Journal Inquiry.
            try
            {
                var journal = await _postingService.PostVendorBillAsync(id);
                var result = await _journalWorkflowService.PostAsync(journal.Id);

                TempData[result.Success ? "BillSuccess" : "BillError"] = result.Message;
            }
            catch (Exception ex)
            {
                TempData["BillError"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task LoadListsAsync()
        {
            ViewBag.Vendors = await _db.Vendors.OrderBy(x => x.Code).ToListAsync();
            ViewBag.Properties = await _db.Properties.OrderBy(x => x.Code).ToListAsync();
        }
    }
}
