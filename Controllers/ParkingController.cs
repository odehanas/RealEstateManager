using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Car parking: the board of each property's bays, adding bays, and giving a bay to a
    /// contract or taking it back. Who may look and who may change is in PermissionCatalog.
    /// </summary>
    [Authorize]
    public class ParkingController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ParkingService _parking;

        public ParkingController(ApplicationDbContext db, ParkingService parking)
        {
            _db = db;
            _parking = parking;
        }

        private int CompanyId => User.GetRequiredCurrentCompanyId();

        /// <summary>The board. With <paramref name="assign"/>, the form to give that bay to a contract.</summary>
        [HttpGet]
        public async Task<IActionResult> Index(int? propertyId, int? assign)
        {
            var companyId = CompanyId;
            ViewBag.Properties = await _db.Properties.AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.Code)
                .Select(x => new { x.Id, Name = x.Code + " - " + x.Name })
                .ToListAsync();
            ViewBag.PropertyId = propertyId;

            var board = await _parking.BoardAsync(companyId, propertyId);
            if (assign.HasValue)
            {
                var property = board.FirstOrDefault(b => b.Bays.Any(x => x.Id == assign.Value));
                var bay = property?.Bays.FirstOrDefault(x => x.Id == assign.Value);
                if (bay != null && bay.IsActive && !bay.IsOccupied)
                {
                    ViewBag.AssignBay = bay;
                    ViewBag.AssignProperty = property!.Property;
                    ViewBag.Leases = await _parking.LeasesAsync(companyId, property.PropertyId);
                }
            }

            return View(board);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddBays(int propertyId, string? prefix, int? from, int? to, string? type) =>
            Done(await _parking.AddBaysAsync(CompanyId, propertyId, prefix, from, to, type), propertyId);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleBay(int id, int? propertyId) =>
            Done(await _parking.ToggleBayAsync(CompanyId, id), propertyId);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Assign(ParkingService.AssignInput input, int? propertyId) =>
            Done(await _parking.AssignAsync(CompanyId, input, User.Identity?.Name), propertyId);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Release(int id, DateTime? on, int? propertyId) =>
            Done(await _parking.ReleaseAsync(CompanyId, id, on), propertyId);

        private IActionResult Done((bool Ok, string Message) result, int? propertyId)
        {
            TempData[result.Ok ? "SuccessMessage" : "ErrorMessage"] = result.Message;
            return RedirectToAction(nameof(Index), new { propertyId });
        }
    }
}
