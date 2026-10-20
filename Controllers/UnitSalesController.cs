using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>Operations ▸ Unit Sales: sale contracts, payment plans, buyer payments and handover.</summary>
    public class UnitSalesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UnitSaleService _sales;

        public UnitSalesController(ApplicationDbContext db, UnitSaleService sales)
        {
            _db = db;
            _sales = sales;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            return View(await _sales.GetSalesAsync());
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await LoadCreateListsAsync();
            return View();
        }

        /// <summary>The VAT treatment a sale of the unit on that date would get, with the reason.</summary>
        [HttpGet]
        public async Task<IActionResult> VatPreview(int unitId, DateTime contractDate)
        {
            try
            {
                var decision = await _sales.PreviewVatAsync(unitId, contractDate);
                return Json(new { category = decision.Category, rate = Domain.VatCategories.IsTaxable(decision.Category) ? 5 : 0, reason = decision.Reason });
            }
            catch (InvalidOperationException ex)
            {
                return Json(new { category = "", rate = 0, reason = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            int unitId, int buyerId, DateTime contractDate, decimal salePrice, string? notes,
            List<string>? planDescription, List<DateTime?>? planDueDate, List<decimal?>? planAmount)
        {
            var plan = new List<(string, DateTime, decimal)>();
            for (var i = 0; i < (planAmount?.Count ?? 0); i++)
            {
                if (planAmount![i] is not { } amount || amount == 0m)
                    continue;
                plan.Add((planDescription?.ElementAtOrDefault(i) ?? "", planDueDate?.ElementAtOrDefault(i) ?? contractDate, amount));
            }

            try
            {
                var userName = User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value;
                var sale = await _sales.CreateAsync(unitId, buyerId, contractDate, salePrice, plan, notes, userName);
                TempData["SuccessMessage"] = $"Contract {sale.ContractNumber} signed: {sale.TotalPrice:N2} including VAT ({Domain.VatCategories.Describe(sale.VatCategory)}).";
                return RedirectToAction(nameof(Details), new { id = sale.Id });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                await LoadCreateListsAsync();
                return View();
            }
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var sale = await _sales.GetAsync(id);
            if (sale == null)
                return NotFound();

            ViewBag.UnitCost = await _sales.GetUnitCostAsync(sale.UnitId);
            ViewBag.Accounts = (await _sales.GetReceiptAccountsAsync())
                .Select(x => new SelectListItem($"{x.Code} - {x.Name}", x.Id.ToString(), x.Code == "11100"))
                .ToList();
            return View(sale);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordReceipt(int id, DateTime receiptDate, decimal amount, string paymentMethod, int accountId, string? reference)
        {
            try
            {
                var receipt = await _sales.RecordReceiptAsync(id, receiptDate, amount, paymentMethod, accountId, reference);
                TempData["SuccessMessage"] = $"Payment {receipt.ReceiptNumber} of {receipt.Amount:N2} recorded and posted" +
                                             (receipt.TaxInvoiceNumber != null ? $" (tax invoice {receipt.TaxInvoiceNumber}, VAT {receipt.VatAmount:N2})." : ".");
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Handover(int id, DateTime handoverDate, string? buyerTitleDeed)
        {
            try
            {
                await _sales.HandoverAsync(id, handoverDate, buyerTitleDeed);
                TempData["SuccessMessage"] = "Unit handed over: the sale is now revenue and the unit's cost is in cost of sales.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            try
            {
                await _sales.CancelAsync(id);
                TempData["SuccessMessage"] = "Contract cancelled; the unit is available again.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task LoadCreateListsAsync()
        {
            ViewBag.Units = (await _sales.GetSaleableUnitsAsync())
                .Select(x => new SelectListItem($"{x.Property!.Code} / {x.UnitNumber} - {x.UsageType} {x.AreaSqFt:N0} sq ft", x.Id.ToString()))
                .ToList();
            ViewBag.Buyers = await _db.Tenants.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.FullName)
                .Select(x => new SelectListItem(x.Code + " - " + x.FullName, x.Id.ToString()))
                .ToListAsync();
        }
    }
}
