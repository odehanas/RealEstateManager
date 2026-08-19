using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Tenant portal sign-in and password management. Lives under /portal and uses the
    /// TenantPortal cookie scheme, never the staff one.
    /// </summary>
    [Route("portal")]
    public class PortalAccountController : Controller
    {
        private readonly TenantPortalAuthService _authService;

        public PortalAccountController(TenantPortalAuthService authService)
        {
            _authService = authService;
        }

        [HttpGet("login")]
        [AllowAnonymous]
        public IActionResult Login()
        {
            // Already signed in as a tenant - go straight through.
            if (User.IsTenantPortalUser())
                return RedirectToAction("Index", "Portal");

            return View(new PortalLoginViewModel());
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(PortalLoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var result = await _authService.SignInAsync(model.Username, model.Password);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Sign in failed.");
                return View(model);
            }

            if (result.MustChangePassword)
                return RedirectToAction(nameof(ChangePassword));

            return RedirectToAction("Index", "Portal");
        }

        [HttpPost("logout")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _authService.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        /// <summary>
        /// Shown when the landlord's subscription has lapsed. The tenant cannot fix it,
        /// so this only explains and points them at their property manager.
        /// </summary>
        [HttpGet("unavailable")]
        public IActionResult Unavailable()
        {
            return View();
        }

        [HttpGet("password")]
        public IActionResult ChangePassword()
        {
            if (!User.IsTenantPortalUser())
                return RedirectToAction(nameof(Login));

            return View(new PortalChangePasswordViewModel { IsForced = true });
        }

        [HttpPost("password")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(PortalChangePasswordViewModel model)
        {
            if (!User.IsTenantPortalUser())
                return RedirectToAction(nameof(Login));

            if (!ModelState.IsValid)
                return View(model);

            var portalUserId = User.GetPortalUserId();
            if (!portalUserId.HasValue)
                return RedirectToAction(nameof(Login));

            var error = await _authService.ChangePasswordAsync(
                portalUserId.Value, model.CurrentPassword, model.NewPassword);

            if (error != null)
            {
                ModelState.AddModelError(string.Empty, error);
                return View(model);
            }

            TempData["PortalMessage"] = "Your password has been changed.";
            return RedirectToAction("Index", "Portal");
        }
    }
}
