using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Maintains the owner expenditure dimension. These categories are not accounts and
    /// not cost centres - they analyse movements on the owner's equity account only.
    /// </summary>
    public class OwnerWithdrawalCategoriesController : Controller
    {
        private readonly ApplicationDbContext _db;

        public OwnerWithdrawalCategoriesController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var categories = await _db.OwnerWithdrawalCategories
                .AsNoTracking()
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Code)
                .Select(x => new OwnerWithdrawalCategoryListItemViewModel
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

            ViewBag.OwnerAccounts = await _db.Accounts
                .AsNoTracking()
                .Where(x => x.IsOwnerAccount)
                .OrderBy(x => x.Code)
                .ToListAsync();

            return View(categories);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new OwnerWithdrawalCategoryViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(OwnerWithdrawalCategoryViewModel model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();

            if (!string.IsNullOrWhiteSpace(model.Code) &&
                await _db.OwnerWithdrawalCategories.AnyAsync(x => x.Code == model.Code.Trim()))
            {
                ModelState.AddModelError(nameof(model.Code), "Category code already exists.");
            }

            if (!ModelState.IsValid)
                return View(model);

            _db.OwnerWithdrawalCategories.Add(new OwnerWithdrawalCategory
            {
                CompanyId = companyId,
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
            var category = await _db.OwnerWithdrawalCategories.FirstOrDefaultAsync(x => x.Id == id);
            if (category == null)
                return NotFound();

            return View(new OwnerWithdrawalCategoryViewModel
            {
                Id = category.Id,
                Code = category.Code,
                Name = category.Name,
                Description = category.Description,
                SortOrder = category.SortOrder,
                IsActive = category.IsActive
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, OwnerWithdrawalCategoryViewModel model)
        {
            var category = await _db.OwnerWithdrawalCategories.FirstOrDefaultAsync(x => x.Id == id);
            if (category == null)
                return NotFound();

            if (!string.IsNullOrWhiteSpace(model.Code) &&
                await _db.OwnerWithdrawalCategories.AnyAsync(x => x.Code == model.Code.Trim() && x.Id != id))
            {
                ModelState.AddModelError(nameof(model.Code), "Category code already exists.");
            }

            if (!ModelState.IsValid)
            {
                model.Id = id;
                return View(model);
            }

            category.Code = model.Code.Trim();
            category.Name = model.Name.Trim();
            category.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
            category.SortOrder = model.SortOrder;
            category.IsActive = model.IsActive;

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
    }
}
