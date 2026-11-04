using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Maintains the owner's places (house, farm, private office...), the second dimension
    /// on owner withdrawals beside the category.
    /// </summary>
    public class OwnerWithdrawalLocationsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public OwnerWithdrawalLocationsController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var locations = await _db.OwnerWithdrawalLocations
                .AsNoTracking()
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Code)
                .Select(x => new OwnerWithdrawalLocationListItemViewModel
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    Description = x.Description,
                    SortOrder = x.SortOrder,
                    IsActive = x.IsActive,
                    UsageCount = x.JournalEntryLines.Count(l => l.JournalEntry != null && l.JournalEntry.IsPosted),
                    NetAmount = x.JournalEntryLines
                        .Where(l => l.JournalEntry != null && l.JournalEntry.IsPosted)
                        .Sum(l => l.Debit - l.Credit)
                })
                .ToListAsync();

            return View(locations);
        }

        [HttpGet]
        public IActionResult Create() => View(new OwnerWithdrawalLocationViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(OwnerWithdrawalLocationViewModel model)
        {
            if (!string.IsNullOrWhiteSpace(model.Code) &&
                await _db.OwnerWithdrawalLocations.AnyAsync(x => x.Code == model.Code.Trim()))
            {
                ModelState.AddModelError(nameof(model.Code), "Location code already exists.");
            }

            if (!ModelState.IsValid)
                return View(model);

            _db.OwnerWithdrawalLocations.Add(new OwnerWithdrawalLocation
            {
                CompanyId = User.GetRequiredCurrentCompanyId(),
                Code = model.Code.Trim(),
                Name = model.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
                SortOrder = model.SortOrder,
                IsActive = model.IsActive
            });

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var location = await _db.OwnerWithdrawalLocations.FirstOrDefaultAsync(x => x.Id == id);
            if (location == null)
                return NotFound();

            return View(new OwnerWithdrawalLocationViewModel
            {
                Id = location.Id,
                Code = location.Code,
                Name = location.Name,
                Description = location.Description,
                SortOrder = location.SortOrder,
                IsActive = location.IsActive
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, OwnerWithdrawalLocationViewModel model)
        {
            var location = await _db.OwnerWithdrawalLocations.FirstOrDefaultAsync(x => x.Id == id);
            if (location == null)
                return NotFound();

            if (!string.IsNullOrWhiteSpace(model.Code) &&
                await _db.OwnerWithdrawalLocations.AnyAsync(x => x.Code == model.Code.Trim() && x.Id != id))
            {
                ModelState.AddModelError(nameof(model.Code), "Location code already exists.");
            }

            if (!ModelState.IsValid)
            {
                model.Id = id;
                return View(model);
            }

            location.Code = model.Code.Trim();
            location.Name = model.Name.Trim();
            location.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
            location.SortOrder = model.SortOrder;
            location.IsActive = model.IsActive;

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
    }
}
