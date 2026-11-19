using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using RealEstateManager.Services;
using RealEstateManager.Services.Localization;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The public vacancy website of one company, at /vacancies/{slug}. Nobody signs in here.
    /// Every action first opens the page of the address it was called on, which confines the
    /// request to that company; an address that is unknown or switched off is simply not found.
    /// Only reading is offered, plus one form: the enquiry, which is limited per visitor.
    /// </summary>
    [AllowAnonymous]
    [Route("vacancies/{slug}")]
    public class VacanciesController : Controller
    {
        private const int EnquiriesPerWindow = 5;
        private static readonly TimeSpan EnquiryWindow = TimeSpan.FromMinutes(10);

        private readonly ListingService _listings;
        private readonly IMemoryCache _cache;
        private readonly UiText _text;

        public VacanciesController(ListingService listings, IMemoryCache cache, UiText text)
        {
            _listings = listings;
            _cache = cache;
            _text = text;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(string slug, [FromQuery] ListingService.PublicFilter filter)
        {
            var site = await _listings.OpenPublicSiteAsync(slug, UiLanguages.IsArabic);
            if (site == null)
                return NotFound();

            var all = await _listings.GetPublicUnitsAsync(site);
            ViewBag.Site = site;
            ViewBag.Filter = filter;
            ViewBag.All = all;
            return View(await _listings.GetPublicUnitsAsync(site, filter));
        }

        [HttpGet("unit/{id:int}")]
        public async Task<IActionResult> Unit(string slug, int id)
        {
            var site = await _listings.OpenPublicSiteAsync(slug, UiLanguages.IsArabic);
            if (site == null)
                return NotFound();

            var unit = await _listings.GetPublicUnitAsync(site, id);
            if (unit == null)
            {
                // Let since the visitor found it: back to what is available now.
                TempData["VacancyNote"] = "That unit is no longer available. Here is what is available now.";
                return RedirectToAction(nameof(Index), new { slug = site.Slug });
            }

            ViewBag.Site = site;
            return View(unit);
        }

        /// <summary>A photo of an advertised unit. Photos never change once uploaded, so browsers may keep them.</summary>
        [HttpGet("photo/{id:int}")]
        public async Task<IActionResult> Photo(string slug, int id)
        {
            var site = await _listings.OpenPublicSiteAsync(slug, false);
            if (site == null)
                return NotFound();

            var image = await _listings.GetPhotoImageAsync(id, publicOnly: true);
            if (image == null)
                return NotFound();

            Response.Headers["Cache-Control"] = "public, max-age=86400";
            return File(image.Value.Content, image.Value.ContentType);
        }

        [HttpPost("enquire")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Enquire(string slug, ListingService.EnquiryInput input, string? website)
        {
            var site = await _listings.OpenPublicSiteAsync(slug, UiLanguages.IsArabic);
            if (site == null)
                return NotFound();

            IActionResult Back() => input.UnitId.HasValue
                ? RedirectToAction(nameof(Unit), new { slug = site.Slug, id = input.UnitId.Value })
                : RedirectToAction(nameof(Index), new { slug = site.Slug });

            // A field people never see and never fill; a program filling every field gives itself away.
            if (!string.IsNullOrEmpty(website))
            {
                TempData["VacancySent"] = true;
                return Back();
            }

            if (!AllowEnquiry())
            {
                TempData["VacancyError"] = _text.Message("Too many enquiries were sent from this device. Please try again in a few minutes, or call us.");
                return Back();
            }

            try
            {
                await _listings.AddEnquiryAsync(site, input, UiLanguages.Current);
                TempData["VacancySent"] = true;
            }
            catch (InvalidOperationException ex)
            {
                TempData["VacancyError"] = _text.Message(ex.Message);
            }
            return Back();
        }

        /// <summary>At most a handful of enquiries from one address in ten minutes.</summary>
        private bool AllowEnquiry()
        {
            var key = "vacancy-enquiry:" + (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            var count = _cache.GetOrCreate(key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = EnquiryWindow;
                return new Counter();
            })!;
            return Interlocked.Increment(ref count.Value) <= EnquiriesPerWindow;
        }

        private sealed class Counter
        {
            public int Value;
        }
    }
}
