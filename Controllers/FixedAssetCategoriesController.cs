using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The company's fixed asset categories and the depreciation / amortization programme
    /// of each. Open to whoever keeps the books and to the company's administrator - see
    /// PermissionCatalog.ControllerPermissionMap.
    /// </summary>
    [Authorize]
    public class FixedAssetCategoriesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly FixedAssetCategoryService _categories;

        public FixedAssetCategoriesController(ApplicationDbContext db, FixedAssetCategoryService categories)
        {
            _db = db;
            _categories = categories;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var companyId = User.GetCurrentCompanyId();
            if (!companyId.HasValue)
                return Forbid();

            return View(await _categories.ListAsync(companyId.Value));
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            if (!User.GetCurrentCompanyId().HasValue)
                return Forbid();

            await LoadAccountsAsync();
            return View("Form", new FixedAssetCategory { UsefulLifeMonths = 60 });
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var companyId = User.GetCurrentCompanyId();
            if (!companyId.HasValue)
                return Forbid();

            var category = await _categories.FindAsync(companyId.Value, id);
            if (category == null)
                return NotFound();

            await LoadAccountsAsync();
            return View("Form", category);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(
            int id, string? name, string? assetClass, string? depreciationMethod, int usefulLifeMonths,
            decimal? decliningRatePercent, decimal residualPercent,
            int? assetAccountId, int? accumulatedDepreciationAccountId, int? depreciationExpenseAccountId)
        {
            var companyId = User.GetCurrentCompanyId();
            if (!companyId.HasValue)
                return Forbid();

            var input = new FixedAssetCategory
            {
                Id = id,
                Name = name ?? string.Empty,
                AssetClass = assetClass ?? string.Empty,
                DepreciationMethod = depreciationMethod ?? string.Empty,
                UsefulLifeMonths = usefulLifeMonths,
                DecliningRatePercent = decliningRatePercent,
                ResidualPercent = residualPercent,
                AssetAccountId = assetAccountId,
                AccumulatedDepreciationAccountId = accumulatedDepreciationAccountId,
                DepreciationExpenseAccountId = depreciationExpenseAccountId
            };

            var error = await _categories.SaveAsync(companyId.Value, input);
            if (error != null)
            {
                ViewBag.Error = error;
                await LoadAccountsAsync();
                return View("Form", input);
            }

            TempData["SuccessMessage"] = id == 0
                ? "Category added."
                : "Category saved. Assets already in the register keep their own method and useful life.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int id)
        {
            var companyId = User.GetCurrentCompanyId();
            if (!companyId.HasValue)
                return Forbid();

            var error = await _categories.ToggleAsync(companyId.Value, id);
            if (error != null)
                TempData["ErrorMessage"] = error;
            else
                TempData["SuccessMessage"] = "Saved.";
            return RedirectToAction(nameof(Index));
        }

        private async Task LoadAccountsAsync()
        {
            ViewBag.Accounts = await _db.Accounts.AsNoTracking()
                .Where(x => x.IsActive && x.IsPostable)
                .OrderBy(x => x.Code)
                .ToListAsync();
        }
    }
}
