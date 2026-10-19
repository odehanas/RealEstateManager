using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    [Authorize(Roles = "SystemAdmin")]
    public class UsersController : Controller
    {
        private readonly AdminManagementService _adminManagementService;
        private readonly PlatformOwnerService _platformOwnerService;

        public UsersController(
            AdminManagementService adminManagementService,
            PlatformOwnerService platformOwnerService)
        {
            _adminManagementService = adminManagementService;
            _platformOwnerService = platformOwnerService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = await _adminManagementService.GetUsersAsync();
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var model = await _adminManagementService.BuildUserFormAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AppUserFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var refreshed = await _adminManagementService.BuildUserFormAsync();
                MergeSelection(model, refreshed);
                return View(refreshed);
            }

            try
            {
                await _adminManagementService.CreateUserAsync(model, _platformOwnerService.IsOwner(User));
                TempData["SuccessMessage"] = "User created successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                var refreshed = await _adminManagementService.BuildUserFormAsync();
                MergeSelection(model, refreshed);
                return View(refreshed);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var model = await _adminManagementService.BuildUserFormAsync(id);
                return View(model);
            }
            catch (InvalidOperationException)
            {
                return NotFound();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(AppUserFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var refreshedModel = await _adminManagementService.BuildUserFormAsync(model.Id);
                MergeSelection(model, refreshedModel);
                return View(refreshedModel);
            }

            try
            {
                await _adminManagementService.UpdateUserAsync(model, _platformOwnerService.IsOwner(User));
                TempData["SuccessMessage"] = "User updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                var refreshedModel = await _adminManagementService.BuildUserFormAsync(model.Id);
                MergeSelection(model, refreshedModel);
                return View(refreshedModel);
            }
        }

        private static void MergeSelection(AppUserFormViewModel source, AppUserFormViewModel target)
        {
            target.Username = source.Username;
            target.DisplayName = source.DisplayName;
            target.IsActive = source.IsActive;
            target.IsSystemAdmin = source.IsSystemAdmin;
            target.DefaultCompanyId = source.DefaultCompanyId;

            foreach (var company in target.Companies)
            {
                company.IsSelected = source.Companies.Any(x => x.CompanyId == company.CompanyId && x.IsSelected);
            }

            foreach (var group in target.PermissionGroups)
            {
                foreach (var permission in group.Permissions)
                {
                    permission.IsSelected = source.PermissionGroups
                        .SelectMany(x => x.Permissions)
                        .Any(x => x.PermissionKey == permission.PermissionKey && x.IsSelected);
                }
            }
        }
    }
}
