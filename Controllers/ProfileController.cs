using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly ProfileService _profileService;
        private readonly AppAuthService _appAuthService;
        private readonly ApplicationDbContext _db;

        public ProfileController(ProfileService profileService, AppAuthService appAuthService, ApplicationDbContext db)
        {
            _profileService = profileService;
            _appAuthService = appAuthService;
            _db = db;
        }

        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View(new ChangePasswordViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                var userId = User.GetAppUserId();
                if (!userId.HasValue)
                    return RedirectToAction("Login", "Auth");

                await _profileService.ChangePasswordAsync(
                    userId.Value,
                    model.CurrentPassword,
                    model.NewPassword);

                // Sign in again so the "must change password" restriction drops off the cookie.
                var wasForced = User.HasClaim(x => x.Type == AppClaimTypes.MustChangePassword);
                var user = await _db.AppUsers.IgnoreQueryFilters().FirstAsync(x => x.Id == userId.Value);
                var companyId = User.GetCurrentCompanyId() ?? await _appAuthService.GetDefaultCompanyIdAsync(user.Id);
                if (companyId.HasValue)
                    await _appAuthService.SignInAsync(HttpContext, user, companyId.Value);

                TempData["SuccessMessage"] = "Password changed successfully.";
                return wasForced ? RedirectToAction("Index", "Home") : RedirectToAction(nameof(ChangePassword));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }
    }
}
