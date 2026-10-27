using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly OpeningBalanceService _openingBalanceService;

        private readonly ManagementReportService _reports;
        private readonly PeriodClosingService _closing;
        private readonly PermissionService _permissions;
        private readonly HrService _hr;

        public HomeController(
            ApplicationDbContext db,
            OpeningBalanceService openingBalanceService,
            ManagementReportService reports,
            PeriodClosingService closing,
            PermissionService permissions,
            HrService hr)
        {
            _hr = hr;
            _db = db;
            _openingBalanceService = openingBalanceService;
            _reports = reports;
            _closing = closing;
            _permissions = permissions;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // A maintenance engineer's home is their job list, not the company's figures.
            if (await _permissions.IsMaintenanceOnlyAsync(User))
                return RedirectToAction("Index", "MaintenanceJobs");

            var today = DateTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);

            var model = new HomeDashboardViewModel
            {
                Today = today
            };

            model.PropertiesCount = await _db.Properties
                .AsNoTracking()
                .CountAsync(x => x.IsActive);

            model.OwnedPropertiesCount = await _db.Properties
                .AsNoTracking()
                .CountAsync(x => x.IsActive && x.OwnershipType == "Owned");

            model.ManagedPropertiesCount = await _db.Properties
                .AsNoTracking()
                .CountAsync(x => x.IsActive && x.OwnershipType == "Managed");

            model.ActiveUnitsCount = await _db.Units
                .AsNoTracking()
                .CountAsync(x => x.IsActive);

            model.OccupiedUnitsCount = await _db.Units
                .AsNoTracking()
                .CountAsync(x => x.IsActive && x.Status == "Occupied");

            model.VacantUnitsCount = await _db.Units
                .AsNoTracking()
                .CountAsync(x => x.IsActive && x.Status == "Vacant");

            model.UnderMaintenanceUnitsCount = await _db.Units
                .AsNoTracking()
                .CountAsync(x => x.IsActive && x.Status == "UnderMaintenance");

            model.ReservedUnitsCount = await _db.Units
                .AsNoTracking()
                .CountAsync(x => x.IsActive && x.Status == "Reserved");

            model.ActiveLeasesCount = await _db.Leases
                .AsNoTracking()
                .CountAsync(x => x.Status == "Active");

            model.ActiveTenantsCount = await _db.Tenants
                .AsNoTracking()
                .CountAsync(x => x.IsActive);

            model.OpenPeriodsCount = await _db.AccountingPeriods
                .AsNoTracking()
                .CountAsync(x => !x.IsClosed);

            var currentPeriod = await _db.AccountingPeriods
                .AsNoTracking()
                .Where(x => x.StartDate <= today && x.EndDate >= today)
                .OrderByDescending(x => x.FiscalYear)
                .ThenByDescending(x => x.PeriodNumber)
                .FirstOrDefaultAsync();

            if (currentPeriod != null)
            {
                model.CurrentPeriodName = $"{currentPeriod.FiscalYear} / {currentPeriod.Name}";
            }

            // The months still waiting to be closed, for those who close them or approve reopening.
            var dashboardCompanyId = User.GetCurrentCompanyId();
            if (dashboardCompanyId.HasValue &&
                await _permissions.HasAnyPermissionAsync(User, AppPermissions.AccountingManage, AppPermissions.PeriodReopenApprove))
            {
                model.UnclosedPeriods = await _closing.GetUnclosedPeriodsAsync(dashboardCompanyId.Value, today);
            }

            // Documents running out and a payroll still to do, for HR and payroll staff.
            if (dashboardCompanyId.HasValue &&
                await _permissions.HasAnyPermissionAsync(User, AppPermissions.HrManage, AppPermissions.PayrollManage, AppPermissions.PayrollApprove))
            {
                var documents = await _hr.GetExpiringDocumentsAsync(30);
                var lastMonth = monthStart.AddMonths(-1);
                var lastRun = await _db.PayrollRuns.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Year == lastMonth.Year && x.Month == lastMonth.Month);
                var activeEmployees = await _db.Employees.AsNoTracking().CountAsync(x => x.Status == EmployeeStatuses.Active);

                model.HrAlerts = new HrDashboardAlerts
                {
                    ActiveEmployees = activeEmployees,
                    ExpiredDocuments = documents.Count(x => x.DaysLeft < 0),
                    ExpiringDocuments = documents.Count(x => x.DaysLeft >= 0),
                    PendingLeave = await _db.LeaveRequests.AsNoTracking().CountAsync(x => x.Status == LeaveStatuses.Pending),
                    PayrollDueMonth = activeEmployees > 0 && (lastRun == null || lastRun.Status == PayrollRunStatuses.Draft)
                        ? lastMonth.ToString("MMMM yyyy")
                        : null,
                    PayrollDueStatus = lastRun == null ? "not prepared" : lastRun.Status == PayrollRunStatuses.Draft ? "waiting for approval" : null
                };
            }

            // Posted invoices only. A lease raises all its future invoices as drafts,
            // and a draft is not a receivable until it is posted to the accounts.
            var outstandingInvoices = await _db.CustomerInvoices
                .AsNoTracking()
                .Where(x =>
                    x.JournalEntry != null &&
                    x.JournalEntry.IsPosted &&
                    x.Status != "Cancelled" &&
                    x.TotalAmount > x.AmountPaid)
                .Select(x => new
                {
                    OutstandingAmount = x.TotalAmount - x.AmountPaid,
                    x.DueDate
                })
                .ToListAsync();

            model.OutstandingInvoicesCount = outstandingInvoices.Count;
            model.OutstandingReceivables = outstandingInvoices.Sum(x => x.OutstandingAmount);
            model.OverdueInvoicesCount = outstandingInvoices.Count(x => x.DueDate.Date < today);
            model.OverdueReceivables = outstandingInvoices
                .Where(x => x.DueDate.Date < today)
                .Sum(x => x.OutstandingAmount);

            model.ThisMonthInvoices = await _db.CustomerInvoices
                .AsNoTracking()
                .Where(x =>
                    x.JournalEntry != null &&
                    x.JournalEntry.IsPosted &&
                    x.Status != "Cancelled" &&
                    x.InvoiceDate.Date >= monthStart &&
                    x.InvoiceDate.Date <= today)
                .SumAsync(x => (decimal?)x.TotalAmount) ?? 0m;

            model.ThisMonthReceipts = await _db.CustomerReceipts
                .AsNoTracking()
                .Where(x =>
                    x.JournalEntry != null &&
                    x.JournalEntry.IsPosted &&
                    x.ReceiptDate.Date >= monthStart &&
                    x.ReceiptDate.Date <= today)
                .SumAsync(x => (decimal?)x.Amount) ?? 0m;

            if (model.ActiveUnitsCount > 0)
            {
                model.OccupancyRate = Math.Round(
                    model.OccupiedUnitsCount * 100m / model.ActiveUnitsCount,
                    2);

                model.VacancyRate = Math.Round(
                    model.VacantUnitsCount * 100m / model.ActiveUnitsCount,
                    2);
            }

            if (model.PropertiesCount > 0)
            {
                model.ManagedPortfolioRate = Math.Round(
                    model.ManagedPropertiesCount * 100m / model.PropertiesCount,
                    2);
            }

            if (model.ThisMonthInvoices > 0m)
            {
                model.MonthlyCollectionRate = Math.Round(
                    model.ThisMonthReceipts * 100m / model.ThisMonthInvoices,
                    2);
            }

            if (model.OutstandingReceivables > 0m)
            {
                model.OverdueReceivablesRate = Math.Round(
                    model.OverdueReceivables * 100m / model.OutstandingReceivables,
                    2);
            }

            // The Tenant Health report's score, averaged over tenants with a current contract.
            var companyIdForHealth = User.GetCurrentCompanyId();
            if (companyIdForHealth.HasValue)
            {
                var health = await _reports.GetTenantHealthAsync(companyIdForHealth.Value, today);
                var current = health.Rows.Where(x => x.IsScored && x.CurrentContractNumber != null).ToList();
                model.ScoredTenantsCount = current.Count;
                model.TenantsAtRiskCount = current.Count(x => x.HealthScore < DashboardTargets.HealthWatch);
                model.AverageTenantHealth = current.Count == 0 ? null : Math.Round(current.Average(x => x.HealthScore), 1);
            }

            model.RecentInvoices = await _db.CustomerInvoices
                .AsNoTracking()
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                // Issued invoices only; otherwise next year's draft instalments head the list.
                .Where(x => x.JournalEntry != null && x.JournalEntry.IsPosted)
                .OrderByDescending(x => x.InvoiceDate)
                .ThenByDescending(x => x.Id)
                .Take(5)
                .Select(x => new HomeDashboardInvoiceItemViewModel
                {
                    InvoiceNumber = x.InvoiceNumber,
                    InvoiceDate = x.InvoiceDate,
                    TenantName = x.Lease != null && x.Lease.Tenant != null ? x.Lease.Tenant.FullName : "-",
                    LeaseDisplay = BuildLeaseDisplay(x.Lease),
                    TotalAmount = x.TotalAmount,
                    OutstandingAmount = x.TotalAmount - x.AmountPaid,
                    Status = x.Status
                })
                .ToListAsync();

            model.RecentReceipts = await _db.CustomerReceipts
                .AsNoTracking()
                .Include(x => x.CustomerInvoice)
                    .ThenInclude(x => x!.Lease)
                        .ThenInclude(x => x!.Tenant)
                .OrderByDescending(x => x.ReceiptDate)
                .ThenByDescending(x => x.Id)
                .Take(5)
                .Select(x => new HomeDashboardReceiptItemViewModel
                {
                    ReceiptNumber = x.ReceiptNumber,
                    ReceiptDate = x.ReceiptDate,
                    TenantName = x.CustomerInvoice != null &&
                                 x.CustomerInvoice.Lease != null &&
                                 x.CustomerInvoice.Lease.Tenant != null
                        ? x.CustomerInvoice.Lease.Tenant.FullName
                        : "-",
                    Amount = x.Amount,
                    PaymentMethod = x.PaymentMethod
                })
                .ToListAsync();

            var accountSetting = await _db.CompanyAccountSettings
                .AsNoTracking()
                .Include(x => x.DefaultCashAccount)
                .Include(x => x.DueToLandlordsAccount)
                .FirstOrDefaultAsync();

            if (accountSetting?.DefaultCashAccountId.HasValue == true && accountSetting.DefaultCashAccount != null)
            {
                model.CashKpiAvailable = true;
                model.CashAccountName = $"{accountSetting.DefaultCashAccount.Code} - {accountSetting.DefaultCashAccount.Name}";
                model.CashBalance = await GetAccountBalanceAsync(accountSetting.DefaultCashAccount);
            }

            if (accountSetting?.DueToLandlordsAccountId.HasValue == true && accountSetting.DueToLandlordsAccount != null)
            {
                model.LandlordKpiAvailable = true;
                model.DueToLandlordsAccountName = $"{accountSetting.DueToLandlordsAccount.Code} - {accountSetting.DueToLandlordsAccount.Name}";
                model.DueToLandlordsBalance = await GetAccountBalanceAsync(accountSetting.DueToLandlordsAccount);
            }

            var managedProperties = await _db.Properties
                .AsNoTracking()
                .Include(x => x.Units)
                .Where(x => x.IsActive && x.OwnershipType == "Managed")
                .OrderBy(x => x.Code)
                .ToListAsync();

            var managedLeases = await _db.Leases
                .AsNoTracking()
                .Include(x => x.Unit)
                .Where(x =>
                    x.Unit != null &&
                    x.Unit.PropertyId > 0 &&
                    x.Status == "Active")
                .ToListAsync();

            var managedInvoices = await _db.CustomerInvoices
                .AsNoTracking()
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                .Where(x =>
                    x.JournalEntry != null &&
                    x.JournalEntry.IsPosted &&
                    x.Status != "Cancelled" &&
                    x.Lease != null &&
                    x.Lease.Unit != null)
                .ToListAsync();

            model.ManagedPropertyDrilldown = managedProperties
                .Select(property =>
                {
                    var propertyUnits = property.Units.Where(x => x.IsActive).ToList();
                    var propertyLeases = managedLeases
                        .Where(x => x.Unit != null && x.Unit.PropertyId == property.Id)
                        .ToList();
                    var propertyInvoices = managedInvoices
                        .Where(x => x.Lease != null && x.Lease.Unit != null && x.Lease.Unit.PropertyId == property.Id)
                        .ToList();

                    var unitsCount = propertyUnits.Count;
                    var occupiedUnitsCount = propertyUnits.Count(x => x.Status == "Occupied");
                    var occupancyRate = unitsCount > 0
                        ? Math.Round(occupiedUnitsCount * 100m / unitsCount, 2)
                        : 0m;

                    return new HomeDashboardManagedPropertyItemViewModel
                    {
                        PropertyId = property.Id,
                        PropertyCode = property.Code,
                        PropertyName = property.Name,
                        OwnerName = string.IsNullOrWhiteSpace(property.OwnerName) ? "-" : property.OwnerName,
                        UnitsCount = unitsCount,
                        OccupiedUnitsCount = occupiedUnitsCount,
                        ActiveLeasesCount = propertyLeases.Count,
                        OutstandingReceivables = propertyInvoices.Sum(x => x.TotalAmount - x.AmountPaid),
                        OccupancyRate = occupancyRate
                    };
                })
                .OrderByDescending(x => x.OutstandingReceivables)
                .ThenBy(x => x.PropertyCode)
                .Take(5)
                .ToList();

            try
            {
                model.UnclearedPdcCount = await _db.PdcReceipts
                    .AsNoTracking()
                    .CountAsync(x =>
                        x.Status != "Cleared" &&
                        x.Status != "Cancelled" &&
                        x.Status != "Replaced");

                model.UnclearedPdcAmount = await _db.PdcReceipts
                    .AsNoTracking()
                    .Where(x =>
                        x.Status != "Cleared" &&
                        x.Status != "Cancelled" &&
                        x.Status != "Replaced")
                    .SumAsync(x => (decimal?)x.Amount) ?? 0m;

                model.RecentPdcReceipts = await _db.PdcReceipts
                    .AsNoTracking()
                    .Include(x => x.Lease)
                        .ThenInclude(x => x!.Tenant)
                    .OrderByDescending(x => x.ReceivedDate)
                    .ThenByDescending(x => x.Id)
                    .Take(5)
                    .Select(x => new HomeDashboardPdcItemViewModel
                    {
                        ChequeNumber = x.ChequeNumber,
                        ReceivedDate = x.ReceivedDate,
                        TenantName = x.Lease != null && x.Lease.Tenant != null ? x.Lease.Tenant.FullName : "-",
                        Amount = x.Amount,
                        Status = x.Status,
                        SettlementMode = x.SettlementMode
                    })
                    .ToListAsync();
            }
            catch
            {
                model.PdcDataAvailable = false;
                model.UnclearedPdcCount = 0;
                model.UnclearedPdcAmount = 0m;
                model.RecentPdcReceipts = new List<HomeDashboardPdcItemViewModel>();
            }

            return View(model);
        }

        [HttpGet]
        public IActionResult Privacy()
        {
            return View();
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        private static string BuildLeaseDisplay(Domain.Lease? lease)
        {
            if (lease == null)
                return "-";

            var propertyCode = lease.Unit?.Property?.Code ?? "-";
            var unitNumber = lease.Unit?.UnitNumber ?? "-";
            return $"{lease.ContractNumber} - {propertyCode}/{unitNumber}";
        }

        private async Task<decimal> GetAccountBalanceAsync(Domain.Account account)
        {
            var lines = await _db.JournalEntryLines
                .AsNoTracking()
                .Include(x => x.JournalEntry)
                .Where(x =>
                    x.AccountId == account.Id &&
                    x.JournalEntry != null &&
                    x.JournalEntry.IsPosted)
                .ToListAsync();

            var totalDebit = lines.Sum(x => x.Debit);
            var totalCredit = lines.Sum(x => x.Credit);

            // Use the effective opening balance so that sub-ledger control
            // accounts (AR / AP / Fixed Asset) reflect the per-entity totals.
            var openingBalance = await _openingBalanceService.GetEffectiveOpeningBalanceAsync(account.Id);

            return IsDebitNature(account.AccountType)
                ? openingBalance + totalDebit - totalCredit
                : openingBalance + totalCredit - totalDebit;
        }

        private static bool IsDebitNature(string accountType)
        {
            return accountType == "Asset" || accountType == "Expense";
        }
    

        // The page shown when a request fails on the live site (UseExceptionHandler). The
        // system administrator also sees the cause, so a failure can be traced without
        // the server logs; everyone else sees only the reference.
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            var failure = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
            var model = new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                Path = failure?.Path
            };

            if (failure?.Error != null)
            {
                HttpContext.RequestServices.GetRequiredService<ILogger<HomeController>>()
                    .LogError(failure.Error, "Request to {Path} failed", failure.Path);

                if (User.IsInRole("SystemAdmin"))
                    model.Detail = failure.Error.ToString();
            }

            return View(model);
        }
    }
}
