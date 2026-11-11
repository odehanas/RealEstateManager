using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The company's own kinds of maintenance work. Open to whoever runs maintenance
    /// (the Maintenance Manager) and to the company's administrator - see
    /// PermissionCatalog.ControllerPermissionMap.
    /// </summary>
    [Authorize]
    public class MaintenanceCategoriesController : Controller
    {
        private readonly MaintenanceCategoryService _categories;

        public MaintenanceCategoriesController(MaintenanceCategoryService categories)
        {
            _categories = categories;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var companyId = User.GetCurrentCompanyId();
            if (!companyId.HasValue)
                return Forbid();

            return View(await _categories.ListAsync(companyId.Value, User.Identity?.Name));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(string? name)
        {
            var companyId = User.GetCurrentCompanyId();
            if (!companyId.HasValue)
                return Forbid();

            return Done(await _categories.AddAsync(companyId.Value, name, User.Identity?.Name), "Kind of work added.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rename(int id, string? name)
        {
            var companyId = User.GetCurrentCompanyId();
            if (!companyId.HasValue)
                return Forbid();

            return Done(await _categories.RenameAsync(companyId.Value, id, name), "Kind of work renamed, on its jobs too.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int id)
        {
            var companyId = User.GetCurrentCompanyId();
            if (!companyId.HasValue)
                return Forbid();

            return Done(await _categories.ToggleAsync(companyId.Value, id), "Saved.");
        }

        private IActionResult Done(string? error, string success)
        {
            if (error != null)
                TempData["ErrorMessage"] = error;
            else
                TempData["SuccessMessage"] = success;
            return RedirectToAction(nameof(Index));
        }
    }
}
