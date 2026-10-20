using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class ManualJournalsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ManualJournalService _manualJournalService;

        public ManualJournalsController(ApplicationDbContext db, ManualJournalService manualJournalService)
        {
            _db = db;
            _manualJournalService = manualJournalService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var entries = await _db.JournalEntries
                .Include(x => x.Lines)
                // Needed so the list can tell whether the period is closed and show the
                // right workflow button.
                .Include(x => x.AccountingPeriod)
                .Where(x => x.JournalType == "Manual")
                .OrderByDescending(x => x.EntryDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return View(entries);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await LoadLookupsAsync();

            var model = new ManualJournalEntryViewModel();

            for (int i = 0; i < 8; i++)
            {
                model.Lines.Add(new ManualJournalLineViewModel());
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ManualJournalEntryViewModel model)
        {
            await LoadLookupsAsync();

            try
            {
                var journalEntryId = await _manualJournalService.CreateManualJournalAsync(model);
                return RedirectToAction(nameof(Index), new { id = journalEntryId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            if (model.Lines == null || model.Lines.Count == 0)
            {
                model.Lines = new List<ManualJournalLineViewModel>();
                for (int i = 0; i < 8; i++)
                {
                    model.Lines.Add(new ManualJournalLineViewModel());
                }
            }

            return View(model);
        }

        private async Task LoadLookupsAsync()
        {
            ViewBag.Accounts = await _db.Accounts.OrderBy(x => x.Code).ToListAsync();
            ViewBag.Units = await _db.Units.Include(x => x.Property).OrderBy(x => x.Property!.Code).ThenBy(x => x.UnitNumber).ToListAsync();
            ViewBag.Tenants = await _db.Tenants.OrderBy(x => x.Code).ToListAsync();
            ViewBag.Vendors = await _db.Vendors.OrderBy(x => x.Code).ToListAsync();
            ViewBag.Leases = await _db.Leases
                .Include(x => x.Unit)
                    .ThenInclude(x => x!.Property)
                .Include(x => x.Tenant)
                .OrderByDescending(x => x.StartDate)
                .ToListAsync();

            ViewBag.OwnerWithdrawalCategories = await _db.OwnerWithdrawalCategories
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Code)
                .ToListAsync();

            // Ids of accounts flagged as owner current accounts, so the General Journal
            // can enable the category selector only on those lines.
            ViewBag.DevelopmentProjects = await DevelopmentProjectService.DevelopmentCostOptions(_db);

            ViewBag.OwnerAccountIds = await _db.Accounts
                .Where(x => x.IsOwnerAccount)
                .Select(x => x.Id)
                .ToListAsync();
        }
    }
}