using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Business Performance: how the company is doing - results against last year,
    /// collections, receivables, occupancy, cheques and contracts coming due - and what
    /// is waiting for this person's decision. Read only; meant for owners and managers.
    /// </summary>
    public class PerformanceController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly FinancialReportService _reports;
        private readonly PermissionService _permissions;
        private readonly ApprovalAuthorityService _authority;

        public PerformanceController(ApplicationDbContext db, FinancialReportService reports, PermissionService permissions, ApprovalAuthorityService authority)
        {
            _db = db;
            _reports = reports;
            _permissions = permissions;
            _authority = authority;
        }

        public sealed record MonthResult(DateTime Month, decimal Revenue, decimal Expenses)
        {
            public decimal Net => Revenue - Expenses;
        }

        public sealed record Decision(string Kind, string Reference, string Description, decimal? Amount, string By, DateTime At, string Link);

        public sealed class PerformanceViewModel
        {
            public string Currency { get; set; } = "AED";
            public DateTime Today { get; set; }

            public decimal RevenueYtd { get; set; }
            public decimal ExpensesYtd { get; set; }
            public decimal NetYtd => RevenueYtd - ExpensesYtd;
            public decimal RevenueLastYtd { get; set; }
            public decimal NetLastYtd { get; set; }
            public List<MonthResult> Months { get; set; } = new();

            public decimal CashBalance { get; set; }
            public decimal CollectedThisMonth { get; set; }
            public decimal CollectedYtd { get; set; }

            public decimal Receivables { get; set; }
            public decimal Overdue { get; set; }
            public decimal[] Aging { get; set; } = new decimal[4];

            public int UnitsTotal { get; set; }
            public int UnitsOccupied { get; set; }
            public int UnitsVacant { get; set; }
            public decimal RentRoll { get; set; }

            public decimal PdcOnHand { get; set; }
            public decimal PdcDue30 { get; set; }
            public int PdcDue30Count { get; set; }

            public decimal Payables { get; set; }
            public decimal PayablesDue30 { get; set; }

            public List<Lease> Expiring { get; set; } = new();
            public List<Decision> Decisions { get; set; } = new();
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var userId = User.GetAppUserId() ?? 0;
            var today = DateTime.Today;
            var yearStart = new DateTime(today.Year, 1, 1);
            var monthStart = new DateTime(today.Year, today.Month, 1);

            var model = new PerformanceViewModel
            {
                Today = today,
                Currency = await _db.Companies.Where(x => x.Id == companyId).Select(x => x.BaseCurrency).FirstOrDefaultAsync() ?? "AED"
            };

            // ---- results: this year to date against the same days last year, and twelve months
            var ytd = await _reports.GetProfitAndLossAsync(companyId, yearStart, today);
            model.RevenueYtd = ytd.TotalRevenue;
            model.ExpensesYtd = ytd.TotalExpenses;
            var lastYtd = await _reports.GetProfitAndLossAsync(companyId, yearStart.AddYears(-1), today.AddYears(-1));
            model.RevenueLastYtd = lastYtd.TotalRevenue;
            model.NetLastYtd = lastYtd.NetProfitOrLoss;

            for (var i = 11; i >= 0; i--)
            {
                var start = monthStart.AddMonths(-i);
                var end = i == 0 ? today : start.AddMonths(1).AddDays(-1);
                var pl = await _reports.GetProfitAndLossAsync(companyId, start, end);
                model.Months.Add(new MonthResult(start, pl.TotalRevenue, pl.TotalExpenses));
            }

            // ---- cash and collections
            // From the trial balance, so opening balances are included.
            model.CashBalance = (await _reports.GetTrialBalanceAsync(companyId, today)).Rows
                .Where(x => x.AccountCode.StartsWith("111")).Sum(x => x.NetBalance);

            var receipts = _db.CustomerReceipts.AsNoTracking().Where(x => x.JournalEntry != null && x.JournalEntry.IsPosted);
            var cleared = _db.PdcReceipts.AsNoTracking().Where(x => x.Status == "Cleared" && x.ClearedDate != null);
            model.CollectedThisMonth = (await receipts.Where(x => x.ReceiptDate >= monthStart && x.ReceiptDate <= today).SumAsync(x => (decimal?)x.Amount) ?? 0m)
                                       + (await cleared.Where(x => x.ClearedDate >= monthStart && x.ClearedDate <= today).SumAsync(x => (decimal?)x.Amount) ?? 0m);
            model.CollectedYtd = (await receipts.Where(x => x.ReceiptDate >= yearStart && x.ReceiptDate <= today).SumAsync(x => (decimal?)x.Amount) ?? 0m)
                                 + (await cleared.Where(x => x.ClearedDate >= yearStart && x.ClearedDate <= today).SumAsync(x => (decimal?)x.Amount) ?? 0m);

            // ---- receivables, aged by due date
            var open = await _db.CustomerInvoices.AsNoTracking()
                .Where(x => x.Status != "Cancelled" && x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.TotalAmount - x.AmountPaid - x.CreditedAmount > 0)
                .Select(x => new { x.DueDate, Balance = x.TotalAmount - x.AmountPaid - x.CreditedAmount })
                .ToListAsync();
            model.Receivables = open.Sum(x => x.Balance);
            foreach (var invoice in open.Where(x => x.DueDate < today))
            {
                var days = (today - invoice.DueDate.Date).Days;
                model.Overdue += invoice.Balance;
                model.Aging[days <= 30 ? 0 : days <= 60 ? 1 : days <= 90 ? 2 : 3] += invoice.Balance;
            }

            // ---- portfolio
            var units = await _db.Units.AsNoTracking().Where(x => x.IsActive && x.Status != "Sold").Select(x => x.Status).ToListAsync();
            model.UnitsTotal = units.Count;
            model.UnitsOccupied = units.Count(x => x == "Occupied");
            model.UnitsVacant = units.Count(x => x == "Vacant");
            model.RentRoll = await _db.Leases.AsNoTracking().Where(x => x.Status == LeaseStatuses.Active).SumAsync(x => (decimal?)x.AnnualRent) ?? 0m;
            model.Expiring = await _db.Leases.AsNoTracking().Include(x => x.Tenant).Include(x => x.Unit).ThenInclude(x => x!.Property)
                .Where(x => x.Status == LeaseStatuses.Active && x.EndDate >= today && x.EndDate <= today.AddDays(90))
                .OrderBy(x => x.EndDate).Take(15).ToListAsync();

            // ---- cheques on hand and bills to pay
            var onHand = _db.PdcReceipts.AsNoTracking().Where(x => x.Status == "Received");
            model.PdcOnHand = await onHand.SumAsync(x => (decimal?)x.Amount) ?? 0m;
            model.PdcDue30 = await onHand.Where(x => x.ChequeDate <= today.AddDays(30)).SumAsync(x => (decimal?)x.Amount) ?? 0m;
            model.PdcDue30Count = await onHand.CountAsync(x => x.ChequeDate <= today.AddDays(30));
            var bills = _db.VendorBills.AsNoTracking().Where(x => x.Status != "Draft" && x.Status != "Cancelled" && x.TotalAmount - x.AmountPaid > 0);
            model.Payables = await bills.SumAsync(x => (decimal?)(x.TotalAmount - x.AmountPaid)) ?? 0m;
            model.PayablesDue30 = await bills.Where(x => x.DueDate <= today.AddDays(30)).SumAsync(x => (decimal?)(x.TotalAmount - x.AmountPaid)) ?? 0m;

            // ---- waiting for this person's decision
            if (await _permissions.HasPermissionAsync(User, AppPermissions.LeasesApprove))
            {
                var contracts = await _db.ApprovalRequests.AsNoTracking()
                    .Where(x => x.Status == ApprovalStatuses.Pending && x.RequestedByAppUserId != userId &&
                                (x.DocumentCreatedByAppUserId == null || x.DocumentCreatedByAppUserId != userId))
                    .OrderBy(x => x.RequestedAt).Take(20).ToListAsync();
                model.Decisions.AddRange(contracts.Select(x => new Decision("Contract", x.DocumentNumber, x.Summary, null, x.RequestedByName, x.RequestedAt, $"/Approvals/Review/{x.Id}")));
            }

            if (await _permissions.HasAnyPermissionAsync(User, AppPermissions.PaymentsApprove, AppPermissions.OwnerOversight))
            {
                var authority = await _authority.GetAuthorityAsync(userId, companyId, User.IsInRole("SystemAdmin"));
                var payments = await _db.PaymentApprovals.AsNoTracking()
                    .Where(x => x.Status == PaymentApprovalStatuses.Pending && x.RequestedByAppUserId != userId)
                    .OrderBy(x => x.RequestedAt).Take(30).ToListAsync();
                model.Decisions.AddRange(payments.Where(x => authority != null && authority.Covers(x.Amount))
                    .Select(x => new Decision("Payment", x.SourceType, x.Description, x.Amount, x.RequestedByName, x.RequestedAt, "/PaymentApprovals")));
            }

            if (await _permissions.HasPermissionAsync(User, AppPermissions.PayrollApprove))
            {
                var runs = await _db.PayrollRuns.AsNoTracking()
                    .Where(x => x.Status == PayrollRunStatuses.Draft && x.PreparedByAppUserId != userId)
                    .OrderBy(x => x.PeriodStart).Take(5).ToListAsync();
                model.Decisions.AddRange(runs.Select(x => new Decision("Payroll", x.RunNumber, $"Payroll {x.PeriodStart:MMMM yyyy}", x.TotalNet, x.PreparedByName ?? "", x.PreparedAt, $"/Payroll/Run/{x.Id}")));
            }

            if (await _permissions.HasPermissionAsync(User, AppPermissions.PeriodReopenApprove))
            {
                var reopen = await _db.PeriodReopenRequests.AsNoTracking().Include(x => x.AccountingPeriod)
                    .Where(x => x.Status == PeriodReopenStatuses.Pending && x.RequestedByAppUserId != userId)
                    .OrderBy(x => x.RequestedAt).Take(10).ToListAsync();
                model.Decisions.AddRange(reopen.Select(x => new Decision("Reopen period", x.AccountingPeriod?.Name ?? "", x.Reason, null, x.RequestedByName, x.RequestedAt, "/Closing/ReopenRequests")));
            }

            return View(model);
        }
    }
}
