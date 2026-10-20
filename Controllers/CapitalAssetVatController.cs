using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>Operations ▸ Capital Assets Scheme (VAT): the ten-year review of input tax recovered on buildings.</summary>
    public class CapitalAssetVatController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly CapitalAssetVatService _cas;

        public CapitalAssetVatController(ApplicationDbContext db, CapitalAssetVatService cas)
        {
            _db = db;
            _cas = cas;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewBag.Candidates = await _cas.GetProjectCandidatesAsync();
            ViewBag.Properties = await _db.Properties.AsNoTracking()
                .Where(x => x.OwnershipType == "Owned")
                .OrderBy(x => x.Code)
                .Select(x => new SelectListItem(x.Code + " - " + x.Name, x.Id.ToString()))
                .ToListAsync();
            return View(await _cas.GetRecordsAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterProject(int projectId)
        {
            try
            {
                var record = await _cas.RegisterProjectAsync(projectId);
                TempData["SuccessMessage"] = $"{record.Name} registered under the Capital Assets Scheme.";
                return RedirectToAction(nameof(Details), new { id = record.Id });
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterManual(
            string name, int propertyId, decimal value, decimal inputVat, decimal recoveredVat, DateTime startDate, string? notes)
        {
            try
            {
                var record = await _cas.RegisterManualAsync(name, propertyId, value, inputVat, recoveredVat, startDate, notes);
                TempData["SuccessMessage"] = $"{record.Name} registered under the Capital Assets Scheme.";
                return RedirectToAction(nameof(Details), new { id = record.Id });
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var record = await _cas.GetAsync(id);
            if (record == null)
                return NotFound();

            // The proposal for the first year still to be adjusted, once that year is over.
            var nextYear = Enumerable.Range(1, 10).FirstOrDefault(y => record.Adjustments.All(a => a.YearNumber != y));
            if (nextYear > 0 && CapitalAssetVatService.YearPeriod(record, nextYear).End <= DateTime.Today)
            {
                var (percent, explanation) = await _cas.ProposeTaxableUseAsync(id, nextYear);
                ViewBag.NextYear = nextYear;
                ViewBag.ProposedPercent = percent;
                ViewBag.Explanation = explanation;
            }
            else
            {
                ViewBag.NextYear = nextYear;
            }

            return View(record);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Adjust(int id, int year, decimal taxableUsePercent, DateTime adjustmentDate, string? notes)
        {
            try
            {
                var adjustment = await _cas.PostAdjustmentAsync(id, year, taxableUsePercent, adjustmentDate, notes);
                TempData["SuccessMessage"] = adjustment.Adjustment == 0m
                    ? $"Year {year} reviewed: no change in use, no adjustment."
                    : $"Year {year} adjustment of {adjustment.Adjustment:N2} posted ({(adjustment.Adjustment > 0 ? "more input tax recoverable" : "input tax repaid")}).";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
