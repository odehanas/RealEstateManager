using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Vendor payments, SAP style: choose the vendor, then pay their open bills in full or
    /// in part (each payment gets a clearing document number), pay an advance before the
    /// bill (kept on Advances to Suppliers), and later apply the advance to bills.
    /// </summary>
    public class VendorPaymentsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly VendorClearingService _clearing;

        public VendorPaymentsController(ApplicationDbContext db, VendorClearingService clearing)
        {
            _db = db;
            _clearing = clearing;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var payments = await _db.VendorPayments.AsNoTracking()
                .Include(x => x.Vendor)
                .Include(x => x.VendorBill).ThenInclude(x => x!.Vendor)
                .Include(x => x.JournalEntry)
                .OrderByDescending(x => x.PaymentDate).ThenByDescending(x => x.Id)
                .Take(500)
                .ToListAsync();

            var ids = payments.Select(x => x.Id).ToList();
            var applied = await _db.VendorClearingLines.AsNoTracking()
                .Where(x => x.VendorClearing!.VendorPaymentId != null && ids.Contains(x.VendorClearing.VendorPaymentId.Value))
                .Select(x => new { PaymentId = x.VendorClearing!.VendorPaymentId!.Value, x.VendorBill!.BillNumber })
                .ToListAsync();
            ViewBag.Bills = applied.GroupBy(x => x.PaymentId).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.BillNumber)));

            ViewBag.Applications = await _db.VendorClearings.AsNoTracking()
                .Where(x => x.ClearingType == VendorClearingTypes.Advance)
                .Include(x => x.Vendor).Include(x => x.AdvancePayment).Include(x => x.Lines).ThenInclude(x => x.VendorBill)
                .OrderByDescending(x => x.ClearingDate).ThenByDescending(x => x.Id)
                .Take(200)
                .ToListAsync();

            return View(payments);
        }

        [HttpGet]
        public async Task<IActionResult> Create(int? vendorId, int? billId, bool advance = false)
        {
            if (billId.HasValue && !vendorId.HasValue)
                vendorId = await _db.VendorBills.Where(x => x.Id == billId.Value).Select(x => (int?)x.VendorId).FirstOrDefaultAsync();

            await LoadListsAsync();
            ViewBag.PreselectBillId = billId;
            return View(new VendorClearingService.PaymentRequest { VendorId = vendorId ?? 0, IsAdvance = advance });
        }

        /// <summary>The vendor's open bills and open advances, for the payment and application screens.</summary>
        [HttpGet]
        public async Task<IActionResult> OpenItems(int vendorId)
        {
            return Json(new
            {
                bills = await _clearing.GetOpenItemsAsync(vendorId),
                advances = await _clearing.GetOpenAdvancesAsync(vendorId)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(VendorClearingService.PaymentRequest request)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            request.Allocations = request.Allocations.Where(x => x.Amount > 0m).ToList();

            var error = await _clearing.ValidateAsync(companyId, request);
            if (error != null)
            {
                ModelState.AddModelError(string.Empty, error);
                await LoadListsAsync();
                return View(request);
            }

            try
            {
                var result = await _clearing.PayAsync(companyId, request, CurrentUserName());
                if (result.NotPostedReason != null)
                    TempData["PostingWarning"] = $"{result.Message} {result.NotPostedReason}";
                else
                    TempData["PaymentSuccess"] = result.Message + " Posted to the accounts.";
                return RedirectToAction(nameof(Details), new { id = result.Payment!.Id });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"The payment could not be recorded, so nothing was saved. {ex.Message}");
                await LoadListsAsync();
                return View(request);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var payment = await _db.VendorPayments.AsNoTracking()
                .Include(x => x.Vendor)
                .Include(x => x.VendorBill)
                .Include(x => x.PaidFromAccount)
                .Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (payment == null)
                return NotFound();

            // The payment's own clearing document, and for an advance every application of it.
            ViewBag.Clearings = await _db.VendorClearings.AsNoTracking()
                .Include(x => x.Lines).ThenInclude(x => x.VendorBill)
                .Include(x => x.JournalEntry)
                .Where(x => x.VendorPaymentId == id || x.AdvancePaymentId == id)
                .OrderBy(x => x.ClearingDate).ThenBy(x => x.Id)
                .ToListAsync();
            return View(payment);
        }

        // ---------------------------------------------------------------- advances (SAP F-54)

        [HttpGet]
        public async Task<IActionResult> ApplyAdvance(int? vendorId, int? advanceId)
        {
            if (advanceId.HasValue && !vendorId.HasValue)
                vendorId = await _db.VendorPayments.Where(x => x.Id == advanceId.Value).Select(x => x.VendorId).FirstOrDefaultAsync();

            await LoadListsAsync();
            ViewBag.VendorId = vendorId;
            ViewBag.AdvanceId = advanceId;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApplyAdvance(int vendorId, int advanceId, DateTime clearingDate, List<VendorClearingService.Allocation> allocations)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            allocations = allocations.Where(x => x.Amount > 0m).ToList();

            var error = await _clearing.ValidateAdvanceApplicationAsync(advanceId, allocations);
            if (error != null)
            {
                TempData["PaymentError"] = error;
                return RedirectToAction(nameof(ApplyAdvance), new { vendorId, advanceId });
            }

            try
            {
                var result = await _clearing.ApplyAdvanceAsync(companyId, advanceId, allocations,
                    clearingDate == default ? DateTime.Today : clearingDate, CurrentUserName());
                if (result.NotPostedReason != null)
                    TempData["PostingWarning"] = $"{result.Message} {result.NotPostedReason}";
                else
                    TempData["PaymentSuccess"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = advanceId });
            }
            catch (Exception ex)
            {
                TempData["PaymentError"] = $"The advance could not be applied, so nothing was saved. {ex.Message}";
                return RedirectToAction(nameof(ApplyAdvance), new { vendorId, advanceId });
            }
        }

        private async Task LoadListsAsync()
        {
            ViewBag.Vendors = await _db.Vendors.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new SelectListItem(x.Code + " - " + x.Name, x.Id.ToString()))
                .ToListAsync();

            ViewBag.CashAccounts = await _db.Accounts.AsNoTracking()
                .Where(x => x.Code.StartsWith("111") && x.IsPostable && !x.IsGroup && x.IsActive)
                .OrderBy(x => x.Code)
                .Select(x => new SelectListItem(x.Code + " " + x.Name, x.Id.ToString()))
                .ToListAsync();

            ViewBag.DefaultCashAccountId = await _db.CompanyAccountSettings.AsNoTracking()
                .Select(x => x.DefaultCashAccountId).FirstOrDefaultAsync();
        }

        private string CurrentUserName() =>
            User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value ?? "User";
    }
}
