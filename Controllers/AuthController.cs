using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class AuthController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly AppAuthService _appAuthService;
        private readonly AdminManagementService _adminManagementService;

        public AuthController(
            ApplicationDbContext db,
            AppAuthService appAuthService,
            AdminManagementService adminManagementService)
        {
            _db = db;
            _appAuthService = appAuthService;
            _adminManagementService = adminManagementService;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            return View(new LoginViewModel
            {
                ReturnUrl = returnUrl
            });
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult RegisterCompany()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            return View(new RegisterCompanyViewModel());
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterCompany(RegisterCompanyViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var userId = await _adminManagementService.RegisterCompanyAsync(model);
                var user = await _db.AppUsers.FirstOrDefaultAsync(x => x.Id == userId && x.IsActive);
                if (user == null)
                {
                    TempData["SuccessMessage"] = "Company created successfully. Please log in with your new admin account.";
                    return RedirectToAction(nameof(Login));
                }

                var companyId = await _appAuthService.GetDefaultCompanyIdAsync(user.Id);
                if (!companyId.HasValue)
                {
                    TempData["SuccessMessage"] = "Company created successfully. Please log in with your new admin account.";
                    return RedirectToAction(nameof(Login));
                }

                await _appAuthService.SignInAsync(HttpContext, user, companyId.Value);
                TempData["SuccessMessage"] = "Company created successfully. Your 15-day trial has started.";
                return RedirectToAction("Index", "Home");
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _appAuthService.ValidateCredentialsAsync(model.Username, model.Password);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View(model);
            }

            var companyId = await _appAuthService.GetDefaultCompanyIdAsync(user.Id);
            if (!companyId.HasValue)
            {
                ModelState.AddModelError(string.Empty, "This user has no company access assigned.");
                return View(model);
            }

            await _appAuthService.SignInAsync(HttpContext, user, companyId.Value);

            if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SwitchCompany(int companyId, string? returnUrl = null)
        {
            var userId = User.GetAppUserId();
            if (!userId.HasValue)
            {
                return RedirectToAction(nameof(Login));
            }

            if (!await _appAuthService.UserHasAccessToCompanyAsync(userId.Value, companyId))
            {
                return Forbid();
            }

            var dbUser = await _db
                .AppUsers
                .FirstOrDefaultAsync(x => x.Id == userId.Value && x.IsActive);

            if (dbUser == null)
            {
                await _appAuthService.SignOutAsync(HttpContext);
                return RedirectToAction(nameof(Login));
            }

            await _appAuthService.SignInAsync(HttpContext, dbUser, companyId);

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _appAuthService.SignOutAsync(HttpContext);
            return RedirectToAction(nameof(Login));
        }
    }
}
