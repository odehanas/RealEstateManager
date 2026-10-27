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
        private readonly GeneralJournalReportService _generalJournal;

        public ReportsController(
            ApplicationDbContext db,
            FinancialReportService financialReportService,
            ManagementReportService managementReportService,
            VatReturnService vatReturnService,
            OwnerReportService ownerReportService,
            CashFlowService cashFlowService,
            GeneralJournalReportService generalJournal)
        {
            _db = db;
            _financialReportService = financialReportService;
            _managementReportService = managementReportService;
            _vatReturnService = vatReturnService;
            _ownerReportService = ownerReportService;
            _cashFlowService = cashFlowService;
            _generalJournal = generalJournal;
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
            // Share capital and prior-year retained earnings are fixed lines on the
            // statement; their accounts let those lines open the ledger too.
            ViewBag.EquityAccountIds = await _db.Accounts.AsNoTracking()
                .Where(x => x.CompanyId == company.Id && (x.Code == "31100" || x.Code == "32100"))
                .GroupBy(x => x.Code)
                .Select(g => new { Code = g.Key, Id = g.Min(x => x.Id) })
                .ToDictionaryAsync(x => x.Code, x => x.Id);

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

        /// <summary>
        /// General Journal: every entry in the period with its lines and the source it
        /// came from. format=csv downloads it, one row per line.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GeneralJournal(
            DateTime? fromDate, DateTime? toDate, string? source, string? status, string? search, string? format)
        {
            var to = (toDate ?? DateTime.Today).Date;
            var from = (fromDate ?? new DateTime(to.Year, to.Month, 1)).Date;
            if (from > to)
                (from, to) = (to, from);

            var model = await _generalJournal.GetAsync(from, to, source, status, search);

            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            {
                var csv = new System.Text.StringBuilder();
                csv.AppendLine("Entry,Date,Period,Source,Document,Reference,Entry description,Status,Posted by,Account code,Account,Line description,Property,Debit,Credit");
                foreach (var entry in model.Entries)
                {
                    foreach (var line in entry.Lines)
                    {
                        csv.AppendLine(string.Join(",", new[]
                        {
                            entry.EntryNumber, entry.EntryDate.ToString("yyyy-MM-dd"), entry.PeriodName, entry.SourceLabel,
                            entry.DocumentNumber, entry.ReferenceNumber, entry.Description, entry.Status, entry.PostedBy,
                            line.AccountCode, line.AccountName, line.Description, line.Property,
                            line.Debit.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                            line.Credit.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                        }.Select(Csv)));
                    }
                }

                var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
                return File(bytes, "text/csv", $"general-journal-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
            }

            ViewBag.Sources = await _generalJournal.SourcesAsync();
            return View(model);
        }

        /// <summary>
        /// Each managed property's result for its owner (rent less commission, its VAT and
        /// costs charged to the owner) beside the commission it earned the company.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> OwnerProfitability(DateTime? fromDate, DateTime? toDate, string? owner,
            [FromServices] OwnerProfitabilityService profitability)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId);
            var to = (toDate ?? DateTime.Today).Date;
            var from = (fromDate ?? GetFiscalYearStart(to, company.FiscalYearStartMonth)).Date;
            if (from > to)
                (from, to) = (to, from);

            return View(await profitability.GetAsync(companyId, from, to, owner));
        }

        // A CSV cell: quoted when needed, and a leading = + - @ neutralised so a
        // spreadsheet never runs it as a formula.
        private static string Csv(string? value)
        {
            value ??= string.Empty;
            if (value.Length > 0 && "=+-@".Contains(value[0]))
                value = "'" + value;
            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }

        private static DateTime GetFiscalYearStart(DateTime reportDate, int fiscalYearStartMonth)
        {
            return reportDate.Month >= fiscalYearStartMonth
                ? new DateTime(reportDate.Year, fiscalYearStartMonth, 1)
                : new DateTime(reportDate.Year - 1, fiscalYearStartMonth, 1);
        }
    }
}
