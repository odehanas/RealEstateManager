using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class RevenueRecognitionController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _postingService;

        public RevenueRecognitionController(ApplicationDbContext db, AccountingPostingService postingService)
        {
            _db = db;
            _postingService = postingService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? asOfDate, bool showPosted = true)
        {
            var cutoffDate = (asOfDate ?? DateTime.Today).Date;

            var query = _db.RevenueRecognitionSchedules
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                .AsQueryable();

            if (!showPosted)
            {
                query = query.Where(x => !x.JournalEntryId.HasValue);
            }

            var schedules = await query
                .Where(x => x.RecognitionDate <= cutoffDate)
                .OrderBy(x => x.RecognitionDate)
                .ThenBy(x => x.Id)
                .ToListAsync();

            ViewBag.AsOfDate = cutoffDate;
            ViewBag.ShowPosted = showPosted;

            return View(schedules);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Post(int id, DateTime? asOfDate, bool showPosted = true)
        {
            await _postingService.PostRevenueRecognitionAsync(id);
            return RedirectToAction(nameof(Index), new { asOfDate, showPosted });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PostDue(DateTime asOfDate, bool showPosted = true)
        {
            await _postingService.ProcessRevenueRecognitionUpToAsync(asOfDate.Date);
            return RedirectToAction(nameof(Index), new { asOfDate, showPosted });
        }
    }
}