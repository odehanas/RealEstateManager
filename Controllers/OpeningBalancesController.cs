using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The opening position: every account's opening balance, entered here, beside the
    /// tenant, vendor and fixed asset figures that make up the control accounts.
    /// </summary>
    public class OpeningBalancesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly OpeningBalanceService _openingBalanceService;

        public OpeningBalancesController(ApplicationDbContext db, OpeningBalanceService openingBalanceService)
        {
            _db = db;
            _openingBalanceService = openingBalanceService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = await _openingBalanceService.GetReconciliationAsync();
            return View(model);
        }

        /// <summary>
        /// Saves the opening balances typed on the screen. The same rules as editing an
        /// account in the chart of accounts: sub-ledger control accounts, the opening
        /// balance control account, headings and non-postable accounts take no figure.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(List<OpeningBalanceEntry> entries)
        {
            var wanted = (entries ?? new List<OpeningBalanceEntry>())
                .GroupBy(x => x.AccountId)
                .ToDictionary(g => g.Key, g => Math.Round(g.Last().OpeningBalance, 2));

            var snapshot = await _openingBalanceService.BuildSnapshotAsync();
            var accounts = await _db.Accounts.Where(x => wanted.Keys.Contains(x.Id)).ToListAsync();

            var changed = new List<string>();
            foreach (var account in accounts)
            {
                var isControl = snapshot.TryGet(account.Id, out var info)
                    && info != null
                    && info.Source != OpeningBalanceService.SourceManual;
                if (account.IsGroup || !account.IsPostable || isControl ||
                    account.Code == OpeningBalanceService.OpeningBalanceControlAccountCode)
                    continue;

                var amount = wanted[account.Id];
                if (account.OpeningBalance == amount)
                    continue;

                account.OpeningBalance = amount;
                changed.Add(account.Code);
            }

            await _db.SaveChangesAsync();

            TempData["OpeningBalanceMessage"] = changed.Count == 0
                ? "No opening balance changed."
                : $"Opening balance saved for {changed.Count} account(s): {string.Join(", ", changed)}.";

            return RedirectToAction(nameof(Index));
        }
    }
}
