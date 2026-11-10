using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The company's bank accounts, each tied to its ledger account and marked as the
    /// company's own money or as owners' funds.
    /// </summary>
    public class BankAccountsController : Controller
    {
        private readonly LandlordService _landlords;
        private readonly ApplicationDbContext _db;

        public BankAccountsController(LandlordService landlords, ApplicationDbContext db)
        {
            _landlords = landlords;
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index() => View(await _landlords.GetBankAccountsAsync());

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await LoadListsAsync(null);
            return View("Form", new BankAccountEditViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BankAccountEditViewModel model)
        {
            model.Id = 0;
            return await SaveAsync(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var model = await _landlords.GetBankAccountAsync(id);
            if (model == null)
                return NotFound();

            await LoadListsAsync(id);
            return View("Form", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, BankAccountEditViewModel model)
        {
            model.Id = id;
            return await SaveAsync(model);
        }

        private async Task<IActionResult> SaveAsync(BankAccountEditViewModel model)
        {
            var saved = await _landlords.SaveBankAccountAsync(ModelState, User.GetRequiredCurrentCompanyId(), model);
            if (saved == null)
            {
                await LoadListsAsync(model.Id == 0 ? null : model.Id);
                return View("Form", model);
            }

            TempData["SuccessMessage"] = $"Bank account {model.Name.Trim()} saved.";
            return RedirectToAction(nameof(Index));
        }

        private async Task LoadListsAsync(int? bankAccountId)
        {
            ViewBag.LedgerAccounts = (await _landlords.GetLedgerChoicesAsync(bankAccountId))
                .Select(x => new SelectListItem { Value = x.Id.ToString(), Text = $"{x.Code} - {x.Name}" })
                .ToList();

            ViewBag.Landlords = await _db.Landlords.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Name })
                .ToListAsync();
        }
    }
}
