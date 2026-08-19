using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Petty cash journal and float control. Every voucher posts a real, balanced
    /// journal through the general ledger - there is no separate petty cash ledger.
    /// </summary>
    public class PettyCashController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly PettyCashService _pettyCashService;

        public PettyCashController(ApplicationDbContext db, PettyCashService pettyCashService)
        {
            _db = db;
            _pettyCashService = pettyCashService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate, int? pettyCashAccountId)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var to = (endDate ?? DateTime.Today).Date;
            var from = (startDate ?? GetFiscalYearStart(to, company.FiscalYearStartMonth)).Date;

            if (from > to)
                (from, to) = (to, from);

            ViewBag.FloatAccounts = await _pettyCashService.GetFloatAccountsAsync();

            var model = await _pettyCashService.GetDashboardAsync(company.Id, from, to, pettyCashAccountId);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Create(int? pettyCashAccountId)
        {
            await LoadExpenseListsAsync();

            var floats = await _pettyCashService.GetFloatAccountsAsync();
            var selected = pettyCashAccountId ?? floats.FirstOrDefault()?.Id ?? 0;

            var model = new PettyCashExpenseViewModel
            {
                PettyCashAccountId = selected,
                CurrentFloatBalance = await GetBalanceForFormAsync(selected)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PettyCashExpenseViewModel model)
        {
            await LoadExpenseListsAsync();
            model.CurrentFloatBalance = await GetBalanceForFormAsync(model.PettyCashAccountId);

            if (model.Amount <= 0m)
                ModelState.AddModelError(nameof(model.Amount), "Amount must be greater than zero.");

            if (model.PettyCashAccountId == 0)
                ModelState.AddModelError(nameof(model.PettyCashAccountId), "Select a petty cash float.");

            if (model.ExpenseAccountId == 0)
                ModelState.AddModelError(nameof(model.ExpenseAccountId), "Select an expense account.");

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                await _pettyCashService.PostExpenseAsync(model);
                TempData["PettyCashMessage"] = $"Voucher posted: {model.Description} for {model.Amount:N2}.";
                return RedirectToAction(nameof(Index), new { pettyCashAccountId = model.PettyCashAccountId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Replenish(int? pettyCashAccountId)
        {
            await LoadReplenishmentListsAsync();

            var floats = await _pettyCashService.GetFloatAccountsAsync();
            var funding = await _pettyCashService.GetFundingAccountsAsync();

            var model = new PettyCashReplenishmentViewModel
            {
                PettyCashAccountId = pettyCashAccountId ?? floats.FirstOrDefault()?.Id ?? 0,
                FundingAccountId = funding.FirstOrDefault(x => x.Code == "11100")?.Id
                                   ?? funding.FirstOrDefault()?.Id ?? 0
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Replenish(PettyCashReplenishmentViewModel model)
        {
            await LoadReplenishmentListsAsync();

            if (model.Amount <= 0m)
                ModelState.AddModelError(nameof(model.Amount), "Amount must be greater than zero.");

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                await _pettyCashService.PostReplenishmentAsync(model);
                TempData["PettyCashMessage"] = $"Replenishment posted: {model.Amount:N2}.";
                return RedirectToAction(nameof(Index), new { pettyCashAccountId = model.PettyCashAccountId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }

        /// <summary>
        /// Returns the current balance of a float, for the live figure on the entry form.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> FloatBalance(int pettyCashAccountId)
        {
            var balance = await GetBalanceForFormAsync(pettyCashAccountId);
            return Json(new { balance, formatted = balance.ToString("N2") });
        }

        private async Task<decimal> GetBalanceForFormAsync(int pettyCashAccountId)
        {
            if (pettyCashAccountId == 0)
                return 0m;

            var companyId = User.GetRequiredCurrentCompanyId();

            var dashboard = await _pettyCashService.GetDashboardAsync(
                companyId,
                DateTime.Today,
                DateTime.Today,
                pettyCashAccountId);

            return dashboard.Floats
                .Where(x => x.AccountId == pettyCashAccountId)
                .Select(x => x.ClosingBalance)
                .FirstOrDefault();
        }

        private async Task LoadExpenseListsAsync()
        {
            ViewBag.FloatAccounts = await _pettyCashService.GetFloatAccountsAsync();
            ViewBag.ExpenseAccounts = await _pettyCashService.GetExpenseAccountsAsync();
            ViewBag.Properties = await _db.Properties.OrderBy(x => x.Code).ToListAsync();
            ViewBag.Units = await _db.Units
                .Include(x => x.Property)
                .OrderBy(x => x.Property!.Code)
                .ThenBy(x => x.UnitNumber)
                .ToListAsync();
        }

        private async Task LoadReplenishmentListsAsync()
        {
            ViewBag.FloatAccounts = await _pettyCashService.GetFloatAccountsAsync();
            ViewBag.FundingAccounts = await _pettyCashService.GetFundingAccountsAsync();
        }

        private static DateTime GetFiscalYearStart(DateTime reportDate, int fiscalYearStartMonth)
        {
            return reportDate.Month >= fiscalYearStartMonth
                ? new DateTime(reportDate.Year, fiscalYearStartMonth, 1)
                : new DateTime(reportDate.Year - 1, fiscalYearStartMonth, 1);
        }
    }
}
