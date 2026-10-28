using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Services;
using RealEstateManager.Services.Localization;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The English / Arabic switch. Open to everyone - staff, tenants, employees and the
    /// sign-in pages. A signed-in staff user's choice is also kept on their profile, so it
    /// follows them to any device.
    /// </summary>
    [AllowAnonymous]
    public class LanguageController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UiText _text;

        public LanguageController(ApplicationDbContext db, UiText text)
        {
            _db = db;
            _text = text;
        }

        [HttpGet]
        public async Task<IActionResult> Set(string lang, string? returnUrl)
        {
            var language = UiLanguages.Clean(lang);
            UiLanguageMiddleware.Remember(Response, language);

            var userId = User.GetAppUserId();
            if (userId.HasValue)
            {
                var user = await _db.AppUsers.FirstOrDefaultAsync(x => x.Id == userId.Value);
                if (user != null && user.PreferredLanguage != language)
                {
                    user.PreferredLanguage = language;
                    await _db.SaveChangesAsync();
                }
            }

            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        }

        /// <summary>Phrases seen in Arabic since the last restart that have no translation yet (system admins).</summary>
        [HttpGet]
        [Authorize(Roles = "SystemAdmin")]
        public IActionResult Missing() => Json(new { translated = _text.Count, missing = _text.Missing });
    }
}
