using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Leave requests and their approval. Approved annual leave reduces the balance;
    /// approved unpaid leave is deducted by the payroll of the month it falls in.
    /// </summary>
    public class LeaveController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly HrService _hr;

        public LeaveController(ApplicationDbContext db, HrService hr)
        {
            _db = db;
            _hr = hr;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? status)
        {
            status ??= LeaveStatuses.Pending;
            var query = _db.LeaveRequests.AsNoTracking().Include(x => x.Employee).AsQueryable();
            if (status != "All")
                query = query.Where(x => x.Status == status);

            return View(new LeaveListViewModel
            {
                Status = status,
                Requests = await query.OrderByDescending(x => x.StartDate).Take(300).ToListAsync(),
                Employees = await EmployeeOptionsAsync(),
                NewRequest = new LeaveRequest { StartDate = DateTime.Today, EndDate = DateTime.Today }
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind(Prefix = "NewRequest")] LeaveRequest request)
        {
            var employee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == request.EmployeeId);
            if (employee == null)
                return Back("Choose the employee.");
            if (request.EndDate.Date < request.StartDate.Date)
                return Back("The leave cannot end before it starts.");
            if (!LeaveTypes.All.Contains(request.LeaveType))
                return Back("Choose the type of leave.");

            var overlapping = await _db.LeaveRequests.AnyAsync(x =>
                x.EmployeeId == request.EmployeeId &&
                (x.Status == LeaveStatuses.Pending || x.Status == LeaveStatuses.Approved) &&
                x.StartDate <= request.EndDate && x.EndDate >= request.StartDate);
            if (overlapping)
                return Back($"{employee.FullName} already has leave booked over some of those dates.");

            request.Days = HrService.CountDays(request.StartDate, request.EndDate);
            if (request.LeaveType == LeaveTypes.Annual)
            {
                var balance = await _hr.GetAnnualLeaveBalanceAsync(employee, request.EndDate);
                var free = balance.Available - balance.Pending;
                if (request.Days > free)
                    return Back($"{employee.FullName} has {free:0.##} day(s) of annual leave available by then; {request.Days:0.##} requested. " +
                                "Book the rest as unpaid leave.");
            }

            request.CompanyId = User.GetRequiredCurrentCompanyId();
            request.Status = LeaveStatuses.Pending;
            request.RequestedByName = CurrentUserName();
            request.RequestedAt = DateTime.UtcNow;
            request.Reason = request.Reason?.Trim();
            _db.LeaveRequests.Add(request);
            await _db.SaveChangesAsync();

            TempData["HrSuccess"] = $"{request.LeaveType} leave of {request.Days:0.##} day(s) for {employee.FullName} is waiting for approval.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Approve(int id, string? comment) => Decide(id, LeaveStatuses.Approved, comment);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Reject(int id, string? comment) => Decide(id, LeaveStatuses.Rejected, comment);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Cancel(int id, string? comment) => Decide(id, LeaveStatuses.Cancelled, comment);

        private async Task<IActionResult> Decide(int id, string status, string? comment)
        {
            var request = await _db.LeaveRequests.Include(x => x.Employee).FirstOrDefaultAsync(x => x.Id == id);
            if (request == null)
                return NotFound();

            if (status == LeaveStatuses.Cancelled ? request.Status is LeaveStatuses.Rejected or LeaveStatuses.Cancelled
                                                  : request.Status != LeaveStatuses.Pending)
                return Back("This request has already been decided.");

            // Unpaid leave already paid for cannot be changed: the payroll of that month is approved.
            if (request.LeaveType == LeaveTypes.Unpaid && request.Status == LeaveStatuses.Approved &&
                await _db.PayrollRuns.AnyAsync(x => x.Status != PayrollRunStatuses.Draft &&
                                                     x.PeriodStart <= request.EndDate && x.PeriodEnd >= request.StartDate))
                return Back("The payroll for those dates is already approved, so this unpaid leave can no longer be cancelled.");

            request.Status = status;
            request.DecidedByName = CurrentUserName();
            request.DecidedAt = DateTime.UtcNow;
            request.DecisionComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
            await _db.SaveChangesAsync();

            TempData["HrSuccess"] = $"{request.Employee?.FullName}'s {request.LeaveType.ToLowerInvariant()} leave is {status.ToLowerInvariant()}.";
            return RedirectToAction(nameof(Index));
        }

        private IActionResult Back(string message)
        {
            TempData["HrError"] = message;
            return RedirectToAction(nameof(Index));
        }

        private async Task<List<SelectListItem>> EmployeeOptionsAsync() =>
            await _db.Employees.AsNoTracking()
                .Where(x => x.Status == EmployeeStatuses.Active || x.Status == EmployeeStatuses.Suspended)
                .OrderBy(x => x.EmployeeCode)
                .Select(x => new SelectListItem(x.EmployeeCode + " - " + x.FirstName + " " + x.LastName, x.Id.ToString()))
                .ToListAsync();

        private string CurrentUserName() =>
            User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value ?? "User";
    }
}
