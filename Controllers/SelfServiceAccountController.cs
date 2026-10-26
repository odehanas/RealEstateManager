using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>Employee portal sign-in and password, under /me with its own cookie.</summary>
    [Route("me")]
    public class SelfServiceAccountController : Controller
    {
        private readonly EmployeePortalService _portal;

        public SelfServiceAccountController(EmployeePortalService portal)
        {
            _portal = portal;
        }

        [HttpGet("login")]
        [AllowAnonymous]
        public IActionResult Login()
        {
            if (User.IsEmployeePortalUser())
                return RedirectToAction("Index", "SelfService");
            return View();
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string? username, string? password)
        {
            var (success, error, mustChange) = await _portal.SignInAsync(username, password);
            if (!success)
            {
                ViewBag.Error = error;
                ViewBag.Username = username;
                return View();
            }

            return mustChange ? RedirectToAction(nameof(ChangePassword)) : RedirectToAction("Index", "SelfService");
        }

        [HttpPost("logout")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _portal.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        [HttpGet("password")]
        public IActionResult ChangePassword()
        {
            if (!User.IsEmployeePortalUser())
                return RedirectToAction(nameof(Login));
            return View();
        }

        [HttpPost("password")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            var id = User.GetEmployeePortalUserId();
            if (!id.HasValue)
                return RedirectToAction(nameof(Login));

            string? error = newPassword != confirmPassword
                ? "The new passwords do not match."
                : await _portal.ChangePasswordAsync(id.Value, currentPassword ?? "", newPassword ?? "");
            if (error != null)
            {
                ViewBag.Error = error;
                return View();
            }

            TempData["SelfServiceMessage"] = "Your password has been changed.";
            return RedirectToAction("Index", "SelfService");
        }

        [HttpGet("unavailable")]
        public IActionResult Unavailable() => View();
    }
}
