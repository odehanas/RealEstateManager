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
        private readonly TwoStepSignInService _twoStep;

        public ProfileController(ProfileService profileService, AppAuthService appAuthService, ApplicationDbContext db, TwoStepSignInService twoStep)
        {
            _twoStep = twoStep;
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

        // ---------------------------------------------------------------------
        //  Two-step sign-in: the user's own page
        // ---------------------------------------------------------------------

        private const string RecoveryCodesKey = "TwoStepRecoveryCodes";

        private Task<Domain.AppUser?> CurrentUserAsync()
        {
            var userId = User.GetAppUserId();
            return _db.AppUsers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == userId && x.IsActive);
        }

        /// <summary>Signs in again so the cookie says what is true now (set up, or not).</summary>
        private async Task RefreshSignInAsync(Domain.AppUser user)
        {
            var companyId = User.GetCurrentCompanyId() ?? await _appAuthService.GetDefaultCompanyIdAsync(user.Id);
            if (companyId.HasValue)
                await _appAuthService.SignInAsync(HttpContext, user, companyId.Value);
        }

        [HttpGet]
        public async Task<IActionResult> TwoStep()
        {
            var user = await CurrentUserAsync();
            if (user == null)
                return RedirectToAction("Login", "Auth");

            var model = new TwoStepPageViewModel
            {
                Enabled = user.TwoFactorEnabled,
                EnabledAt = user.TwoFactorEnabledAt,
                RecoveryCodesLeft = TwoStepSignInService.RecoveryCodesLeft(user),
                Required = await _twoStep.IsRequiredOfAsync(user.Id),
                NewRecoveryCodes = (TempData[RecoveryCodesKey] as string ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList()
            };

            if (!user.TwoFactorEnabled)
            {
                var setup = await _twoStep.BeginSetupAsync(user);
                model.Key = setup.Key;
                model.QrDataUri = setup.QrDataUri;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnableTwoStep(string? code)
        {
            var user = await CurrentUserAsync();
            if (user == null)
                return RedirectToAction("Login", "Auth");

            var wasForced = User.HasClaim(x => x.Type == AppClaimTypes.MustSetUpTwoStep);
            var (codes, error) = await _twoStep.ConfirmSetupAsync(user, code);
            if (codes == null)
            {
                TempData["ErrorMessage"] = error;
                return RedirectToAction(nameof(TwoStep));
            }

            await RefreshSignInAsync(user);
            TempData[RecoveryCodesKey] = string.Join(' ', codes);
            TempData["SuccessMessage"] = wasForced
                ? "Two-step sign-in is on. Keep the recovery codes below, then carry on from the menu."
                : "Two-step sign-in is on. From your next sign-in you will be asked for a code from your phone.";
            return RedirectToAction(nameof(TwoStep));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NewRecoveryCodes(string? code)
        {
            var user = await CurrentUserAsync();
            if (user == null)
                return RedirectToAction("Login", "Auth");

            if (!await _twoStep.CheckAsync(user, code))
            {
                TempData["ErrorMessage"] = "That code is not right. Type the six digits your authenticator app shows now.";
                return RedirectToAction(nameof(TwoStep));
            }

            TempData[RecoveryCodesKey] = string.Join(' ', await _twoStep.NewRecoveryCodesAsync(user));
            TempData["SuccessMessage"] = "New recovery codes made. The old ones no longer work.";
            return RedirectToAction(nameof(TwoStep));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DisableTwoStep(string? code)
        {
            var user = await CurrentUserAsync();
            if (user == null)
                return RedirectToAction("Login", "Auth");

            if (!await _twoStep.CheckAsync(user, code))
            {
                TempData["ErrorMessage"] = "That code is not right. Type the six digits your authenticator app shows now, or a recovery code.";
                return RedirectToAction(nameof(TwoStep));
            }

            TwoStepSignInService.Clear(user);
            await _db.SaveChangesAsync();
            await RefreshSignInAsync(user);

            TempData["SuccessMessage"] = "Two-step sign-in is off.";
            return RedirectToAction(nameof(TwoStep));
        }
    }
}
