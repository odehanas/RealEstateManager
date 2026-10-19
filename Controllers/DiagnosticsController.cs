using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Read-only consistency checks on the current company's books, returned as raw
    /// JSON for support work. Nothing in the menus links here.
    ///
    /// Diagnostics has no entry in PermissionCatalog, so without the role below any
    /// signed-in staff user could open it. Keep every action here read-only: work
    /// that changes the books belongs on its own screen behind its own permission
    /// (closing a year is Closing/CloseYear, under Accounting Manage).
    /// </summary>
    [Authorize(Roles = "SystemAdmin")]
    public class DiagnosticsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly FinancialReportService _financialReportService;
        private readonly OpeningBalanceService _openingBalanceService;

        public DiagnosticsController(
            ApplicationDbContext db,
            FinancialReportService financialReportService,
            OpeningBalanceService openingBalanceService)
        {
            _db = db;
            _financialReportService = financialReportService;
            _openingBalanceService = openingBalanceService;
        }

        [HttpGet]
        public async Task<IActionResult> TrialBalance(DateTime? asOfDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var report = await _financialReportService.GetTrialBalanceAsync(companyId, (asOfDate ?? DateTime.Today).Date);
            return Json(report);
        }

        [HttpGet]
        public async Task<IActionResult> ProfitAndLoss(int? fiscalYear, DateTime? asOfDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var cutoffDate = (asOfDate ?? DateTime.Today).Date;
            var year = fiscalYear ?? cutoffDate.Year;

            var startDate = new DateTime(year, 1, 1);
            var endDate = cutoffDate.Year == year
                ? cutoffDate
                : new DateTime(year, 12, 31);

            var report = await _financialReportService.GetProfitAndLossAsync(company.Id, startDate, endDate);
            return Json(report);
        }

        [HttpGet]
        public async Task<IActionResult> BalanceSheet(DateTime? asOfDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var report = await _financialReportService.GetBalanceSheetAsync(company.Id, (asOfDate ?? DateTime.Today).Date);
            return Json(report);
        }

        [HttpGet]
        public async Task<IActionResult> ComparePnLAndBalanceSheet(DateTime? asOfDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var cutoffDate = (asOfDate ?? DateTime.Today).Date;
            var startDate = new DateTime(cutoffDate.Year, 1, 1);

            var pnl = await _financialReportService.GetProfitAndLossAsync(company.Id, startDate, cutoffDate);
            var balanceSheet = await _financialReportService.GetBalanceSheetAsync(company.Id, cutoffDate);

            return Json(new
            {
                asOfDate = cutoffDate,
                pnlNetProfitOrLoss = pnl.NetProfitOrLoss,
                balanceSheetCurrentYearProfitOrLoss = balanceSheet.CurrentYearProfitOrLoss,
                difference = pnl.NetProfitOrLoss - balanceSheet.CurrentYearProfitOrLoss,
                isMatching = pnl.NetProfitOrLoss == balanceSheet.CurrentYearProfitOrLoss
            });
        }

        [HttpGet]
        public async Task<IActionResult> UnbalancedJournals(DateTime? asOfDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var cutoffDate = (asOfDate ?? DateTime.Today).Date;

            var entries = await _db.JournalEntries
                .AsNoTracking()
                .Include(x => x.Lines)
                .Where(x => x.CompanyId == companyId && x.IsPosted && x.EntryDate <= cutoffDate)
                .ToListAsync();

            var unbalanced = entries
                .Select(x => new
                {
                    x.Id,
                    x.EntryNumber,
                    x.EntryDate,
                    x.Description,
                    x.SourceType,
                    TotalDebit = x.Lines.Sum(l => l.Debit),
                    TotalCredit = x.Lines.Sum(l => l.Credit),
                    Difference = x.Lines.Sum(l => l.Debit) - x.Lines.Sum(l => l.Credit)
                })
                .Where(x => x.Difference != 0m)
                .OrderBy(x => x.EntryDate)
                .ToList();

            return Json(new
            {
                asOfDate = cutoffDate,
                count = unbalanced.Count,
                totalDifference = unbalanced.Sum(x => x.Difference),
                entries = unbalanced
            });
        }

        [HttpGet]
        public async Task<IActionResult> OpeningBalanceCheck()
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var reconciliation = await _openingBalanceService.GetReconciliationAsync();
            var difference = reconciliation.TotalDebitOpeningBalance - reconciliation.TotalCreditOpeningBalance;

            return Json(new
            {
                companyId,
                companyName = company.Name,
                totalDebit = reconciliation.TotalDebitOpeningBalance,
                totalCredit = reconciliation.TotalCreditOpeningBalance,
                difference,
                isBalanced = difference == 0m
            });
        }
    }
}
