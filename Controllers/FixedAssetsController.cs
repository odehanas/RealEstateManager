using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class FixedAssetsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly FixedAssetService _fixedAssetService;

        public FixedAssetsController(ApplicationDbContext db, FixedAssetService fixedAssetService)
        {
            _db = db;
            _fixedAssetService = fixedAssetService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? postingDate)
        {
            var model = await _fixedAssetService.GetRegisterAsync((postingDate ?? DateTime.Today).Date);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var model = new FixedAssetCreateViewModel
            {
                AcquisitionDate = DateTime.Today,
                InServiceDate = DateTime.Today,
                UsefulLifeMonths = 60
            };

            await ApplyDefaultMappingsAsync(model);
            await LoadLookupsAsync();

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(FixedAssetCreateViewModel model)
        {
            await ApplyDefaultMappingsAsync(model);
            await LoadLookupsAsync();

            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
            {
                ModelState.AddModelError(string.Empty, "No company record found.");
            }
            else if (await _db.FixedAssets.AnyAsync(x => x.CompanyId == companyId && x.AssetCode == model.AssetCode))
            {
                ModelState.AddModelError(nameof(model.AssetCode), "Asset code already exists.");
            }

            if (string.IsNullOrWhiteSpace(model.AssetCode))
            {
                ModelState.AddModelError(nameof(model.AssetCode), "Asset code is required.");
            }

            if (string.IsNullOrWhiteSpace(model.Name))
            {
                ModelState.AddModelError(nameof(model.Name), "Asset name is required.");
            }

            if (model.Cost < 0m)
            {
                ModelState.AddModelError(nameof(model.Cost), "Initial acquisition cost cannot be negative.");
            }

            if (model.ResidualValue < 0m)
            {
                ModelState.AddModelError(nameof(model.ResidualValue), "Residual value cannot be negative.");
            }

            if (model.Cost > 0m && model.ResidualValue >= model.Cost)
            {
                ModelState.AddModelError(nameof(model.ResidualValue), "Residual value must be less than the initial acquisition cost.");
            }

            if (model.Cost > 0m && !model.InitialCreditAccountId.HasValue)
            {
                ModelState.AddModelError(nameof(model.InitialCreditAccountId), "Select the payment / payable account for the initial acquisition cost.");
            }

            var companySetting = await _db.CompanyAccountSettings.FirstOrDefaultAsync(x => x.CompanyId == companyId);
            if (model.Cost > 0m &&
                companySetting?.AccountsPayableAccountId.HasValue == true &&
                model.InitialCreditAccountId == companySetting.AccountsPayableAccountId &&
                !model.InitialVendorId.HasValue)
            {
                ModelState.AddModelError(nameof(model.InitialVendorId), "Select the vendor when the source account is Accounts Payable.");
            }

            if (model.UsefulLifeMonths <= 0)
            {
                ModelState.AddModelError(nameof(model.UsefulLifeMonths), "Useful life in months must be greater than zero.");
            }

            if (model.OpeningCost < 0m)
            {
                ModelState.AddModelError(nameof(model.OpeningCost), "Opening cost cannot be negative.");
            }

            if (model.OpeningAccumulatedDepreciation < 0m)
            {
                ModelState.AddModelError(nameof(model.OpeningAccumulatedDepreciation), "Opening accumulated depreciation cannot be negative.");
            }

            if (model.OpeningCost > 0m && model.OpeningAccumulatedDepreciation > model.OpeningCost)
            {
                ModelState.AddModelError(nameof(model.OpeningAccumulatedDepreciation), "Opening accumulated depreciation cannot exceed opening cost.");
            }

            if (model.InServiceDate.Date < model.AcquisitionDate.Date)
            {
                ModelState.AddModelError(nameof(model.InServiceDate), "In-service date cannot be earlier than acquisition date.");
            }

            if (!model.AssetAccountId.HasValue)
            {
                ModelState.AddModelError(nameof(model.AssetAccountId), "Asset account is required.");
            }

            if (!model.AccumulatedDepreciationAccountId.HasValue)
            {
                ModelState.AddModelError(nameof(model.AccumulatedDepreciationAccountId), "Accumulated depreciation account is required.");
            }

            if (!model.DepreciationExpenseAccountId.HasValue)
            {
                ModelState.AddModelError(nameof(model.DepreciationExpenseAccountId), "Depreciation expense account is required.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var fixedAssetId = await _fixedAssetService.CreateDraftAssetAsync(companyId, model);
                TempData["SuccessMessage"] = "Fixed asset draft saved. Add acquisition costs and capitalize it when ready for use.";
                return RedirectToAction(nameof(Acquisition), new { id = fixedAssetId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Acquisition(int id)
        {
            try
            {
                await LoadLookupsAsync();
                var model = await _fixedAssetService.GetAcquisitionViewAsync(id);
                return View(model);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCost(FixedAssetAcquisitionCostEntryViewModel model)
        {
            try
            {
                await _fixedAssetService.AddAcquisitionCostAsync(model);
                TempData["SuccessMessage"] = "Acquisition cost added successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Acquisition), new { id = model.FixedAssetId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Capitalize(FixedAssetCapitalizeViewModel model)
        {
            try
            {
                await _fixedAssetService.CapitalizeAsync(model);
                TempData["SuccessMessage"] = "Fixed asset capitalized successfully. Depreciation schedule has been created.";
                return RedirectToAction(nameof(Depreciation), new { id = model.FixedAssetId });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Acquisition), new { id = model.FixedAssetId });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Depreciation(int id)
        {
            try
            {
                var model = await _fixedAssetService.GetDepreciationViewAsync(id);
                return View(model);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunMonthly(DateTime postingDate)
        {
            try
            {
                var result = await _fixedAssetService.PostDepreciationUpToAsync(postingDate.Date);
                TempData["SuccessMessage"] = result.PostedCount == 0
                    ? "No depreciation schedules were due for posting."
                    : $"Posted {result.PostedCount} depreciation schedules totaling {result.PostedAmount:N2}.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Index), new { postingDate = postingDate.ToString("yyyy-MM-dd") });
        }

        private async Task ApplyDefaultMappingsAsync(FixedAssetCreateViewModel model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies
                .Include(x => x.AccountSetting)
                .FirstOrDefaultAsync(x => x.Id == companyId);

            var setting = company?.AccountSetting;
            if (setting == null)
                return;

            model.AssetAccountId ??= setting.FixedAssetDefaultAccountId;
            model.AccumulatedDepreciationAccountId ??= setting.AccumulatedDepreciationDefaultAccountId;
            model.DepreciationExpenseAccountId ??= setting.DepreciationExpenseAccountId;
            model.InitialCreditAccountId ??= setting.AccountsPayableAccountId ?? setting.DefaultCashAccountId;
        }

        private async Task LoadLookupsAsync()
        {
            ViewBag.Properties = await _db.Properties
                .Where(x => x.IsActive)
                .OrderBy(x => x.Code)
                .ToListAsync();

            ViewBag.Accounts = await _db.Accounts
                .Where(x => x.IsActive && x.IsPostable)
                .OrderBy(x => x.Code)
                .ToListAsync();

            ViewBag.Vendors = await _db.Vendors
                .Where(x => x.IsActive)
                .OrderBy(x => x.Code)
                .ToListAsync();
        }
    }
}
