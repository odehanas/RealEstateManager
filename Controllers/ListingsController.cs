using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The office's side of the vacancy website: switch the company's public page on, choose
    /// which vacant units are advertised, give each its photos and description, and answer
    /// the enquiries visitors leave. The public side is <see cref="VacanciesController"/>.
    /// </summary>
    public class ListingsController : Controller
    {
        private readonly ListingService _listings;

        public ListingsController(ListingService listings)
        {
            _listings = listings;
        }

        private string? UserName =>
            User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value;

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var site = await _listings.GetOrCreateSiteAsync();
            ViewBag.Site = site;
            ViewBag.PublicUrl = PublicUrl(site.Slug);
            ViewBag.NewEnquiries = await _listings.CountNewEnquiriesAsync();
            return View(await _listings.GetBoardAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSite(ListingService.SiteInput input)
        {
            try
            {
                await _listings.SaveSiteAsync(input);
                TempData["SuccessMessage"] = input.IsEnabled
                    ? "The vacancy website is saved and open to the public."
                    : "The vacancy website is saved. It is switched off: nobody outside the office can see it.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            return RedirectToAction(nameof(Index));
        }

        /// <summary>One unit's advertisement: details, description and photos.</summary>
        [HttpGet]
        public async Task<IActionResult> Unit(int id)
        {
            var unit = await _listings.GetUnitAsync(id);
            if (unit == null)
                return NotFound();

            var site = await _listings.GetOrCreateSiteAsync();
            ViewBag.Site = site;
            ViewBag.PublicUrl = PublicUrl(site.Slug);
            ViewBag.Photos = await _listings.GetPhotosAsync(id);
            return View(unit);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveUnit(int id, ListingService.UnitInput input)
        {
            try
            {
                await _listings.SaveUnitAsync(id, input);
                TempData["SuccessMessage"] = "The unit's advertisement is saved.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            return RedirectToAction(nameof(Unit), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int id, bool listed)
        {
            try
            {
                var unit = await _listings.SetListedAsync(id, listed);
                TempData["SuccessMessage"] = listed
                    ? $"Unit {unit.UnitNumber} is on the website."
                    : $"Unit {unit.UnitNumber} is off the website.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(UnitPhoto.MaxPerUnit * UnitPhoto.MaxFileBytes + 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = UnitPhoto.MaxPerUnit * UnitPhoto.MaxFileBytes + 1024 * 1024)]
        public async Task<IActionResult> UploadPhotos(int id, List<IFormFile>? photos)
        {
            var (added, problems) = await _listings.AddPhotosAsync(id, photos, UserName);
            if (added > 0)
                TempData["SuccessMessage"] = $"{added} photo(s) added.";
            if (problems.Count > 0)
                TempData["ErrorMessage"] = string.Join(" ", problems);
            return RedirectToAction(nameof(Unit), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePhoto(int id)
        {
            try
            {
                var unitId = await _listings.DeletePhotoAsync(id);
                TempData["SuccessMessage"] = "Photo deleted.";
                return RedirectToAction(nameof(Unit), new { id = unitId });
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MakeCover(int id)
        {
            try
            {
                var unitId = await _listings.MakeCoverAsync(id);
                TempData["SuccessMessage"] = "That photo is now the cover.";
                return RedirectToAction(nameof(Unit), new { id = unitId });
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>A unit photo, for the office's screens.</summary>
        [HttpGet]
        public async Task<IActionResult> Photo(int id)
        {
            var image = await _listings.GetPhotoImageAsync(id, publicOnly: false);
            return image == null ? NotFound() : File(image.Value.Content, image.Value.ContentType);
        }

        [HttpGet]
        public async Task<IActionResult> Enquiries(string? status)
        {
            ViewBag.Status = string.IsNullOrWhiteSpace(status) ? null : EnquiryStatuses.Clean(status);
            return View(await _listings.GetEnquiriesAsync(status));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateEnquiry(int id, string? status, string? notes, string? returnStatus)
        {
            try
            {
                await _listings.UpdateEnquiryAsync(id, status, notes, UserName);
                TempData["SuccessMessage"] = "Enquiry updated.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            return RedirectToAction(nameof(Enquiries), new { status = returnStatus });
        }

        private string PublicUrl(string slug) => $"{Request.Scheme}://{Request.Host}{Request.PathBase}/vacancies/{slug}";
    }
}
