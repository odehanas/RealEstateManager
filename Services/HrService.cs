using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Employee rules that do not belong to one screen: codes, leave balances, the UAE
    /// end-of-service gratuity, and documents that are about to expire.
    /// </summary>
    public class HrService
    {
        private readonly ApplicationDbContext _db;

        public HrService(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>Next free employee code, EMP-0001, EMP-0002 and so on.</summary>
        public async Task<string> GenerateEmployeeCodeAsync()
        {
            const string prefix = "EMP-";
            var numbers = (await _db.Employees.AsNoTracking()
                    .Where(x => x.EmployeeCode.StartsWith(prefix))
                    .Select(x => x.EmployeeCode)
                    .ToListAsync())
                .Select(x => int.TryParse(x[prefix.Length..], out var n) ? n : 0);

            var next = numbers.DefaultIfEmpty(0).Max() + 1;
            return $"{prefix}{next:D4}";
        }

        // =====================================================================
        //  LEAVE
        // =====================================================================

        /// <summary>
        /// Calendar days between two dates, both included. UAE annual leave is counted in
        /// calendar days; public holidays inside it are a matter for the approver.
        /// </summary>
        public static decimal CountDays(DateTime start, DateTime end) =>
            end.Date < start.Date ? 0 : (decimal)(end.Date - start.Date).TotalDays + 1;

        public sealed record LeaveBalance(decimal Opening, decimal Accrued, decimal Taken, decimal Pending)
        {
            public decimal Available => Opening + Accrued - Taken;
        }

        /// <summary>
        /// Annual leave earned and taken up to a date. Leave accrues month by month at the
        /// employee's yearly entitlement (30 days under UAE law after the first year),
        /// from the opening balance date or the joining date.
        /// </summary>
        public async Task<LeaveBalance> GetAnnualLeaveBalanceAsync(Employee employee, DateTime asOf)
        {
            var from = (employee.OpeningBalanceDate ?? employee.JoiningDate).Date;
            var until = asOf.Date;
            if (employee.LeavingDate.HasValue && employee.LeavingDate.Value.Date < until)
                until = employee.LeavingDate.Value.Date;

            var months = CompletedMonths(from, until);
            var accrued = Math.Round(employee.AnnualLeaveDaysPerYear / 12m * months, 2);

            var leave = await _db.LeaveRequests.AsNoTracking()
                .Where(x => x.EmployeeId == employee.Id && x.LeaveType == LeaveTypes.Annual && x.StartDate >= from)
                .Select(x => new { x.Status, x.Days })
                .ToListAsync();

            return new LeaveBalance(
                employee.OpeningLeaveBalance,
                accrued,
                leave.Where(x => x.Status == LeaveStatuses.Approved).Sum(x => x.Days),
                leave.Where(x => x.Status == LeaveStatuses.Pending).Sum(x => x.Days));
        }

        /// <summary>
        /// Approved unpaid leave days that fall inside a month, for the payroll. Leave that
        /// starts before or ends after the month only counts its days inside it.
        /// </summary>
        public async Task<Dictionary<int, decimal>> GetUnpaidLeaveDaysAsync(DateTime monthStart, DateTime monthEnd)
        {
            var leave = await _db.LeaveRequests.AsNoTracking()
                .Where(x => x.LeaveType == LeaveTypes.Unpaid && x.Status == LeaveStatuses.Approved &&
                            x.StartDate <= monthEnd && x.EndDate >= monthStart)
                .Select(x => new { x.EmployeeId, x.StartDate, x.EndDate })
                .ToListAsync();

            return leave
                .GroupBy(x => x.EmployeeId)
                .ToDictionary(g => g.Key, g => g.Sum(x =>
                    CountDays(x.StartDate < monthStart ? monthStart : x.StartDate, x.EndDate > monthEnd ? monthEnd : x.EndDate)));
        }

        private static int CompletedMonths(DateTime from, DateTime to)
        {
            if (to < from)
                return 0;
            var months = (to.Year - from.Year) * 12 + to.Month - from.Month;
            if (to.Day < from.Day)
                months--;
            return Math.Max(0, months);
        }

        // =====================================================================
        //  END OF SERVICE GRATUITY (UAE Federal Decree-Law No. 33 of 2021)
        // =====================================================================

        public sealed record GratuityResult(
            decimal ServiceYears, decimal DailyBasic, decimal FirstFiveYearsDays, decimal LaterYearsDays,
            decimal Amount, bool Capped, bool Eligible);

        /// <summary>
        /// The gratuity the law gives for a period of service, on the last basic salary:
        /// 21 days' basic pay for each of the first five years, 30 days for each year after,
        /// part years pro rata, and never more than two years' pay. Nothing is due for less
        /// than one year's continuous service (<see cref="GratuityResult.Eligible"/>), but
        /// the full amount is still provided for in the accounts from the first day.
        /// </summary>
        public static GratuityResult CalculateGratuity(decimal basicMonthly, DateTime joining, DateTime asOf)
        {
            var days = Math.Max(0, (asOf.Date - joining.Date).TotalDays + 1);
            var years = (decimal)days / 365m;
            var daily = basicMonthly / 30m;

            var firstDays = Math.Min(years, 5m) * 21m;
            var laterDays = Math.Max(years - 5m, 0m) * 30m;
            var amount = (firstDays + laterDays) * daily;

            var cap = basicMonthly * 24m;
            var capped = amount > cap;
            if (capped)
                amount = cap;

            return new GratuityResult(Math.Round(years, 2), Math.Round(daily, 2), Math.Round(firstDays, 2),
                Math.Round(laterDays, 2), Math.Round(amount, 2), capped, years >= 1m);
        }

        /// <summary>
        /// End-of-service provision already in the accounts for an employee: the opening
        /// figure plus everything approved payroll runs have charged.
        /// </summary>
        public async Task<decimal> GetGratuityProvisionBookedAsync(Employee employee)
        {
            var accrued = await _db.PayrollRunLines.AsNoTracking()
                .Where(x => x.EmployeeId == employee.Id && x.PayrollRun != null &&
                            x.PayrollRun.Status != PayrollRunStatuses.Draft)
                .SumAsync(x => (decimal?)x.GratuityAccrual) ?? 0m;
            return employee.OpeningGratuityProvision + accrued;
        }

        // =====================================================================
        //  DOCUMENT EXPIRY
        // =====================================================================

        public sealed record ExpiringDocument(int EmployeeId, string EmployeeCode, string EmployeeName,
            string Document, string? Number, DateTime Expiry)
        {
            public int DaysLeft => (Expiry.Date - DateTime.Today).Days;
        }

        /// <summary>
        /// Passports, Emirates IDs, visas, labour cards and insurance of current employees
        /// that have expired or expire within the window, soonest first.
        /// </summary>
        public async Task<List<ExpiringDocument>> GetExpiringDocumentsAsync(int withinDays)
        {
            var limit = DateTime.Today.AddDays(withinDays);
            var employees = await _db.Employees.AsNoTracking()
                .Where(x => x.Status == EmployeeStatuses.Active || x.Status == EmployeeStatuses.Suspended)
                .ToListAsync();

            var result = new List<ExpiringDocument>();
            foreach (var e in employees)
            {
                void Check(string doc, string? number, DateTime? expiry)
                {
                    if (expiry.HasValue && expiry.Value.Date <= limit)
                        result.Add(new ExpiringDocument(e.Id, e.EmployeeCode, e.FullName, doc, number, expiry.Value));
                }

                Check("Passport", e.PassportNumber, e.PassportExpiry);
                Check("Emirates ID", e.EmiratesIdNumber, e.EmiratesIdExpiry);
                Check("Residence visa", e.ResidenceVisaNumber, e.ResidenceVisaExpiry);
                Check("Labour card", e.LabourCardNumber, e.LabourCardExpiry);
                Check("Health insurance", e.HealthInsuranceNumber, e.HealthInsuranceExpiry);
                if (e.ContractType == EmployeeContractTypes.Limited)
                    Check("Contract", null, e.ContractEndDate);
            }

            return result.OrderBy(x => x.Expiry).ToList();
        }
    }
}
