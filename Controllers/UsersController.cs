using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Users &amp; Permissions. A System Admin manages every user; a company administrator
    /// manages the users of the companies they administer - see UserAdminScope.
    /// </summary>
    [Authorize(Policy = UserManagerRequirement.PolicyName)]
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

        private Task<UserAdminScope> ScopeAsync() => _adminManagementService.GetUserAdminScopeAsync(User);

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var scope = await ScopeAsync();
            ViewBag.Scope = scope;
            var model = await _adminManagementService.GetUsersAsync(scope);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var scope = await ScopeAsync();
            ViewBag.Scope = scope;
            var model = await _adminManagementService.BuildUserFormAsync(scope);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AppUserFormViewModel model)
        {
            var scope = await ScopeAsync();
            ViewBag.Scope = scope;

            if (!ModelState.IsValid)
            {
                var refreshed = await _adminManagementService.BuildUserFormAsync(scope);
                MergeSelection(model, refreshed);
                return View(refreshed);
            }

            try
            {
                await _adminManagementService.CreateUserAsync(model, _platformOwnerService.IsOwner(User), scope);
                TempData["SuccessMessage"] = "User created successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                var refreshed = await _adminManagementService.BuildUserFormAsync(scope);
                MergeSelection(model, refreshed);
                return View(refreshed);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var scope = await ScopeAsync();
            ViewBag.Scope = scope;

            try
            {
                var model = await _adminManagementService.BuildUserFormAsync(scope, id);
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
            var scope = await ScopeAsync();
            ViewBag.Scope = scope;

            if (!ModelState.IsValid)
            {
                var refreshedModel = await _adminManagementService.BuildUserFormAsync(scope, model.Id);
                MergeSelection(model, refreshedModel);
                return View(refreshedModel);
            }

            try
            {
                await _adminManagementService.UpdateUserAsync(model, _platformOwnerService.IsOwner(User), scope);
                TempData["SuccessMessage"] = "User updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                try
                {
                    var refreshedModel = await _adminManagementService.BuildUserFormAsync(scope, model.Id);
                    MergeSelection(model, refreshedModel);
                    return View(refreshedModel);
                }
                catch (InvalidOperationException)
                {
                    return NotFound();
                }
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
                var posted = source.Companies.FirstOrDefault(x => x.CompanyId == company.CompanyId);
                company.IsSelected = posted?.IsSelected == true;
                company.IsCompanyAdmin = posted?.IsCompanyAdmin == true;
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
