using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>Reporting ▸ Building View: each building drawn floor by floor, units coloured by rent health.</summary>
    public class BuildingViewController : Controller
    {
        private readonly BuildingViewService _buildings;
        private readonly BuildingPhotoService _photos;

        public BuildingViewController(BuildingViewService buildings, BuildingPhotoService photos)
        {
            _buildings = buildings;
            _photos = photos;
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
            ViewBag.Photos = await _photos.GetPhotosAsync(id);
            return View(building);
        }

        /// <summary>A building photo, for the photo mode.</summary>
        [HttpGet]
        public async Task<IActionResult> PhotoImage(int id)
        {
            var image = await _photos.GetImageAsync(id);
            if (image == null)
                return NotFound();

            Response.Headers.CacheControl = "private, max-age=3600";
            return File(image.Value.Content, image.Value.ContentType);
        }
    }
}
