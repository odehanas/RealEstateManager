using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Services.Localization;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// What lets a phone install the application from the browser, without an app store:
    /// a manifest (the name, icon and colours of the home-screen app) and a service worker
    /// (which shows the notifications and an offline page). There are three apps on the one
    /// site - staff at /, tenants at /portal/ and employees at /me/ - and each gets its own
    /// manifest and worker under its own path, so installing one never opens another.
    ///
    /// Anonymous: the browser fetches these without a sign-in, and they carry nothing private.
    /// </summary>
    [AllowAnonymous]
    public class AppInstallController : Controller
    {
        private readonly IWebHostEnvironment _environment;
        private readonly UiText _text;

        public AppInstallController(IWebHostEnvironment environment, UiText text)
        {
            _environment = environment;
            _text = text;
        }

        [HttpGet("/manifest.webmanifest")]
        public IActionResult StaffManifest() =>
            Manifest("/", "RealEstateManager", "RealEstateManager", "#212529");

        [HttpGet("/portal/manifest.webmanifest")]
        public IActionResult TenantManifest() =>
            Manifest("/portal/", _text["Tenant Portal"], _text["Tenant Portal"], "#0f2740");

        [HttpGet("/me/manifest.webmanifest")]
        public IActionResult EmployeeManifest() =>
            Manifest("/me/", _text["My HR"], _text["My HR"], "#0f2740");

        // The same worker for all three; its scope is the path it is served from.
        [HttpGet("/sw.js")]
        [HttpGet("/portal/sw.js")]
        [HttpGet("/me/sw.js")]
        public IActionResult ServiceWorker()
        {
            // Checked on every visit, so a new version reaches phones with the next publish.
            Response.Headers.CacheControl = "no-cache";
            return PhysicalFile(Path.Combine(_environment.WebRootPath, "js", "app-sw.js"), "text/javascript; charset=utf-8");
        }

        private IActionResult Manifest(string scope, string name, string shortName, string themeColour)
        {
            var arabic = UiLanguages.IsArabic;
            Response.Headers.CacheControl = "no-cache";
            return new JsonResult(new Dictionary<string, object>
            {
                ["id"] = scope,
                ["name"] = name,
                ["short_name"] = shortName,
                ["start_url"] = scope,
                ["scope"] = scope,
                ["display"] = "standalone",
                ["orientation"] = "any",
                ["background_color"] = "#f2f4f7",
                ["theme_color"] = themeColour,
                ["lang"] = arabic ? "ar" : "en",
                ["dir"] = arabic ? "rtl" : "ltr",
                ["icons"] = new object[]
                {
                    new { src = "/img/app/icon-192.png", sizes = "192x192", type = "image/png", purpose = "any" },
                    new { src = "/img/app/icon-512.png", sizes = "512x512", type = "image/png", purpose = "any" },
                    new { src = "/img/app/icon-maskable-512.png", sizes = "512x512", type = "image/png", purpose = "maskable" }
                }
            })
            { ContentType = "application/manifest+json; charset=utf-8" };
        }
    }
}
