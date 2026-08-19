using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly FinancialReportService _financialReportService;
        private readonly ManagementReportService _managementReportService;
        private readonly VatReturnService _vatReturnService;
        private readonly OwnerReportService _ownerReportService;
        private readonly CashFlowService _cashFlowService;

        public ReportsController(
            ApplicationDbContext db,
            FinancialReportService financialReportService,
            ManagementReportService managementReportService,
            VatReturnService vatReturnService,
            OwnerReportService ownerReportService,
            CashFlowService cashFlowService)
        {
            _db = db;
            _financialReportService = financialReportService;
            _managementReportService = managementReportService;
            _vatReturnService = vatReturnService;
            _ownerReportService = ownerReportService;
            _cashFlowService = cashFlowService;
        }

        [HttpGet]
        public async Task<IActionResult> TrialBalance(DateTime? asOfDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var reportDate = (asOfDate ?? DateTime.Today).Date;
            var model = await _financialReportService.GetTrialBalanceAsync(company.Id, reportDate);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> ProfitAndLoss(DateTime? asOfDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var reportDate = (asOfDate ?? DateTime.Today).Date;
            var yearStart = new DateTime(reportDate.Year, 1, 1);

            var model = await _financialReportService.GetProfitAndLossAsync(company.Id, yearStart, reportDate);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> BalanceSheet(DateTime? asOfDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var reportDate = (asOfDate ?? DateTime.Today).Date;
            var model = await _financialReportService.GetBalanceSheetAsync(company.Id, reportDate);
            return View(model);
        }

        /// <summary>
        /// Profit and Loss broken down over the property dimension carried on every
        /// journal line. Accepts an explicit From/To period; defaults to the current
        /// fiscal year to date.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ProfitAndLossByProperty(DateTime? startDate, DateTime? endDate, int? propertyId)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var to = (endDate ?? DateTime.Today).Date;
            var from = (startDate ?? GetFiscalYearStart(to, company.FiscalYearStartMonth)).Date;

            if (from > to)
                (from, to) = (to, from);

            ViewBag.PropertyOptions = await _managementReportService.GetPropertyOptionsAsync();

            var model = await _managementReportService.GetProfitAndLossByPropertyAsync(company.Id, from, to, propertyId);
            return View(model);
        }

        /// <summary>Occupancy, area and rent realisation per property at a given date.</summary>
        [HttpGet]
        public async Task<IActionResult> PropertyUtilization(DateTime? asOfDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var reportDate = (asOfDate ?? DateTime.Today).Date;
            var model = await _managementReportService.GetPropertyUtilizationAsync(company.Id, reportDate);
            return View(model);
        }

        /// <summary>Ranked tenant scorecard based on receivables, collections, PDCs and tenure.</summary>
        [HttpGet]
        public async Task<IActionResult> TenantHealth(DateTime? asOfDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var reportDate = (asOfDate ?? DateTime.Today).Date;
            var model = await _managementReportService.GetTenantHealthAsync(company.Id, reportDate);
            return View(model);
        }

        /// <summary>
        /// Owner expenditure analysed over the owner withdrawal category dimension.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> OwnerWithdrawals(DateTime? startDate, DateTime? endDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var to = (endDate ?? DateTime.Today).Date;
            var from = (startDate ?? GetFiscalYearStart(to, company.FiscalYearStartMonth)).Date;

            if (from > to)
                (from, to) = (to, from);

            var model = await _managementReportService.GetOwnerWithdrawalsAsync(company.Id, from, to);
            return View(model);
        }

        /// <summary>
        /// VAT return for a tax period, plus the annual apportionment wash-up for the
        /// fiscal year containing the period end.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> VatReturn(DateTime? startDate, DateTime? endDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var to = (endDate ?? DateTime.Today).Date;

            // Default to the calendar quarter containing the end date - the usual UAE
            // tax period for a business of this size.
            var defaultQuarterStart = new DateTime(to.Year, ((to.Month - 1) / 3) * 3 + 1, 1);
            var from = (startDate ?? defaultQuarterStart).Date;

            if (from > to)
                (from, to) = (to, from);

            var yearStart = GetFiscalYearStart(to, company.FiscalYearStartMonth);

            ViewBag.WashUp = await _vatReturnService.GetAnnualWashUpAsync(company.Id, yearStart, to);

            var model = await _vatReturnService.GetReturnAsync(company.Id, from, to);
            return View(model);
        }

        /// <summary>
        /// Owner tenancy schedule - who occupies which unit, on what terms, and what is
        /// owed, grouped by the owner of the property.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> OwnerTenancySchedule(
            DateTime? asOfDate,
            string? ownershipType,
            string? ownerName,
            bool includeVacant = true)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var reportDate = (asOfDate ?? DateTime.Today).Date;

            ViewBag.OwnerNames = await _ownerReportService.GetOwnerNamesAsync(company.Id);

            var model = await _ownerReportService.GetTenancyScheduleAsync(
                company.Id, reportDate, ownershipType, ownerName, includeVacant);

            return View(model);
        }

        /// <summary>Statement of Cash Flows, IAS 7 direct method.</summary>
        [HttpGet]
        public async Task<IActionResult> CashFlow(DateTime? startDate, DateTime? endDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var to = (endDate ?? DateTime.Today).Date;
            var from = (startDate ?? GetFiscalYearStart(to, company.FiscalYearStartMonth)).Date;

            if (from > to)
                (from, to) = (to, from);

            var model = await _cashFlowService.GetCashFlowAsync(company.Id, from, to);
            return View(model);
        }

        private static DateTime GetFiscalYearStart(DateTime reportDate, int fiscalYearStartMonth)
        {
            return reportDate.Month >= fiscalYearStartMonth
                ? new DateTime(reportDate.Year, fiscalYearStartMonth, 1)
                : new DateTime(reportDate.Year - 1, fiscalYearStartMonth, 1);
        }
    }
}
