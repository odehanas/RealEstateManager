using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The branches of the company being worked in. A company administrator sees the
    /// group; opening a branch is for an administrator of the mother company.
    /// </summary>
    [Authorize(Policy = UserManagerRequirement.PolicyName)]
    public class BranchesController : Controller
    {
        private readonly AdminManagementService _adminManagementService;

        public BranchesController(AdminManagementService adminManagementService)
        {
            _adminManagementService = adminManagementService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = await _adminManagementService.GetBranchesAsync(User, User.GetRequiredCurrentCompanyId());
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            if (!await _adminManagementService.CanOpenBranchAsync(User, companyId))
                return Forbid();

            return View(await _adminManagementService.BuildBranchFormAsync(companyId));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BranchFormViewModel model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            if (!await _adminManagementService.CanOpenBranchAsync(User, companyId))
                return Forbid();

            model.MotherCompanyName = (await _adminManagementService.BuildBranchFormAsync(companyId)).MotherCompanyName;
            ModelState.Remove(nameof(model.MotherCompanyName));

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                await _adminManagementService.OpenBranchAsync(User, companyId, model);
                TempData["SuccessMessage"] =
                    $"Branch \"{model.Name.Trim()}\" is open. Switch to it with the company list at the top of the page.";
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }
    }
}
