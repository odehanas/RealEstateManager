using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class VendorPaymentsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _postingService;

        public VendorPaymentsController(ApplicationDbContext db, AccountingPostingService postingService)
        {
            _db = db;
            _postingService = postingService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var payments = await _db.VendorPayments
                .Include(x => x.VendorBill)
                    .ThenInclude(x => x!.Vendor)
                .Include(x => x.VendorBill)
                    .ThenInclude(x => x!.Property)
                .OrderByDescending(x => x.PaymentDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return View(payments);
        }

        [HttpGet]
        public async Task<IActionResult> Create(int? billId)
        {
            await LoadBillsAsync(billId);

            var model = new VendorPaymentCreateViewModel();

            if (billId.HasValue)
            {
                var bill = await _db.VendorBills.FirstOrDefaultAsync(x => x.Id == billId.Value);
                if (bill != null)
                {
                    model.VendorBillId = bill.Id;
                    model.Amount = bill.TotalAmount - bill.AmountPaid;
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(VendorPaymentCreateViewModel model)
        {
            await LoadBillsAsync(model.VendorBillId);

            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
            {
                ModelState.AddModelError(string.Empty, "No company record found.");
            }

            var bill = await _db.VendorBills
                .Include(x => x.Vendor)
                .FirstOrDefaultAsync(x => x.Id == model.VendorBillId);

            if (bill == null)
            {
                ModelState.AddModelError(nameof(model.VendorBillId), "Selected vendor bill was not found.");
            }

            if (model.Amount <= 0)
            {
                ModelState.AddModelError(nameof(model.Amount), "Payment amount must be greater than zero.");
            }

            if (bill != null)
            {
                var outstanding = bill.TotalAmount - bill.AmountPaid;
                if (model.Amount > outstanding)
                {
                    ModelState.AddModelError(nameof(model.Amount), "Payment amount cannot exceed the outstanding bill balance.");
                }
            }

            if (!string.IsNullOrWhiteSpace(model.PaymentNumber))
            {
                var exists = await _db.VendorPayments.AnyAsync(x => x.PaymentNumber == model.PaymentNumber);
                if (exists)
                {
                    ModelState.AddModelError(nameof(model.PaymentNumber), "Payment number already exists.");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!bill!.JournalEntryId.HasValue)
            {
                await _postingService.PostVendorBillAsync(bill.Id);
            }

            var payment = new VendorPayment
            {
                CompanyId = companyId,
                VendorBillId = bill.Id,
                PaymentNumber = string.IsNullOrWhiteSpace(model.PaymentNumber)
                    ? await GeneratePaymentNumberAsync()
                    : model.PaymentNumber.Trim(),
                PaymentDate = model.PaymentDate,
                Amount = model.Amount,
                PaymentMethod = model.PaymentMethod
            };

            _db.VendorPayments.Add(payment);
            await _db.SaveChangesAsync();

            await _postingService.PostVendorPaymentAsync(payment.Id);

            return RedirectToAction(nameof(Index));
        }

        private async Task LoadBillsAsync(int? selectedBillId = null)
        {
            var bills = await _db.VendorBills
                .Include(x => x.Vendor)
                .Include(x => x.Property)
                .Where(x => x.TotalAmount > x.AmountPaid)
                .OrderByDescending(x => x.BillDate)
                .ToListAsync();

            ViewBag.Bills = bills;
            ViewBag.SelectedBillId = selectedBillId;
        }

        private async Task<string> GeneratePaymentNumberAsync()
        {
            var today = DateTime.Today;
            var count = await _db.VendorPayments.CountAsync(x => x.PaymentDate.Date == today) + 1;
            return $"VPAY-{today:yyyyMMdd}-{count:D3}";
        }
    }
}
