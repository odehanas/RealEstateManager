using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class ClosingController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _postingService;

        public ClosingController(ApplicationDbContext db, AccountingPostingService postingService)
        {
            _db = db;
            _postingService = postingService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? fiscalYear)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var year = fiscalYear ?? DateTime.Today.Year;

            var periods = await _db.AccountingPeriods
                .Where(x => x.CompanyId == company.Id && x.FiscalYear == year)
                .OrderBy(x => x.PeriodNumber)
                .ToListAsync();

            var closings = await _db.FiscalYearClosings
                .Include(x => x.JournalEntry)
                .OrderByDescending(x => x.FiscalYear)
                .ToListAsync();

            var model = new ClosingDashboardViewModel
            {
                FiscalYear = year,
                Periods = periods.Select(x => new AccountingPeriodItemViewModel
                {
                    PeriodNumber = x.PeriodNumber,
                    Name = x.Name,
                    StartDate = x.StartDate,
                    EndDate = x.EndDate,
                    IsClosed = x.IsClosed,
                    ClosedAt = x.ClosedAt
                }).ToList(),
                FiscalYearClosings = closings.Select(x => new FiscalYearClosingItemViewModel
                {
                    FiscalYear = x.FiscalYear,
                    ProfitOrLossAmount = x.ProfitOrLossAmount,
                    ClosedAt = x.ClosedAt,
                    JournalEntryNumber = x.JournalEntry?.EntryNumber
                }).ToList()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClosePeriod(int fiscalYear, int periodNumber)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            await _postingService.CloseAccountingPeriodAsync(company.Id, fiscalYear, periodNumber);

            return RedirectToAction(nameof(Index), new { fiscalYear });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CloseYear(int fiscalYear)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            await _postingService.CloseFiscalYearAsync(company.Id, fiscalYear);

            return RedirectToAction(nameof(Index), new { fiscalYear });
        }
    }
}
