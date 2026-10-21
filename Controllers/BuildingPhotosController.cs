using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Building photos for the building view: upload them, and mark each unit's outline on
    /// them. Changes the property record, so it needs Create &amp; Edit Master Data.
    /// </summary>
    public class BuildingPhotosController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly BuildingPhotoService _photos;

        public BuildingPhotosController(ApplicationDbContext db, BuildingPhotoService photos)
        {
            _db = db;
            _photos = photos;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int id)
        {
            var property = await _db.Properties.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (property == null)
                return NotFound();

            ViewBag.Property = property;
            ViewBag.UnitCount = await _db.Units.CountAsync(x => x.PropertyId == id && x.IsActive);
            return View(await _photos.GetPhotosAsync(id));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(RealEstateManager.Domain.PropertyPhoto.MaxFileBytes + 1024 * 1024)]
        public async Task<IActionResult> Upload(int id, IFormFile? file, string? title)
        {
            try
            {
                var userName = User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value;
                var photo = await _photos.UploadAsync(id, file, title, userName);
                TempData["SuccessMessage"] = $"Photo \"{photo.Title}\" uploaded. Now mark the units on it.";
                return RedirectToAction(nameof(Mark), new { id = photo.Id });
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Index), new { id });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var propertyId = await _photos.DeletePhotoAsync(id);
                TempData["SuccessMessage"] = "Photo and its unit outlines deleted.";
                return RedirectToAction(nameof(Index), new { id = propertyId });
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Index", "BuildingView");
            }
        }

        /// <summary>The marking screen: the photo, the building's units, and the outlines drawn so far.</summary>
        [HttpGet]
        public async Task<IActionResult> Mark(int id)
        {
            var photo = await _photos.GetPhotoInfoAsync(id);
            if (photo == null)
                return NotFound();

            var units = await _db.Units.AsNoTracking()
                .Where(x => x.PropertyId == photo.PropertyId && x.IsActive)
                .ToListAsync();
            ViewBag.Units = units
                .Select(u => new { u.Id, u.UnitNumber, Floor = BuildingViewService.ResolveFloor(u), u.FloorPosition, u.FloorSide, u.UsageType })
                .OrderByDescending(u => u.Floor).ThenBy(u => u.FloorPosition ?? int.MaxValue).ThenBy(u => u.UnitNumber)
                .Select(u => new MarkUnit(u.Id, u.UnitNumber, u.Floor, u.FloorSide, u.UsageType))
                .ToList();
            return View(photo);
        }

        public sealed record MarkUnit(int Id, string UnitNumber, int Floor, string? Side, string? Usage);

        /// <summary>The image, for the marking screen.</summary>
        [HttpGet]
        public async Task<IActionResult> Image(int id)
        {
            var image = await _photos.GetImageAsync(id);
            return image == null ? NotFound() : File(image.Value.Content, image.Value.ContentType);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveArea(int photoId, int unitId, string points)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<double[][]>(points ?? "[]") ?? Array.Empty<double[]>();
                await _photos.SaveAreaAsync(photoId, unitId, parsed);
                return Json(new { ok = true });
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException)
            {
                return Json(new { ok = false, message = ex is JsonException ? "The outline could not be read." : ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteArea(int photoId, int unitId)
        {
            await _photos.DeleteAreaAsync(photoId, unitId);
            return Json(new { ok = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Repeat(int id, int lowerFloor, int upperFloor, int lastFloor, bool overwrite)
        {
            try
            {
                var added = await _photos.RepeatFloorsAsync(id, lowerFloor, upperFloor, lastFloor, overwrite);
                TempData["SuccessMessage"] = added == 0
                    ? "No outlines added: the floors may already be marked, or the repeated outlines would fall off the photo."
                    : $"{added} unit outline(s) added from floor {upperFloor} to floor {lastFloor}. Check them, and adjust any that do not sit right.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            return RedirectToAction(nameof(Mark), new { id });
        }
    }
}
