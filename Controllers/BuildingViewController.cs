using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>Reporting ▸ Building View: each building drawn floor by floor, units coloured by rent health.</summary>
    public class BuildingViewController : Controller
    {
        private readonly BuildingViewService _buildings;

        public BuildingViewController(BuildingViewService buildings)
        {
            _buildings = buildings;
        }

        /// <summary>All buildings side by side.</summary>
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            return View(await _buildings.GetPortfolioAsync());
        }

        /// <summary>One building, floor by floor.</summary>
        [HttpGet]
        public async Task<IActionResult> Property(int id, int? unit)
        {
            var building = await _buildings.GetBuildingAsync(id);
            if (building == null)
                return NotFound();

            ViewBag.SelectedUnitId = unit;
            return View(building);
        }
    }
}
