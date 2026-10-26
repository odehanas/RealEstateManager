using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Daily attendance: marked by hand on a day sheet or imported from a time-attendance
    /// device, summarised by month, and turned into payroll transactions - overtime,
    /// overtime on rest days, unauthorised absence and (optionally) late arrival - that the
    /// payroll then pays or deducts.
    /// </summary>
    public class AttendanceService
    {
        private readonly ApplicationDbContext _db;
        private readonly PayrollService _payroll;

        public AttendanceService(ApplicationDbContext db, PayrollService payroll)
        {
            _db = db;
            _payroll = payroll;
        }

        public static bool IsWeeklyOff(PayrollSettings settings, DateTime date) =>
            (settings.WeeklyOffDays ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(d => string.Equals(d, date.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase));

        // =====================================================================
        //  DAY SHEET
        // =====================================================================

        public sealed class DayRow
        {
            public int EmployeeId { get; set; }
            public string EmployeeCode { get; set; } = string.Empty;
            public string EmployeeName { get; set; } = string.Empty;
            public string? JobTitle { get; set; }
            public string Status { get; set; } = AttendanceStatuses.Present;
            public TimeSpan? CheckIn { get; set; }
            public TimeSpan? CheckOut { get; set; }
            public string? Notes { get; set; }
            public decimal WorkedHours { get; set; }
            public decimal OvertimeHours { get; set; }
            public int LateMinutes { get; set; }
            public bool IsSaved { get; set; }
            public string? LeaveType { get; set; }
        }

        /// <summary>
        /// Everyone employed on the date, with what is already recorded. Unrecorded people
        /// start as on leave (approved leave covers the day), weekly off, or present.
        /// </summary>
        public async Task<List<DayRow>> GetDaySheetAsync(int companyId, DateTime date)
        {
            date = date.Date;
            var settings = await _payroll.GetSettingsAsync(companyId);
            var employees = await _db.Employees.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Status != EmployeeStatuses.Suspended &&
                            x.JoiningDate <= date && (x.LeavingDate == null || x.LeavingDate >= date))
                .OrderBy(x => x.EmployeeCode)
                .ToListAsync();

            var records = await _db.AttendanceRecords.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Date == date)
                .ToDictionaryAsync(x => x.EmployeeId);

            var leave = await _db.LeaveRequests.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Status == LeaveStatuses.Approved && x.StartDate <= date && x.EndDate >= date)
                .ToListAsync();

            var weeklyOff = IsWeeklyOff(settings, date);
            return employees.Select(e =>
            {
                var onLeave = leave.FirstOrDefault(l => l.EmployeeId == e.Id);
                var row = new DayRow
                {
                    EmployeeId = e.Id,
                    EmployeeCode = e.EmployeeCode,
                    EmployeeName = e.FullName,
                    JobTitle = e.JobTitle,
                    LeaveType = onLeave?.LeaveType,
                    Status = onLeave != null ? AttendanceStatuses.OnLeave : weeklyOff ? AttendanceStatuses.WeeklyOff : AttendanceStatuses.Present
                };

                if (records.TryGetValue(e.Id, out var r))
                {
                    row.Status = r.Status;
                    row.CheckIn = r.CheckIn;
                    row.CheckOut = r.CheckOut;
                    row.Notes = r.Notes;
                    row.WorkedHours = r.WorkedHours;
                    row.OvertimeHours = r.OvertimeHours;
                    row.LateMinutes = r.LateMinutes;
                    row.IsSaved = true;
                }

                return row;
            }).ToList();
        }

        public sealed record DayEntry(int EmployeeId, string Status, TimeSpan? CheckIn, TimeSpan? CheckOut, string? Notes);

        public async Task<(bool Success, string Message)> SaveDayAsync(int companyId, DateTime date, List<DayEntry> entries, string source = "Manual")
        {
            date = date.Date;
            if (date > DateTime.Today)
                return (false, "Attendance cannot be recorded for a future date.");

            var settings = await _payroll.GetSettingsAsync(companyId);
            var lockedBy = await PaidPayrollCoveringAsync(companyId, date);
            if (lockedBy != null)
                return (false, $"Payroll {lockedBy} for that month is approved, so its attendance can no longer change.");

            var ids = entries.Select(x => x.EmployeeId).ToList();
            var existing = await _db.AttendanceRecords
                .Where(x => x.CompanyId == companyId && x.Date == date && ids.Contains(x.EmployeeId))
                .ToDictionaryAsync(x => x.EmployeeId);

            foreach (var entry in entries)
            {
                if (!AttendanceStatuses.All.Contains(entry.Status))
                    continue;

                if (!existing.TryGetValue(entry.EmployeeId, out var record))
                {
                    record = new AttendanceRecord { CompanyId = companyId, EmployeeId = entry.EmployeeId, Date = date };
                    _db.AttendanceRecords.Add(record);
                }

                record.Status = entry.Status;
                record.CheckIn = entry.CheckIn;
                record.CheckOut = entry.CheckOut;
                record.Notes = string.IsNullOrWhiteSpace(entry.Notes) ? null : entry.Notes.Trim();
                record.Source = source;
                Measure(record, settings);
            }

            await _db.SaveChangesAsync();
            return (true, $"Attendance for {date:dd/MM/yyyy} saved for {entries.Count} employee(s).");
        }

        /// <summary>
        /// Works out hours worked, overtime and lateness from the clock times. On a working
        /// day overtime is the time beyond the normal day; on a weekly rest day or a public
        /// holiday every hour worked is overtime.
        /// </summary>
        public static void Measure(AttendanceRecord record, PayrollSettings settings)
        {
            record.WorkedHours = 0m;
            record.OvertimeHours = 0m;
            record.LateMinutes = 0;

            if (record.CheckIn.HasValue && record.CheckOut.HasValue)
            {
                var span = record.CheckOut.Value - record.CheckIn.Value;
                if (span < TimeSpan.Zero)
                    span += TimeSpan.FromDays(1); // a night shift ending after midnight
                record.WorkedHours = Math.Round((decimal)span.TotalHours, 2);
            }

            var restDay = record.Status is AttendanceStatuses.WeeklyOff or AttendanceStatuses.Holiday;
            if (restDay)
            {
                record.OvertimeHours = record.WorkedHours;
                return;
            }

            if (record.Status != AttendanceStatuses.Present)
            {
                record.WorkedHours = 0m;
                return;
            }

            record.OvertimeHours = Math.Max(0m, record.WorkedHours - settings.WorkingHoursPerDay);

            if (record.CheckIn.HasValue)
            {
                var late = (int)Math.Floor((record.CheckIn.Value - settings.WorkStartTime).TotalMinutes);
                record.LateMinutes = late > settings.LateGraceMinutes ? late : 0;
            }
        }

        // =====================================================================
        //  IMPORT FROM A TIME-ATTENDANCE DEVICE
        // =====================================================================

        /// <summary>
        /// Reads a CSV exported from a time-attendance device: one line per employee per day,
        /// EmployeeCode, Date, CheckIn, CheckOut (a header line is skipped). Dates as
        /// yyyy-MM-dd or dd/MM/yyyy, times as HH:mm. An employee clocked in is present; one
        /// clocked in on a rest day is recorded as working on their weekly off.
        /// </summary>
        public async Task<(bool Success, string Message)> ImportCsvAsync(int companyId, Stream csv)
        {
            var settings = await _payroll.GetSettingsAsync(companyId);
            var employees = await _db.Employees.AsNoTracking().Where(x => x.CompanyId == companyId)
                .ToDictionaryAsync(x => x.EmployeeCode.ToUpperInvariant(), x => x.Id);

            using var reader = new StreamReader(csv);
            var problems = new List<string>();
            var byDate = new Dictionary<DateTime, List<DayEntry>>();
            var lineNo = 0;
            string? text;
            while ((text = await reader.ReadLineAsync()) != null)
            {
                lineNo++;
                var cells = text.Split(',', ';', '\t').Select(x => x.Trim().Trim('"')).ToArray();
                if (cells.Length < 2 || string.IsNullOrWhiteSpace(cells[0]))
                    continue;
                if (lineNo == 1 && !employees.ContainsKey(cells[0].ToUpperInvariant()) && !DateTime.TryParse(cells[1], out _))
                    continue; // header

                if (!employees.TryGetValue(cells[0].ToUpperInvariant(), out var employeeId))
                {
                    problems.Add($"line {lineNo}: no employee {cells[0]}");
                    continue;
                }

                if (!DateTime.TryParseExact(cells[1], new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    problems.Add($"line {lineNo}: date {cells[1]}");
                    continue;
                }

                TimeSpan? Time(int i) => cells.Length > i && TimeSpan.TryParse(cells[i], CultureInfo.InvariantCulture, out var t) ? t : null;
                var checkIn = Time(2);
                var checkOut = Time(3);
                var status = checkIn.HasValue
                    ? (IsWeeklyOff(settings, date) ? AttendanceStatuses.WeeklyOff : AttendanceStatuses.Present)
                    : AttendanceStatuses.Absent;

                if (!byDate.TryGetValue(date.Date, out var list))
                    byDate[date.Date] = list = new List<DayEntry>();
                list.RemoveAll(x => x.EmployeeId == employeeId);
                list.Add(new DayEntry(employeeId, status, checkIn, checkOut, "Imported"));
            }

            var saved = 0;
            foreach (var (date, entries) in byDate.OrderBy(x => x.Key))
            {
                var (ok, message) = await SaveDayAsync(companyId, date, entries, "Import");
                if (ok)
                    saved += entries.Count;
                else
                    problems.Add($"{date:dd/MM/yyyy}: {message}");
            }

            var summary = $"Imported {saved} attendance record(s) over {byDate.Count} day(s).";
            return problems.Count == 0
                ? (true, summary)
                : (saved > 0, summary + " Skipped: " + string.Join("; ", problems.Take(8)) + (problems.Count > 8 ? $" and {problems.Count - 8} more." : "."));
        }

        // =====================================================================
        //  MONTH SUMMARY AND PAYROLL TRANSACTIONS
        // =====================================================================

        public sealed class MonthRow
        {
            public int EmployeeId { get; set; }
            public string EmployeeCode { get; set; } = string.Empty;
            public string EmployeeName { get; set; } = string.Empty;
            public int Present { get; set; }
            public int Absent { get; set; }
            public int OnLeave { get; set; }
            public int Off { get; set; }
            public int NotRecorded { get; set; }
            public int LateDays { get; set; }
            public int LateMinutes { get; set; }
            public decimal OvertimeHours { get; set; }
            public decimal RestDayOvertimeHours { get; set; }
        }

        public async Task<List<MonthRow>> GetMonthSummaryAsync(int companyId, int year, int month)
        {
            var start = new DateTime(year, month, 1);
            var end = start.AddMonths(1).AddDays(-1);
            var lastDay = end < DateTime.Today ? end : DateTime.Today;
            var settings = await _payroll.GetSettingsAsync(companyId);

            var employees = await _db.Employees.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Status != EmployeeStatuses.Suspended &&
                            x.JoiningDate <= end && (x.LeavingDate == null || x.LeavingDate >= start))
                .OrderBy(x => x.EmployeeCode)
                .ToListAsync();
            var records = await _db.AttendanceRecords.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Date >= start && x.Date <= end)
                .ToListAsync();

            return employees.Select(e =>
            {
                var own = records.Where(r => r.EmployeeId == e.Id).ToList();
                var from = e.JoiningDate > start ? e.JoiningDate.Date : start;
                var to = e.LeavingDate.HasValue && e.LeavingDate.Value < lastDay ? e.LeavingDate.Value.Date : lastDay;
                var expectedDays = 0;
                for (var d = from; d <= to; d = d.AddDays(1))
                    if (!IsWeeklyOff(settings, d)) expectedDays++;

                var recordedWorkingDays = own.Count(r => !IsWeeklyOff(settings, r.Date));
                return new MonthRow
                {
                    EmployeeId = e.Id,
                    EmployeeCode = e.EmployeeCode,
                    EmployeeName = e.FullName,
                    Present = own.Count(r => r.Status == AttendanceStatuses.Present),
                    Absent = own.Count(r => r.Status == AttendanceStatuses.Absent),
                    OnLeave = own.Count(r => r.Status == AttendanceStatuses.OnLeave),
                    Off = own.Count(r => r.Status is AttendanceStatuses.WeeklyOff or AttendanceStatuses.Holiday),
                    NotRecorded = Math.Max(0, expectedDays - recordedWorkingDays),
                    LateDays = own.Count(r => r.LateMinutes > 0),
                    LateMinutes = own.Sum(r => r.LateMinutes),
                    OvertimeHours = own.Where(r => r.Status == AttendanceStatuses.Present).Sum(r => r.OvertimeHours),
                    RestDayOvertimeHours = own.Where(r => r.Status is AttendanceStatuses.WeeklyOff or AttendanceStatuses.Holiday).Sum(r => r.OvertimeHours)
                };
            }).ToList();
        }

        /// <summary>
        /// Turns a month's attendance into approved payroll transactions, one per employee
        /// and kind: overtime, overtime on rest days, unauthorised absence (absent days not
        /// covered by approved leave) and, if the company deducts it, late arrival. Running it
        /// again replaces what it made before, so attendance can be corrected and re-run
        /// until the payroll is approved.
        /// </summary>
        public async Task<(bool Success, string Message)> GenerateTransactionsAsync(int companyId, int year, int month, string userName)
        {
            var start = new DateTime(year, month, 1);
            var end = start.AddMonths(1).AddDays(-1);

            var lockedBy = await PaidPayrollCoveringAsync(companyId, end);
            if (lockedBy != null)
                return (false, $"Payroll {lockedBy} for that month is approved; its transactions can no longer change.");

            var settings = await _payroll.GetSettingsAsync(companyId);
            var summary = await GetMonthSummaryAsync(companyId, year, month);

            var leaveDays = await _db.LeaveRequests.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Status == LeaveStatuses.Approved && x.StartDate <= end && x.EndDate >= start)
                .Select(x => new { x.EmployeeId, x.StartDate, x.EndDate })
                .ToListAsync();
            var absences = await _db.AttendanceRecords.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Date >= start && x.Date <= end && x.Status == AttendanceStatuses.Absent)
                .Select(x => new { x.EmployeeId, x.Date })
                .ToListAsync();

            // What this generation made last time, not yet paid by an approved payroll, is replaced.
            var previous = await _db.PayrollAdjustments
                .Where(x => x.CompanyId == companyId && x.Source == PayrollAdjustmentSources.Attendance &&
                            x.Date >= start && x.Date <= end &&
                            (x.PayrollRunId == null || x.PayrollRun!.Status == PayrollRunStatuses.Draft))
                .ToListAsync();
            _db.PayrollAdjustments.RemoveRange(previous);

            var made = 0;
            void Add(int employeeId, string category, decimal quantity, string description)
            {
                if (quantity <= 0)
                    return;
                _db.PayrollAdjustments.Add(new PayrollAdjustment
                {
                    CompanyId = companyId,
                    EmployeeId = employeeId,
                    Date = end,
                    Category = category,
                    Quantity = quantity,
                    Description = description,
                    Status = PayrollAdjustmentStatuses.Approved,
                    Source = PayrollAdjustmentSources.Attendance,
                    CreatedByName = userName,
                    CreatedAt = DateTime.UtcNow,
                    DecidedByName = userName,
                    DecidedAt = DateTime.UtcNow
                });
                made++;
            }

            var monthName = start.ToString("MMM yyyy", CultureInfo.InvariantCulture);
            foreach (var row in summary)
            {
                Add(row.EmployeeId, PayrollAdjustmentCategories.Overtime, row.OvertimeHours, $"Overtime per attendance, {monthName}");
                Add(row.EmployeeId, PayrollAdjustmentCategories.RestDayOvertime, row.RestDayOvertimeHours, $"Rest-day / holiday work per attendance, {monthName}");

                var unexcused = absences.Count(a => a.EmployeeId == row.EmployeeId &&
                                                    !leaveDays.Any(l => l.EmployeeId == row.EmployeeId && l.StartDate <= a.Date && l.EndDate >= a.Date));
                Add(row.EmployeeId, PayrollAdjustmentCategories.UnauthorisedAbsence, unexcused, $"Absent without leave per attendance, {monthName}");

                if (settings.DeductLateTime)
                    Add(row.EmployeeId, PayrollAdjustmentCategories.LateArrival, row.LateMinutes, $"Late on {row.LateDays} day(s), {monthName}");
            }

            await _db.SaveChangesAsync();
            var refreshed = await _payroll.RefreshDraftRunsAsync(companyId);

            return (true, $"{made} payroll transaction(s) generated from {monthName} attendance." +
                          (refreshed.Count > 0 ? $" Draft payroll {string.Join(", ", refreshed)} recalculated." : ""));
        }

        /// <summary>The approved payroll that already paid the month of a date, if any.</summary>
        private async Task<string?> PaidPayrollCoveringAsync(int companyId, DateTime date) =>
            await _db.PayrollRuns.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Status != PayrollRunStatuses.Draft &&
                            x.PeriodStart <= date && x.PeriodEnd >= date)
                .Select(x => x.RunNumber)
                .FirstOrDefaultAsync();
    }
}
