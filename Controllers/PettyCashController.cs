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
        private readonly JournalWorkflowService _journalWorkflowService;

        public PettyCashController(
            ApplicationDbContext db,
            PettyCashService pettyCashService,
            JournalWorkflowService journalWorkflowService)
        {
            _db = db;
            _pettyCashService = pettyCashService;
            _journalWorkflowService = journalWorkflowService;
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

            var model = new PettyCashExpenseViewModel { PettyCashAccountId = selected };
            (model.CurrentFloatBalance, model.CurrentOnHold) = await GetAvailabilityForFormAsync(selected);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PettyCashExpenseViewModel model)
        {
            await LoadExpenseListsAsync();
            (model.CurrentFloatBalance, model.CurrentOnHold) = await GetAvailabilityForFormAsync(model.PettyCashAccountId);

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
                var journalId = await _pettyCashService.PostExpenseAsync(model);

                // "Post Voucher" posts, through the same checks as Journal Inquiry.
                var notPosted = await _journalWorkflowService.PostNewEntryAsync(journalId);
                TempData["PettyCashMessage"] = notPosted == null
                    ? $"Voucher posted: {model.Description} for {model.Amount:N2}."
                    : notPosted.StartsWith(JournalWorkflowService.AwaitingApprovalPrefix)
                        ? $"Voucher recorded for {model.Amount:N2}. {notPosted} Until then the amount is on hold in the float."
                        : $"Voucher saved as a draft but not posted: {notPosted}";
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
                var journalId = await _pettyCashService.PostReplenishmentAsync(model);

                // "Post Replenishment" posts, through the same checks as Journal Inquiry.
                var notPosted = await _journalWorkflowService.PostNewEntryAsync(journalId);
                TempData["PettyCashMessage"] = notPosted == null
                    ? $"Replenishment posted: {model.Amount:N2} added to the float."
                    : notPosted.StartsWith(JournalWorkflowService.AwaitingApprovalPrefix)
                        ? $"Replenishment recorded for {model.Amount:N2}. {notPosted}"
                        : $"Replenishment saved as a draft but not posted: {notPosted}";
                return RedirectToAction(nameof(Index), new { pettyCashAccountId = model.PettyCashAccountId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }

        /// <summary>
        /// Returns what can be spent from a float, for the live figure on the entry form:
        /// the cash in hand less what vouchers waiting for approval hold.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> FloatBalance(int pettyCashAccountId)
        {
            var (balance, onHold) = await GetAvailabilityForFormAsync(pettyCashAccountId);
            return Json(new
            {
                balance,
                formatted = (balance - onHold).ToString("N2"),
                onHold,
                onHoldFormatted = onHold.ToString("N2"),
                cashFormatted = balance.ToString("N2")
            });
        }

        private async Task<(decimal CashInHand, decimal OnHold)> GetAvailabilityForFormAsync(int pettyCashAccountId) =>
            pettyCashAccountId == 0
                ? (0m, 0m)
                : await _pettyCashService.GetAvailabilityAsync(User.GetRequiredCurrentCompanyId(), pettyCashAccountId);

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
