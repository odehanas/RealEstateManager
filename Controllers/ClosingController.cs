using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The Closing Cockpit: each month's checklist with its traffic lights, the runs that
    /// turn them green, closing the month, asking for a closed month to be reopened, and
    /// the year-end close. Accounting.Manage opens it; the reopen decisions need
    /// Closing.ApproveReopen (see AppPermissions).
    /// </summary>
    public class ClosingController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly PeriodClosingService _closing;
        private readonly PermissionService _permissions;

        public ClosingController(ApplicationDbContext db, PeriodClosingService closing, PermissionService permissions)
        {
            _db = db;
            _closing = closing;
            _permissions = permissions;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? fiscalYear, int? period)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var year = fiscalYear ?? await DefaultFiscalYearAsync(companyId);
            var canApprove = await _permissions.HasPermissionAsync(User, AppPermissions.PeriodReopenApprove);

            var model = await _closing.GetCockpitAsync(companyId, year, period, User.GetAppUserId() ?? 0, canApprove);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunStep(int periodId, string step)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var (success, message) = await _closing.RunStepAsync(companyId, periodId, step);
            Flash(success, message);
            return await BackToPeriodAsync(periodId);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClosePeriod(int periodId, bool acknowledgeWarnings = false)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var (success, message) = await _closing.ClosePeriodAsync(companyId, periodId, acknowledgeWarnings, CurrentUserName());
            Flash(success, message);
            return await BackToPeriodAsync(periodId);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestReopen(int periodId, string? reason)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var (success, message) = await _closing.RequestReopenAsync(
                companyId, periodId, reason, User.GetAppUserId() ?? 0, CurrentUserName());
            Flash(success, message);
            return await BackToPeriodAsync(periodId);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> WithdrawReopen(int requestId, int periodId)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var (success, message) = await _closing.WithdrawReopenAsync(companyId, requestId, User.GetAppUserId() ?? 0, CurrentUserName());
            Flash(success, message);
            return await BackToPeriodAsync(periodId);
        }

        /// <summary>The approver's desk: every reopen request waiting, and what was decided lately.</summary>
        [HttpGet]
        public async Task<IActionResult> ReopenRequests()
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            ViewBag.CanApprove = await _permissions.HasPermissionAsync(User, AppPermissions.PeriodReopenApprove);
            var model = await _closing.GetReopenRequestsAsync(companyId, User.GetAppUserId() ?? 0);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveReopen(int requestId, string? comment, string? returnTo)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var (success, message) = await _closing.ApproveReopenAsync(
                companyId, requestId, comment, User.GetAppUserId() ?? 0, CurrentUserName(), User.IsInRole("SystemAdmin"));
            Flash(success, message);
            return AfterDecision(returnTo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectReopen(int requestId, string? comment, string? returnTo)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var (success, message) = await _closing.RejectReopenAsync(
                companyId, requestId, comment, User.GetAppUserId() ?? 0, CurrentUserName());
            Flash(success, message);
            return AfterDecision(returnTo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CloseYear(int fiscalYear)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var (success, message) = await _closing.CloseFiscalYearAsync(companyId, fiscalYear, CurrentUserName());
            Flash(success, message);
            return RedirectToAction(nameof(Index), new { fiscalYear });
        }

        /// <summary>The fiscal year of the oldest period still waiting to be closed, else today's.</summary>
        private async Task<int> DefaultFiscalYearAsync(int companyId)
        {
            var today = DateTime.Today;
            var oldestOpen = await _db.AccountingPeriods.AsNoTracking()
                .Where(x => x.CompanyId == companyId && !x.IsClosed && x.StartDate <= today)
                .OrderBy(x => x.StartDate)
                .Select(x => (int?)x.FiscalYear)
                .FirstOrDefaultAsync();

            if (oldestOpen.HasValue)
                return oldestOpen.Value;

            var current = await _db.AccountingPeriods.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.StartDate <= today && x.EndDate >= today)
                .Select(x => (int?)x.FiscalYear)
                .FirstOrDefaultAsync();

            return current ?? today.Year;
        }

        private async Task<IActionResult> BackToPeriodAsync(int periodId)
        {
            var period = await _db.AccountingPeriods.AsNoTracking()
                .Where(x => x.Id == periodId)
                .Select(x => new { x.FiscalYear, x.PeriodNumber })
                .FirstOrDefaultAsync();

            return period == null
                ? RedirectToAction(nameof(Index))
                : RedirectToAction(nameof(Index), new { fiscalYear = period.FiscalYear, period = period.PeriodNumber });
        }

        private IActionResult AfterDecision(string? returnTo) =>
            !string.IsNullOrWhiteSpace(returnTo) && Url.IsLocalUrl(returnTo)
                ? Redirect(returnTo)
                : RedirectToAction(nameof(ReopenRequests));

        private void Flash(bool success, string message) =>
            TempData[success ? "ClosingSuccess" : "ClosingError"] = message;

        private string CurrentUserName() =>
            User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value ?? "User";
    }
}
