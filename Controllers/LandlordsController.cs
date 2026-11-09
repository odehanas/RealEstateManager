using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>The owners of the managed properties: who they are, their bank account, and how their rent is collected.</summary>
    public class LandlordsController : Controller
    {
        private readonly LandlordService _landlords;

        public LandlordsController(LandlordService landlords)
        {
            _landlords = landlords;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewBag.CanEdit = await HttpContext.RequestServices.GetRequiredService<PermissionService>()
                .HasPermissionAsync(User, AppPermissions.MasterDataManage);
            return View(await _landlords.GetLandlordsAsync());
        }

        [HttpGet]
        public IActionResult Create() => View("Form", new LandlordEditViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(LandlordEditViewModel model)
        {
            model.Id = 0;
            return await SaveAsync(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var model = await _landlords.GetLandlordAsync(id);
            return model == null ? NotFound() : View("Form", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, LandlordEditViewModel model)
        {
            model.Id = id;
            return await SaveAsync(model);
        }

        private async Task<IActionResult> SaveAsync(LandlordEditViewModel model)
        {
            var saved = await _landlords.SaveLandlordAsync(ModelState, User.GetRequiredCurrentCompanyId(), model);
            if (saved == null)
                return View("Form", model);

            TempData["SuccessMessage"] = $"Landlord {model.Name.Trim()} saved.";
            return RedirectToAction(nameof(Index));
        }
    }
}
