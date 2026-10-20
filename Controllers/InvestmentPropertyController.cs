using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>Reporting ▸ Investment Property Note: carrying amount movement and fair values (IAS 40, cost model).</summary>
    public class InvestmentPropertyController : Controller
    {
        private readonly InvestmentPropertyNoteService _note;
        private readonly InvestmentPropertySetupService _setup;
        private readonly PermissionService _permissions;

        public InvestmentPropertyController(
            InvestmentPropertyNoteService note, InvestmentPropertySetupService setup, PermissionService permissions)
        {
            _note = note;
            _setup = setup;
            _permissions = permissions;
        }

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? fromDate, DateTime? toDate)
        {
            var to = (toDate ?? DateTime.Today).Date;
            var from = (fromDate ?? new DateTime(to.Year, 1, 1)).Date;

            if (await _permissions.HasPermissionAsync(User, AppPermissions.AccountingManage))
            {
                ViewBag.SetupCandidates = await _setup.GetCandidatesAsync();
                ViewBag.Unallocated = await _setup.GetUnallocatedAsync();
            }

            try
            {
                return View(await _note.GetNoteAsync(from, to));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(new InvestmentPropertyNoteDto { FromDate = from, ToDate = to });
            }
        }

        /// <summary>Brings an existing building under the cost model. Posts journals, so it needs Manage Accounting.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetUpBuilding(
            int propertyId, DateTime startDate, decimal landCost, decimal buildingCost, int remainingLifeYears, bool allocate)
        {
            try
            {
                var asset = await _setup.SetUpAsync(propertyId, startDate, landCost, buildingCost, remainingLifeYears, allocate);
                TempData["SuccessMessage"] =
                    $"{asset.AssetCode} set up: {buildingCost:N2} depreciated over {remainingLifeYears} years from {startDate:dd/MM/yyyy}, about {buildingCost / (remainingLifeYears * 12):N2} a month. Land is not depreciated.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
