using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Daily attendance: the day sheet, import from a time-attendance device, the month
    /// summary, and turning the month into payroll transactions.
    /// </summary>
    public class AttendanceController : Controller
    {
        private readonly AttendanceService _attendance;
        private readonly PayrollService _payroll;

        public AttendanceController(AttendanceService attendance, PayrollService payroll)
        {
            _attendance = attendance;
            _payroll = payroll;
        }

        public sealed class DayPost
        {
            public int EmployeeId { get; set; }
            public string Status { get; set; } = string.Empty;
            public TimeSpan? CheckIn { get; set; }
            public TimeSpan? CheckOut { get; set; }
            public string? Notes { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? date)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var day = (date ?? DateTime.Today).Date;
            var settings = await _payroll.GetSettingsAsync(companyId);
            return View(new AttendanceDayViewModel
            {
                Date = day,
                Rows = await _attendance.GetDaySheetAsync(companyId, day),
                IsWeeklyOff = AttendanceService.IsWeeklyOff(settings, day),
                Settings = settings
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(DateTime date, List<DayPost> rows)
        {
            var (success, message) = await _attendance.SaveDayAsync(User.GetRequiredCurrentCompanyId(), date,
                rows.Select(r => new AttendanceService.DayEntry(r.EmployeeId, r.Status, r.CheckIn, r.CheckOut, r.Notes)).ToList());
            TempData[success ? "HrSuccess" : "HrError"] = message;
            return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Import(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["HrError"] = "Choose the CSV file exported from the attendance device.";
                return RedirectToAction(nameof(Index));
            }

            await using var stream = file.OpenReadStream();
            var (success, message) = await _attendance.ImportCsvAsync(User.GetRequiredCurrentCompanyId(), stream);
            TempData[success ? "HrSuccess" : "HrError"] = message;
            return RedirectToAction(nameof(Month));
        }

        [HttpGet]
        public async Task<IActionResult> Month(int? year, int? month)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var y = year ?? DateTime.Today.Year;
            var m = month ?? DateTime.Today.Month;
            return View(new AttendanceMonthViewModel
            {
                Year = y,
                Month = m,
                Rows = await _attendance.GetMonthSummaryAsync(companyId, y, m),
                Settings = await _payroll.GetSettingsAsync(companyId)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Generate(int year, int month)
        {
            var name = User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value ?? "User";
            var (success, message) = await _attendance.GenerateTransactionsAsync(User.GetRequiredCurrentCompanyId(), year, month, name);
            TempData[success ? "HrSuccess" : "HrError"] = message;
            return RedirectToAction(nameof(Month), new { year, month });
        }
    }
}
