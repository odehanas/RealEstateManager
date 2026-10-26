using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The monthly payroll.
    ///
    /// PREPARE. A run is created for a month and calculated for every employee employed
    /// in it: fixed pay pro rata to the days paid (joining or leaving mid-month, unpaid
    /// leave), overtime at the company's multiplier on the basic hourly rate, bonuses,
    /// salary-advance instalments and other deductions. While it is a draft every line can
    /// be adjusted and the run recalculated.
    ///
    /// APPROVE. A second person approves it: one journal charges the salary cost (per
    /// building, for staff assigned to one) against salaries payable, recovers advances,
    /// and provides for end-of-service gratuity under UAE law.
    ///
    /// PAY. Salaries are paid in batches by method: a WPS salary information file (SIF),
    /// a bank transfer list, or printed cheques. Each batch clears salaries payable
    /// against the bank account it is paid from.
    /// </summary>
    public class PayrollService
    {
        public const string RunSourceType = "PayrollRun";
        public const string PaymentSourceType = "PayrollPayment";
        public const string AdvanceSourceType = "EmployeeAdvance";

        private readonly ApplicationDbContext _db;
        private readonly HrService _hr;

        public PayrollService(ApplicationDbContext db, HrService hr)
        {
            _db = db;
            _hr = hr;
        }

        // =====================================================================
        //  SETTINGS
        // =====================================================================

        /// <summary>The company's payroll settings, created with the standard accounts on first use.</summary>
        public async Task<PayrollSettings> GetSettingsAsync(int companyId)
        {
            var settings = await _db.PayrollSettings.FirstOrDefaultAsync(x => x.CompanyId == companyId);
            if (settings != null)
                return settings;

            var accounts = await _db.Accounts.AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .Select(x => new { x.Id, x.Code })
                .ToDictionaryAsync(x => x.Code, x => x.Id);
            int? Find(string code) => accounts.TryGetValue(code, out var id) ? id : null;

            settings = new PayrollSettings
            {
                CompanyId = companyId,
                BasicSalaryExpenseAccountId = Find("64100"),
                AllowancesExpenseAccountId = Find("64200"),
                OvertimeBonusExpenseAccountId = Find("64300"),
                GratuityExpenseAccountId = Find("64400"),
                SalariesPayableAccountId = Find("21700"),
                GratuityProvisionAccountId = Find("21800"),
                EmployeeAdvancesAccountId = Find("11600"),
                DefaultBankAccountId = Find("11100")
            };
            _db.PayrollSettings.Add(settings);
            await _db.SaveChangesAsync();
            return settings;
        }

        // =====================================================================
        //  PREPARE
        // =====================================================================

        public async Task<(bool Success, string Message, int? RunId)> CreateRunAsync(
            int companyId, int year, int month, int userId, string userName)
        {
            if (month is < 1 or > 12)
                return (false, "Choose a month.", null);

            if (await _db.PayrollRuns.AnyAsync(x => x.CompanyId == companyId && x.Year == year && x.Month == month))
                return (false, $"The payroll for {MonthName(year, month)} already exists.", null);

            var start = new DateTime(year, month, 1);
            var run = new PayrollRun
            {
                CompanyId = companyId,
                RunNumber = $"PR-{year}-{month:D2}",
                Year = year,
                Month = month,
                PeriodStart = start,
                PeriodEnd = start.AddMonths(1).AddDays(-1),
                Status = PayrollRunStatuses.Draft,
                PreparedByAppUserId = userId,
                PreparedByName = Limit(userName, 200),
                PreparedAt = DateTime.UtcNow
            };
            _db.PayrollRuns.Add(run);
            await _db.SaveChangesAsync();

            var count = await CalculateAsync(run, keepAdjustments: false);
            return (true, $"Payroll {run.RunNumber} prepared for {count} employee(s). Review it, then send it for approval.", run.Id);
        }

        /// <summary>
        /// Recalculates a draft run from the current employee records: new joiners are
        /// added, leavers dropped, salaries and unpaid leave refreshed. Overtime hours,
        /// bonuses and deductions already entered are kept.
        /// </summary>
        public async Task<(bool Success, string Message)> RecalculateAsync(int runId)
        {
            var run = await _db.PayrollRuns.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == runId);
            if (run == null)
                return (false, "The payroll run was not found.");
            if (run.Status != PayrollRunStatuses.Draft)
                return (false, "Only a draft payroll can be recalculated.");

            var count = await CalculateAsync(run, keepAdjustments: true);
            return (true, $"Recalculated for {count} employee(s).");
        }

        private async Task<int> CalculateAsync(PayrollRun run, bool keepAdjustments)
        {
            var settings = await GetSettingsAsync(run.CompanyId);
            await _db.Entry(run).Collection(x => x.Lines).LoadAsync();

            var employees = await _db.Employees
                .Include(x => x.Department)
                .Where(x => x.CompanyId == run.CompanyId &&
                            x.Status != EmployeeStatuses.Suspended &&
                            x.JoiningDate <= run.PeriodEnd &&
                            (x.LeavingDate == null || x.LeavingDate >= run.PeriodStart))
                .OrderBy(x => x.EmployeeCode)
                .ToListAsync();

            var unpaid = await _hr.GetUnpaidLeaveDaysAsync(run.PeriodStart, run.PeriodEnd);
            var advances = await _db.EmployeeAdvances.AsNoTracking()
                .Where(x => x.CompanyId == run.CompanyId && x.Amount > x.RecoveredAmount)
                .ToListAsync();

            // Approved transactions this payroll takes: everything dated up to its month end
            // that no other payroll has taken. They are reserved for this run while it is a
            // draft, so a later month cannot take them too, and released if it is deleted.
            var employeeIds = employees.Select(x => x.Id).ToList();
            var transactions = await _db.PayrollAdjustments
                .Where(x => x.CompanyId == run.CompanyId &&
                            (x.PayrollRunId == run.Id ||
                             (x.PayrollRunId == null && x.Status == PayrollAdjustmentStatuses.Approved && x.Date <= run.PeriodEnd)))
                .ToListAsync();
            foreach (var t in transactions)
                t.PayrollRunId = t.Status == PayrollAdjustmentStatuses.Approved && employeeIds.Contains(t.EmployeeId) ? run.Id : null;

            // Drop lines for people no longer in the run.
            var ids = employees.Select(x => x.Id).ToHashSet();
            foreach (var stale in run.Lines.Where(x => !ids.Contains(x.EmployeeId)).ToList())
            {
                run.Lines.Remove(stale);
                _db.PayrollRunLines.Remove(stale);
            }

            foreach (var employee in employees)
            {
                var line = run.Lines.FirstOrDefault(x => x.EmployeeId == employee.Id);
                var isNew = line == null;
                line ??= new PayrollRunLine { EmployeeId = employee.Id };

                var outstanding = advances.Where(a => a.EmployeeId == employee.Id).ToList();
                var suggestedRecovery = outstanding.Sum(a => Math.Min(a.MonthlyInstalment, a.Outstanding));

                if (isNew || !keepAdjustments)
                    line.AdvanceRecovery = suggestedRecovery;

                line.UnpaidLeaveDays = unpaid.TryGetValue(employee.Id, out var days) ? days : 0m;
                ApplyTransactions(line, employee, settings, transactions.Where(t => t.PayrollRunId == run.Id && t.EmployeeId == employee.Id));
                Fill(line, employee, run, settings);
                line.AdvanceRecovery = Math.Min(line.AdvanceRecovery, Math.Max(0m, line.Gross - line.OtherDeductions));
                line.GratuityAccrual = await GratuityAccrualAsync(employee, run, settings);

                if (isNew)
                    run.Lines.Add(line);
            }

            UpdateTotals(run);
            await _db.SaveChangesAsync();
            return run.Lines.Count;
        }

        /// <summary>Copies the employee's details in and works out the pay for the line's days and adjustments.</summary>
        private static void Fill(PayrollRunLine line, Employee employee, PayrollRun run, PayrollSettings settings)
        {
            line.EmployeeCode = employee.EmployeeCode;
            line.EmployeeName = Limit(employee.FullName, 200);
            line.JobTitle = employee.JobTitle;
            line.DepartmentName = employee.Department?.Name;
            line.PropertyId = employee.PropertyId;
            line.PaymentMethod = employee.PaymentMethod;
            line.BankName = employee.BankName;
            line.Iban = employee.Iban;
            line.BankRoutingCode = employee.BankRoutingCode;
            line.MohrePersonCode = employee.MohrePersonCode;

            line.DaysInMonth = DateTime.DaysInMonth(run.Year, run.Month);
            var from = employee.JoiningDate.Date > run.PeriodStart ? employee.JoiningDate.Date : run.PeriodStart;
            var to = employee.LeavingDate.HasValue && employee.LeavingDate.Value.Date < run.PeriodEnd ? employee.LeavingDate.Value.Date : run.PeriodEnd;
            line.DaysEmployed = (int)HrService.CountDays(from, to);
            line.UnpaidLeaveDays = Math.Min(line.UnpaidLeaveDays, line.DaysEmployed);
            line.AbsenceDays = Math.Min(line.AbsenceDays, line.DaysEmployed - line.UnpaidLeaveDays);

            var factor = line.DaysInMonth == 0 ? 0m : line.DaysPaid / line.DaysInMonth;
            line.FullBasic = employee.BasicSalary;
            line.FullAllowances = employee.HousingAllowance + employee.TransportAllowance + employee.OtherAllowance;
            line.Basic = Round(employee.BasicSalary * factor);
            line.Housing = Round(employee.HousingAllowance * factor);
            line.Transport = Round(employee.TransportAllowance * factor);
            line.OtherAllowance = Round(employee.OtherAllowance * factor);
        }

        /// <summary>The basic hourly wage: overtime and late time are priced on it under UAE law.</summary>
        public static decimal HourlyBasic(Employee employee, PayrollSettings settings) =>
            settings.WorkingHoursPerDay <= 0 ? 0m : employee.BasicSalary / 30m / settings.WorkingHoursPerDay;

        /// <summary>
        /// Prices one transaction for an employee: hours of overtime at the overtime rate on
        /// the basic hourly wage, late minutes at that wage, money as entered. Absence days
        /// are not priced here - they reduce the days paid instead.
        /// </summary>
        public static decimal PriceTransaction(PayrollAdjustment t, Employee employee, PayrollSettings settings)
        {
            var hourly = HourlyBasic(employee, settings);
            return t.Category switch
            {
                PayrollAdjustmentCategories.Overtime => Round(t.Quantity * hourly * settings.OvertimeMultiplier),
                PayrollAdjustmentCategories.RestDayOvertime => Round(t.Quantity * hourly * settings.RestDayOvertimeMultiplier),
                PayrollAdjustmentCategories.LateArrival => Round(t.Quantity / 60m * hourly),
                PayrollAdjustmentCategories.UnauthorisedAbsence => Round(t.Quantity * employee.MonthlyGross / 30m),
                _ => t.Amount
            };
        }

        /// <summary>Totals an employee's transactions onto their payroll line.</summary>
        private static void ApplyTransactions(PayrollRunLine line, Employee employee, PayrollSettings settings, IEnumerable<PayrollAdjustment> transactions)
        {
            var list = transactions.ToList();
            bool Is(PayrollAdjustment t, params string[] categories) => categories.Contains(t.Category);

            line.OvertimeHours = list.Where(t => Is(t, PayrollAdjustmentCategories.Overtime, PayrollAdjustmentCategories.RestDayOvertime)).Sum(t => t.Quantity);
            line.OvertimeAmount = list.Where(t => Is(t, PayrollAdjustmentCategories.Overtime, PayrollAdjustmentCategories.RestDayOvertime))
                .Sum(t => PriceTransaction(t, employee, settings));
            line.Bonus = list.Where(t => Is(t, PayrollAdjustmentCategories.Bonus, PayrollAdjustmentCategories.Commission, PayrollAdjustmentCategories.OtherAddition))
                .Sum(t => t.Amount);
            line.AbsenceDays = list.Where(t => Is(t, PayrollAdjustmentCategories.UnauthorisedAbsence)).Sum(t => t.Quantity);

            var deductions = list.Where(t => !PayrollAdjustmentCategories.Get(t.Category).IsAddition &&
                                             t.Category != PayrollAdjustmentCategories.UnauthorisedAbsence).ToList();
            line.OtherDeductions = deductions.Sum(t => PriceTransaction(t, employee, settings));
            line.DeductionNote = deductions.Count == 0
                ? null
                : Limit(string.Join(", ", deductions.GroupBy(t => PayrollAdjustmentCategories.Get(t.Category).Name)
                    .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key)), 200);
        }

        /// <summary>
        /// The end-of-service provision this month: what the law would pay at the month end
        /// on the current basic salary, less what is already provided for. A pay cut can
        /// make it negative, which releases part of the provision.
        /// </summary>
        private async Task<decimal> GratuityAccrualAsync(Employee employee, PayrollRun run, PayrollSettings settings)
        {
            if (!settings.AccrueGratuity)
                return 0m;

            var asOf = employee.LeavingDate.HasValue && employee.LeavingDate.Value.Date < run.PeriodEnd
                ? employee.LeavingDate.Value.Date
                : run.PeriodEnd;
            var target = HrService.CalculateGratuity(employee.BasicSalary, employee.JoiningDate, asOf).Amount;

            var bookedElsewhere = employee.OpeningGratuityProvision + (await _db.PayrollRunLines.AsNoTracking()
                .Where(x => x.EmployeeId == employee.Id && x.PayrollRunId != run.Id && x.PayrollRun != null &&
                            x.PayrollRun.Status != PayrollRunStatuses.Draft)
                .SumAsync(x => (decimal?)x.GratuityAccrual) ?? 0m);

            return Round(target - bookedElsewhere);
        }

        /// <summary>
        /// Recalculates every draft payroll, oldest first, after transactions were approved,
        /// changed or removed - so a draft always shows what it would pay. Returns the runs touched.
        /// </summary>
        public async Task<List<string>> RefreshDraftRunsAsync(int companyId)
        {
            var drafts = await _db.PayrollRuns.Include(x => x.Lines)
                .Where(x => x.CompanyId == companyId && x.Status == PayrollRunStatuses.Draft)
                .OrderBy(x => x.PeriodEnd)
                .ToListAsync();

            foreach (var run in drafts)
                await CalculateAsync(run, keepAdjustments: true);

            return drafts.Select(x => x.RunNumber).ToList();
        }

        /// <summary>
        /// The one figure set on the payroll line itself: how much of the employee's salary
        /// advances to recover this month. Everything else comes from leave and payroll
        /// transactions.
        /// </summary>
        public async Task<(bool Success, string Message)> UpdateAdvanceRecoveryAsync(int lineId, decimal advanceRecovery)
        {
            var line = await _db.PayrollRunLines.Include(x => x.PayrollRun).FirstOrDefaultAsync(x => x.Id == lineId);
            if (line?.PayrollRun == null)
                return (false, "The payroll line was not found.");
            if (line.PayrollRun.Status != PayrollRunStatuses.Draft)
                return (false, "This payroll is approved. Return it to draft to change it.");
            if (advanceRecovery < 0)
                return (false, "The recovery cannot be negative.");

            var employee = await _db.Employees.Include(x => x.Department).FirstAsync(x => x.Id == line.EmployeeId);

            var outstanding = await _db.EmployeeAdvances.AsNoTracking()
                .Where(x => x.EmployeeId == line.EmployeeId)
                .SumAsync(x => (decimal?)(x.Amount - x.RecoveredAmount)) ?? 0m;
            if (advanceRecovery > outstanding)
                return (false, $"{employee.FullName} owes {outstanding:N2} in advances; more cannot be recovered.");

            line.AdvanceRecovery = Round(advanceRecovery);
            if (line.Net < 0)
                return (false, $"Recovering {advanceRecovery:N2} would make {employee.FullName}'s net salary negative ({line.Net:N2}).");

            await _db.Entry(line.PayrollRun).Collection(x => x.Lines).LoadAsync();
            UpdateTotals(line.PayrollRun);
            await _db.SaveChangesAsync();
            return (true, $"{employee.FullName}: net salary {line.Net:N2}.");
        }

        public async Task<(bool Success, string Message)> DeleteRunAsync(int runId)
        {
            var run = await _db.PayrollRuns.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == runId);
            if (run == null)
                return (false, "The payroll run was not found.");
            if (run.Status != PayrollRunStatuses.Draft)
                return (false, "Only a draft payroll can be deleted.");

            // Its transactions go back to waiting for the next payroll.
            foreach (var t in await _db.PayrollAdjustments.Where(x => x.PayrollRunId == run.Id).ToListAsync())
                t.PayrollRunId = null;

            _db.PayrollRunLines.RemoveRange(run.Lines);
            _db.PayrollRuns.Remove(run);
            await _db.SaveChangesAsync();
            return (true, $"Payroll {run.RunNumber} deleted.");
        }

        private static void UpdateTotals(PayrollRun run)
        {
            run.TotalGross = run.Lines.Sum(x => x.Gross);
            run.TotalDeductions = run.Lines.Sum(x => x.TotalDeductions);
            run.TotalNet = run.Lines.Sum(x => x.Net);
            run.TotalGratuityAccrual = run.Lines.Sum(x => x.GratuityAccrual);
        }

        // =====================================================================
        //  APPROVE
        // =====================================================================

        public async Task<int> CountApproversAsync(int companyId) =>
            await _db.AppUserPermissions.IgnoreQueryFilters()
                .Where(p => p.PermissionKey == AppPermissions.PayrollApprove &&
                            _db.AppUserCompanies.IgnoreQueryFilters().Any(c => c.AppUserId == p.AppUserId && c.CompanyId == companyId))
                .Select(p => p.AppUserId).Distinct().CountAsync();

        public async Task<(bool Success, string Message)> ApproveAsync(int runId, int userId, string userName, bool isSystemAdmin)
        {
            var run = await _db.PayrollRuns.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == runId);
            if (run == null)
                return (false, "The payroll run was not found.");
            if (run.Status != PayrollRunStatuses.Draft)
                return (false, $"Payroll {run.RunNumber} is already approved.");
            if (run.Lines.Count == 0)
                return (false, "The payroll has no employees.");

            if (run.PreparedByAppUserId == userId && !isSystemAdmin && await CountApproversAsync(run.CompanyId) > 1)
                return (false, "You prepared this payroll, so another approver has to approve it.");

            var negative = run.Lines.FirstOrDefault(x => x.Net < 0);
            if (negative != null)
                return (false, $"{negative.EmployeeName}'s net salary is negative. Correct the deductions first.");

            var settings = await GetSettingsAsync(run.CompanyId);
            var accounts = await RequireAccountsAsync(settings,
                ("Basic salaries", settings.BasicSalaryExpenseAccountId),
                ("Allowances", settings.AllowancesExpenseAccountId),
                ("Overtime and bonuses", settings.OvertimeBonusExpenseAccountId),
                ("Salaries payable", settings.SalariesPayableAccountId),
                ("Employee advances", settings.EmployeeAdvancesAccountId),
                ("End of service expense", settings.GratuityExpenseAccountId),
                ("End of service provision", settings.GratuityProvisionAccountId));
            if (accounts.Problem != null)
                return (false, accounts.Problem);

            var lines = new List<JournalEntryLine>();
            void Add(int accountId, decimal debit, decimal credit, int? propertyId, string description)
            {
                // A negative amount (a gratuity release) belongs on the other side.
                if (debit < 0m) { credit -= debit; debit = 0m; }
                if (credit < 0m) { debit -= credit; credit = 0m; }
                if (debit == 0m && credit == 0m)
                    return;
                lines.Add(new JournalEntryLine { AccountId = accountId, Debit = debit, Credit = credit, PropertyId = propertyId, Description = description });
            }

            var month = MonthName(run.Year, run.Month);
            foreach (var g in run.Lines.GroupBy(x => x.PropertyId))
            {
                Add(settings.BasicSalaryExpenseAccountId!.Value, g.Sum(x => x.Basic), 0m, g.Key, $"Basic salaries {month}");
                Add(settings.AllowancesExpenseAccountId!.Value, g.Sum(x => x.Allowances), 0m, g.Key, $"Allowances {month}");
                Add(settings.OvertimeBonusExpenseAccountId!.Value, g.Sum(x => x.OvertimeAmount + x.Bonus), 0m, g.Key, $"Overtime and bonuses {month}");
                Add(settings.BasicSalaryExpenseAccountId!.Value, 0m, g.Sum(x => x.OtherDeductions), g.Key, $"Salary deductions {month}");
                Add(settings.GratuityExpenseAccountId!.Value, g.Sum(x => x.GratuityAccrual), 0m, g.Key, $"End of service provision {month}");
            }

            Add(settings.SalariesPayableAccountId!.Value, 0m, run.Lines.Sum(x => x.Net), null, $"Net salaries payable {month}");
            Add(settings.EmployeeAdvancesAccountId!.Value, 0m, run.Lines.Sum(x => x.AdvanceRecovery), null, $"Advances recovered {month}");
            Add(settings.GratuityProvisionAccountId!.Value, 0m, run.Lines.Sum(x => x.GratuityAccrual), null, $"End of service provision {month}");

            await using var transaction = await _db.Database.BeginTransactionAsync();

            var (journal, problem) = await PostJournalAsync(run.CompanyId, AccrualDate(run, DateTime.Today), $"Payroll {month}", RunSourceType, run.Id,
                lines, run.RunNumber, userName);
            if (problem != null)
                return (false, problem);

            await ApplyAdvanceRecoveriesAsync(run, recover: true);

            run.JournalEntryId = journal!.Id;
            run.Status = PayrollRunStatuses.Approved;
            run.ApprovedByName = Limit(userName, 200);
            run.ApprovedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return (true, $"Payroll {run.RunNumber} approved: net salaries of {run.TotalNet:N2} are now payable.");
        }

        /// <summary>
        /// The date the salary cost is posted on: the day the payroll is approved when that
        /// falls inside its month - salaries in the UAE are often paid before the month end,
        /// and the payment must not reach the ledger before the cost it settles - otherwise
        /// the month end. The cost always lands in the payroll's own month.
        /// </summary>
        public static DateTime AccrualDate(PayrollRun run, DateTime approvedOn)
        {
            var day = approvedOn.Date;
            if (day > run.PeriodEnd)
                return run.PeriodEnd;
            return day < run.PeriodStart ? run.PeriodStart : day;
        }

        /// <summary>
        /// Takes an approved payroll back to draft, for a correction, provided nothing has
        /// been paid yet and its month is still open.
        /// </summary>
        public async Task<(bool Success, string Message)> ReturnToDraftAsync(int runId)
        {
            var run = await _db.PayrollRuns.Include(x => x.Lines).Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == runId);
            if (run == null)
                return (false, "The payroll run was not found.");
            if (run.Status == PayrollRunStatuses.Draft)
                return (false, "This payroll is already a draft.");
            if (run.Payments.Count > 0)
                return (false, "Salaries have been paid from this payroll. Cancel those payments first.");

            await using var transaction = await _db.Database.BeginTransactionAsync();
            if (run.JournalEntryId.HasValue)
            {
                var journal = await _db.JournalEntries.Include(x => x.Lines).FirstAsync(x => x.Id == run.JournalEntryId.Value);
                run.JournalEntryId = null;
                _db.JournalEntryLines.RemoveRange(journal.Lines);
                _db.JournalEntries.Remove(journal);
            }

            await ApplyAdvanceRecoveriesAsync(run, recover: false);
            run.Status = PayrollRunStatuses.Draft;
            run.ApprovedByName = null;
            run.ApprovedAt = null;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (ClosedPeriodException ex)
            {
                return (false, ex.Message);
            }

            await transaction.CommitAsync();
            return (true, $"Payroll {run.RunNumber} is back in draft and out of the accounts.");
        }

        /// <summary>
        /// Moves each line's advance recovery onto the oldest advances first, or - when the
        /// payroll goes back to draft - back off the most recent ones first.
        /// </summary>
        private async Task ApplyAdvanceRecoveriesAsync(PayrollRun run, bool recover)
        {
            foreach (var line in run.Lines.Where(x => x.AdvanceRecovery > 0))
            {
                var advances = await _db.EmployeeAdvances
                    .Where(x => x.EmployeeId == line.EmployeeId)
                    .OrderBy(x => x.AdvanceDate).ThenBy(x => x.Id)
                    .ToListAsync();
                if (!recover)
                    advances.Reverse();

                var left = line.AdvanceRecovery;
                foreach (var advance in advances)
                {
                    if (left <= 0)
                        break;
                    var take = recover ? Math.Min(left, advance.Amount - advance.RecoveredAmount) : Math.Min(left, advance.RecoveredAmount);
                    advance.RecoveredAmount += recover ? take : -take;
                    left -= take;
                }
            }
        }

        // =====================================================================
        //  PAY
        // =====================================================================

        public sealed record PaymentRequest(
            string Method, DateTime PaymentDate, int BankAccountId, string? Reference, string? FirstChequeNumber, List<int>? LineIds);

        public async Task<(bool Success, string Message, int? PaymentId)> PayAsync(int runId, PaymentRequest request, string userName)
        {
            var run = await _db.PayrollRuns.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == runId);
            if (run == null)
                return (false, "The payroll run was not found.", null);
            if (run.Status == PayrollRunStatuses.Draft)
                return (false, "Approve the payroll before paying it.", null);
            if (!SalaryPaymentMethods.All.Contains(request.Method))
                return (false, "Choose a payment method.", null);

            // How and where to pay is taken from the employee record as it is today, so a
            // corrected IBAN or MOHRE code does not need the payroll to be redone. The
            // amounts stay as approved.
            var unpaidIds = run.Lines.Where(x => !x.IsPaid).Select(x => x.EmployeeId).ToList();
            var current = await _db.Employees.AsNoTracking().Where(x => unpaidIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            foreach (var line in run.Lines.Where(x => !x.IsPaid && current.ContainsKey(x.EmployeeId)))
            {
                var e = current[line.EmployeeId];
                line.PaymentMethod = e.PaymentMethod;
                line.BankName = e.BankName;
                line.Iban = e.Iban;
                line.BankRoutingCode = e.BankRoutingCode;
                line.MohrePersonCode = e.MohrePersonCode;
            }

            var lines = run.Lines
                .Where(x => !x.IsPaid && x.PaymentMethod == request.Method && x.Net > 0 &&
                            (request.LineIds == null || request.LineIds.Count == 0 || request.LineIds.Contains(x.Id)))
                .OrderBy(x => x.EmployeeCode)
                .ToList();
            if (lines.Count == 0)
                return (false, $"Nobody is left to pay by {SalaryPaymentMethods.Describe(request.Method)} in this payroll.", null);

            var settings = await GetSettingsAsync(run.CompanyId);
            var bank = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == request.BankAccountId);
            if (bank == null || !bank.IsPostable || !bank.IsActive || bank.AccountType != "Asset")
                return (false, "Choose the bank account the salaries are paid from.", null);

            var payable = await RequireAccountsAsync(settings, ("Salaries payable", settings.SalariesPayableAccountId));
            if (payable.Problem != null)
                return (false, payable.Problem, null);

            // Salaries cannot be paid in the ledger before the payroll put them there.
            var costDate = run.JournalEntryId.HasValue
                ? await _db.JournalEntries.Where(x => x.Id == run.JournalEntryId.Value).Select(x => (DateTime?)x.EntryDate).FirstOrDefaultAsync()
                : null;
            if (costDate.HasValue && request.PaymentDate.Date < costDate.Value.Date)
                return (false, $"The payroll was posted on {costDate.Value:dd/MM/yyyy}; the payment date cannot be earlier.", null);

            var payment = new PayrollPayment
            {
                CompanyId = run.CompanyId,
                PayrollRunId = run.Id,
                Method = request.Method,
                PaymentDate = request.PaymentDate.Date,
                BankAccountId = bank.Id,
                Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : Limit(request.Reference.Trim(), 100),
                TotalAmount = lines.Sum(x => x.Net),
                EmployeeCount = lines.Count,
                CreatedByName = Limit(userName, 200),
                CreatedAt = DateTime.UtcNow
            };

            switch (request.Method)
            {
                case SalaryPaymentMethods.Wps:
                {
                    var problem = CheckWps(settings, lines);
                    if (problem != null)
                        return (false, problem, null);
                    (payment.FileName, payment.FileContent) = BuildSif(settings, run, lines, DateTime.Now);
                    payment.Reference ??= payment.FileName;
                    break;
                }

                case SalaryPaymentMethods.BankTransfer:
                {
                    var missing = lines.Where(x => string.IsNullOrWhiteSpace(x.Iban)).Select(x => x.EmployeeName).ToList();
                    if (missing.Count > 0)
                        return (false, $"No IBAN on file for: {string.Join(", ", missing)}.", null);
                    payment.FileName = $"{run.RunNumber}-bank-transfer.csv";
                    payment.FileContent = BuildTransferCsv(run, lines);
                    break;
                }

                case SalaryPaymentMethods.Cheque:
                {
                    var numbers = ChequeNumbers(request.FirstChequeNumber, lines.Count);
                    if (numbers == null)
                        return (false, "Enter the number of the first cheque, e.g. 000451.", null);
                    for (var i = 0; i < lines.Count; i++)
                        lines[i].ChequeNumber = numbers[i];
                    payment.Reference ??= lines.Count == 1 ? $"Cheque {numbers[0]}" : $"Cheques {numbers[0]}-{numbers[^1]}";
                    break;
                }
            }

            await using var transaction = await _db.Database.BeginTransactionAsync();

            _db.PayrollPayments.Add(payment);
            await _db.SaveChangesAsync();

            var journalLines = new List<JournalEntryLine>
            {
                new() { AccountId = settings.SalariesPayableAccountId!.Value, Debit = payment.TotalAmount, Credit = 0m,
                        Description = $"Salaries paid by {SalaryPaymentMethods.Describe(payment.Method)} - {MonthName(run.Year, run.Month)}" },
                new() { AccountId = bank.Id, Debit = 0m, Credit = payment.TotalAmount,
                        Description = $"{payment.Reference} ({payment.EmployeeCount} employee(s))" }
            };

            var (journal, journalProblem) = await PostJournalAsync(run.CompanyId, payment.PaymentDate,
                $"Salary payment {run.RunNumber} - {SalaryPaymentMethods.Describe(payment.Method)}", PaymentSourceType, payment.Id,
                journalLines, payment.Reference, userName);
            if (journalProblem != null)
                return (false, journalProblem, null);

            payment.JournalEntryId = journal!.Id;
            foreach (var line in lines)
                line.PayrollPaymentId = payment.Id;
            run.Status = run.Lines.All(x => x.IsPaid || x.Net <= 0) ? PayrollRunStatuses.Paid : PayrollRunStatuses.PartlyPaid;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            var what = payment.Method switch
            {
                SalaryPaymentMethods.Wps => $"WPS salary file {payment.FileName} is ready to upload to your bank.",
                SalaryPaymentMethods.BankTransfer => "The transfer list and the letter to the bank are ready.",
                _ => $"{lines.Count} cheque(s) numbered {payment.Reference} are ready to print."
            };
            return (true, $"Salaries of {payment.TotalAmount:N2} for {payment.EmployeeCount} employee(s) recorded as paid. {what}", payment.Id);
        }

        /// <summary>
        /// Undoes a payment batch - a rejected WPS file, a spoilt cheque run - while its
        /// month is open. The salaries are payable again.
        /// </summary>
        public async Task<(bool Success, string Message)> CancelPaymentAsync(int paymentId)
        {
            var payment = await _db.PayrollPayments.Include(x => x.Lines).Include(x => x.PayrollRun)
                .FirstOrDefaultAsync(x => x.Id == paymentId);
            if (payment == null)
                return (false, "The payment was not found.");

            await using var transaction = await _db.Database.BeginTransactionAsync();
            if (payment.JournalEntryId.HasValue)
            {
                var journal = await _db.JournalEntries.Include(x => x.Lines).FirstAsync(x => x.Id == payment.JournalEntryId.Value);
                payment.JournalEntryId = null;
                _db.JournalEntryLines.RemoveRange(journal.Lines);
                _db.JournalEntries.Remove(journal);
            }

            foreach (var line in payment.Lines)
            {
                line.PayrollPaymentId = null;
                line.ChequeNumber = null;
            }

            var run = payment.PayrollRun!;
            _db.PayrollPayments.Remove(payment);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (ClosedPeriodException ex)
            {
                return (false, ex.Message);
            }

            run.Status = await _db.PayrollPayments.AnyAsync(x => x.PayrollRunId == run.Id)
                ? PayrollRunStatuses.PartlyPaid
                : PayrollRunStatuses.Approved;
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return (true, "The payment is cancelled; those salaries are payable again.");
        }

        private static List<string>? ChequeNumbers(string? first, int count)
        {
            first = first?.Trim();
            if (string.IsNullOrEmpty(first) || !first.All(char.IsDigit) || first.Length > 18)
                return null;

            var start = long.Parse(first, CultureInfo.InvariantCulture);
            return Enumerable.Range(0, count)
                .Select(i => (start + i).ToString(CultureInfo.InvariantCulture).PadLeft(first.Length, '0'))
                .ToList();
        }

        // =====================================================================
        //  WPS SALARY INFORMATION FILE
        // =====================================================================

        private static string? CheckWps(PayrollSettings settings, List<PayrollRunLine> lines)
        {
            if (!IsDigits(settings.EmployerEstablishmentId, 13))
                return "Enter the company's 13-digit MOHRE establishment number in Payroll Settings before producing a WPS file.";
            if (!IsDigits(settings.EmployerBankRoutingCode, 9))
                return "Enter the 9-digit routing code of the company's salary bank in Payroll Settings.";

            var problems = new List<string>();
            foreach (var line in lines)
            {
                if (!IsDigits(line.MohrePersonCode, 14))
                    problems.Add($"{line.EmployeeName}: 14-digit MOHRE person code");
                if (!IsDigits(line.BankRoutingCode, 9))
                    problems.Add($"{line.EmployeeName}: 9-digit bank routing code");
                if (string.IsNullOrWhiteSpace(line.Iban))
                    problems.Add($"{line.EmployeeName}: IBAN");
            }

            return problems.Count == 0
                ? null
                : "The WPS file cannot be produced until these are on the employee records: " + string.Join("; ", problems.Take(10)) +
                  (problems.Count > 10 ? $" and {problems.Count - 10} more." : ".");
        }

        /// <summary>
        /// The Salary Information File in the standard UAE WPS layout: one EDR record per
        /// employee and an SCR control record, comma separated. Named after the employer's
        /// establishment number and the creation time, as the WPS agents expect.
        /// </summary>
        public static (string FileName, string Content) BuildSif(PayrollSettings settings, PayrollRun run, List<PayrollRunLine> lines, DateTime createdAt)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                // The two components must add up to what is paid, and neither may be negative:
                // overtime and bonus are the variable part, deductions come off the fixed part.
                var variablePay = Math.Min(line.OvertimeAmount + line.Bonus, line.Net);
                var fixedPay = line.Net - variablePay;
                sb.Append("EDR,")
                    .Append(line.MohrePersonCode).Append(',')
                    .Append(line.BankRoutingCode).Append(',')
                    .Append(line.Iban?.Replace(" ", string.Empty)).Append(',')
                    .Append(run.PeriodStart.ToString("yyyy-MM-dd", inv)).Append(',')
                    .Append(run.PeriodEnd.ToString("yyyy-MM-dd", inv)).Append(',')
                    .Append(line.DaysInMonth.ToString(inv)).Append(',')
                    .Append(fixedPay.ToString("0.00", inv)).Append(',')
                    .Append(variablePay.ToString("0.00", inv)).Append(',')
                    .Append((line.UnpaidLeaveDays + line.AbsenceDays).ToString("0", inv))
                    .Append("\r\n");
            }

            sb.Append("SCR,")
                .Append(settings.EmployerEstablishmentId).Append(',')
                .Append(settings.EmployerBankRoutingCode).Append(',')
                .Append(createdAt.ToString("yyyy-MM-dd", inv)).Append(',')
                .Append(createdAt.ToString("HHmm", inv)).Append(',')
                .Append(run.PeriodStart.ToString("MMyyyy", inv)).Append(',')
                .Append(lines.Count.ToString(inv)).Append(',')
                .Append(lines.Sum(x => x.Net).ToString("0.00", inv)).Append(',')
                .Append("AED,")
                .Append(run.RunNumber)
                .Append("\r\n");

            var fileName = $"{settings.EmployerEstablishmentId}{createdAt.ToString("yyMMddHHmmss", inv)}.SIF";
            return (fileName, sb.ToString());
        }

        private static string BuildTransferCsv(PayrollRun run, List<PayrollRunLine> lines)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("Employee Code,Employee Name,Bank,IBAN,Amount,Reference\r\n");
            foreach (var line in lines)
            {
                sb.Append(Csv(line.EmployeeCode)).Append(',')
                    .Append(Csv(line.EmployeeName)).Append(',')
                    .Append(Csv(line.BankName)).Append(',')
                    .Append(Csv(line.Iban?.Replace(" ", string.Empty))).Append(',')
                    .Append(line.Net.ToString("0.00", inv)).Append(',')
                    .Append(Csv($"Salary {run.PeriodStart:MMM yyyy}"))
                    .Append("\r\n");
            }

            return sb.ToString();
        }

        private static string Csv(string? value)
        {
            value ??= string.Empty;
            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        }

        // =====================================================================
        //  SALARY ADVANCES
        // =====================================================================

        public async Task<(bool Success, string Message)> CreateAdvanceAsync(int companyId, EmployeeAdvance advance, string userName)
        {
            if (advance.Amount <= 0)
                return (false, "Enter the amount advanced.");
            if (advance.MonthlyInstalment <= 0 || advance.MonthlyInstalment > advance.Amount)
                return (false, "The monthly instalment must be more than zero and no more than the advance.");

            var employee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == advance.EmployeeId);
            if (employee == null)
                return (false, "Choose the employee.");

            var settings = await GetSettingsAsync(companyId);
            var accounts = await RequireAccountsAsync(settings, ("Employee advances", settings.EmployeeAdvancesAccountId));
            if (accounts.Problem != null)
                return (false, accounts.Problem);

            var bank = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == advance.PaidFromAccountId);
            if (bank == null || !bank.IsPostable || bank.AccountType != "Asset")
                return (false, "Choose the account the advance is paid from.");

            await using var transaction = await _db.Database.BeginTransactionAsync();
            advance.CompanyId = companyId;
            advance.RecoveredAmount = 0m;
            _db.EmployeeAdvances.Add(advance);
            await _db.SaveChangesAsync();

            var (journal, problem) = await PostJournalAsync(companyId, advance.AdvanceDate,
                $"Salary advance to {employee.FullName}", AdvanceSourceType, advance.Id,
                new List<JournalEntryLine>
                {
                    new() { AccountId = settings.EmployeeAdvancesAccountId!.Value, Debit = advance.Amount, Credit = 0m, Description = $"Advance {employee.EmployeeCode} {employee.FullName}" },
                    new() { AccountId = bank.Id, Debit = 0m, Credit = advance.Amount, Description = advance.Reference ?? "Salary advance" }
                },
                advance.Reference, userName);
            if (problem != null)
                return (false, problem);

            advance.JournalEntryId = journal!.Id;
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return (true, $"Advance of {advance.Amount:N2} to {employee.FullName} recorded. {advance.MonthlyInstalment:N2} will be recovered from each payroll.");
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        private async Task<(bool Ok, string? Problem)> RequireAccountsAsync(PayrollSettings settings, params (string Name, int? Id)[] required)
        {
            foreach (var (name, id) in required)
            {
                if (!id.HasValue)
                    return (false, $"Choose the {name} account in Payroll Settings.");
                var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value);
                if (account == null || !account.IsPostable || !account.IsActive)
                    return (false, $"The {name} account in Payroll Settings must be an active, postable account.");
            }

            return (true, null);
        }

        /// <summary>Creates a posted journal, refusing a date with no period or in a closed one.</summary>
        private async Task<(JournalEntry? Journal, string? Problem)> PostJournalAsync(
            int companyId, DateTime date, string description, string sourceType, int sourceId,
            List<JournalEntryLine> lines, string? documentNumber, string userName)
        {
            if (lines.Count < 2 || lines.Sum(x => x.Debit) != lines.Sum(x => x.Credit))
                return (null, "The payroll journal does not balance; nothing was posted.");

            var period = await _db.AccountingPeriods.FirstOrDefaultAsync(x =>
                x.CompanyId == companyId && x.StartDate <= date.Date && x.EndDate >= date.Date);
            if (period == null)
                return (null, $"No accounting period covers {date:dd/MM/yyyy}.");
            if (period.IsClosed)
                return (null, $"{period.Name} is closed, so nothing can be posted on {date:dd/MM/yyyy}. Choose a date in an open month.");

            var count = await _db.JournalEntries.CountAsync() + 1;
            var journal = new JournalEntry
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                EntryNumber = $"{(sourceType == RunSourceType ? "PAY" : sourceType == PaymentSourceType ? "SAL" : "ADV")}-{DateTime.UtcNow:yyyyMMdd}-{count:D6}",
                EntryDate = date.Date,
                JournalType = "Payroll",
                Status = "Posted",
                IsPosted = true,
                PostedAt = DateTime.UtcNow,
                PostedBy = Limit(userName, 200),
                Description = description,
                DocumentNumber = documentNumber,
                SourceType = sourceType,
                SourceId = sourceId,
                CreatedAt = DateTime.UtcNow,
                Lines = lines
            };

            _db.JournalEntries.Add(journal);
            await _db.SaveChangesAsync();
            return (journal, null);
        }

        public static string MonthName(int year, int month) =>
            new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        private static bool IsDigits(string? value, int length) =>
            value != null && value.Length == length && value.All(char.IsDigit);

        private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static string Limit(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
