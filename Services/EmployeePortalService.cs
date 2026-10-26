using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The rules every new password must meet, for staff and portal users alike: at least
    /// ten characters with upper and lower case letters and a digit, and not the username.
    /// </summary>
    public static class PasswordPolicy
    {
        public const int MinimumLength = 10;

        public static string? Check(string? password, string? username = null)
        {
            if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
                return $"Use at least {MinimumLength} characters.";
            if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
                return "Use upper and lower case letters and at least one digit.";
            if (!string.IsNullOrEmpty(username) && password.Contains(username, StringComparison.OrdinalIgnoreCase))
                return "The password must not contain the username.";
            if (password.Equals("Admin@12345", StringComparison.OrdinalIgnoreCase) || password.StartsWith("Password", StringComparison.OrdinalIgnoreCase))
                return "Choose a password that is not a common one.";
            return null;
        }

        /// <summary>Readable but unguessable: two letters, six digits, two letters.</summary>
        public static string Temporary()
        {
            const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string digits = "23456789";
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(10);
            var result = new char[10];
            for (var i = 0; i < 10; i++)
            {
                var source = i is < 2 or >= 8 ? letters : digits;
                result[i] = source[bytes[i] % source.Length];
            }

            return new string(result);
        }
    }

    /// <summary>
    /// The employee self-service portal: sign-in at /me, and what an employee may see and
    /// ask for - their payslips, leave balance, salary advances, leave requests and
    /// certificates. Employees are not staff users: they have their own login table and
    /// cookie, and cannot reach any company screen.
    /// </summary>
    public class EmployeePortalService
    {
        private const int MaxFailedAttempts = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        private readonly ApplicationDbContext _db;
        private readonly PasswordHashingService _hashing;
        private readonly IHttpContextAccessor _http;
        private readonly HrService _hr;

        public EmployeePortalService(ApplicationDbContext db, PasswordHashingService hashing, IHttpContextAccessor http, HrService hr)
        {
            _db = db;
            _hashing = hashing;
            _http = http;
            _hr = hr;
        }

        // =====================================================================
        //  SIGN-IN
        // =====================================================================

        public async Task<(bool Success, string? Error, bool MustChangePassword)> SignInAsync(string? username, string? password)
        {
            const string failure = "Incorrect username or password.";
            var name = (username ?? string.Empty).Trim();
            var user = await _db.EmployeePortalUsers.IgnoreQueryFilters()
                .Include(x => x.Employee).Include(x => x.Company)
                .FirstOrDefaultAsync(x => x.Username == name);
            if (user == null)
                return (false, failure, false);

            if (user.LockedOutUntil.HasValue && user.LockedOutUntil > DateTime.UtcNow)
                return (false, $"This account is locked after repeated wrong passwords. Try again in {Math.Max(1, (int)(user.LockedOutUntil.Value - DateTime.UtcNow).TotalMinutes)} minute(s).", false);

            if (!user.IsActive || user.Employee == null || EmployeeStatuses.HasLeft(user.Employee.Status))
                return (false, failure, false);

            if (!_hashing.VerifyPassword(password ?? string.Empty, user.PasswordHash))
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= MaxFailedAttempts)
                {
                    user.LockedOutUntil = DateTime.UtcNow.Add(LockoutDuration);
                    user.FailedLoginAttempts = 0;
                }

                await _db.SaveChangesAsync();
                return (false, failure, false);
            }

            user.FailedLoginAttempts = 0;
            user.LockedOutUntil = null;
            user.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, user.Employee.FullName),
                new(AppClaimTypes.EmployeePortalUserId, user.Id.ToString()),
                new(AppClaimTypes.EmployeePortalEmployeeId, user.EmployeeId.ToString()),
                // Scopes every query to the employee's company through the existing filters.
                new(AppClaimTypes.CurrentCompanyId, user.CompanyId.ToString()),
                new(AppClaimTypes.CurrentCompanyName, user.Company?.Name ?? string.Empty)
            };

            await _http.HttpContext!.SignInAsync(AppAuthSchemes.EmployeePortal,
                new ClaimsPrincipal(new ClaimsIdentity(claims, AppAuthSchemes.EmployeePortal)),
                new AuthenticationProperties { IsPersistent = false });

            return (true, null, user.MustChangePassword);
        }

        public Task SignOutAsync() => _http.HttpContext!.SignOutAsync(AppAuthSchemes.EmployeePortal);

        public async Task<string?> ChangePasswordAsync(int portalUserId, string current, string next)
        {
            var user = await _db.EmployeePortalUsers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == portalUserId);
            if (user == null)
                return "Account not found.";
            if (!_hashing.VerifyPassword(current, user.PasswordHash))
                return "The current password is not correct.";
            var policy = PasswordPolicy.Check(next, user.Username);
            if (policy != null)
                return policy;

            user.PasswordHash = _hashing.HashPassword(next);
            user.MustChangePassword = false;
            await _db.SaveChangesAsync();
            return null;
        }

        /// <summary>Creates or resets an employee's portal login. Returns the temporary password to hand over.</summary>
        public async Task<(bool Success, string Message, string? TemporaryPassword)> IssueAccessAsync(int employeeId, string? username)
        {
            var employee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId);
            if (employee == null)
                return (false, "Employee not found.", null);

            var name = (username ?? string.Empty).Trim();
            if (name.Length < 4)
                return (false, "Choose a username of at least 4 characters - the mobile number or email is usual.", null);

            var taken = await _db.EmployeePortalUsers.IgnoreQueryFilters().AnyAsync(x => x.Username == name && x.EmployeeId != employeeId);
            if (taken)
                return (false, "That username is already in use.", null);

            var temporary = PasswordPolicy.Temporary();
            var user = await _db.EmployeePortalUsers.FirstOrDefaultAsync(x => x.EmployeeId == employeeId);
            if (user == null)
            {
                user = new EmployeePortalUser { CompanyId = employee.CompanyId, EmployeeId = employeeId, CreatedAt = DateTime.UtcNow };
                _db.EmployeePortalUsers.Add(user);
            }

            user.Username = name;
            user.PasswordHash = _hashing.HashPassword(temporary);
            user.MustChangePassword = true;
            user.IsActive = true;
            user.FailedLoginAttempts = 0;
            user.LockedOutUntil = null;
            await _db.SaveChangesAsync();

            return (true, $"Portal access for {employee.FullName}: username {name}, temporary password shown below. They must change it at first sign-in.", temporary);
        }

        public async Task SetActiveAsync(int employeeId, bool active)
        {
            var user = await _db.EmployeePortalUsers.FirstOrDefaultAsync(x => x.EmployeeId == employeeId);
            if (user == null)
                return;
            user.IsActive = active;
            await _db.SaveChangesAsync();
        }

        // =====================================================================
        //  WHAT THE EMPLOYEE SEES
        // =====================================================================

        public async Task<Employee?> GetEmployeeAsync(int employeeId) =>
            await _db.Employees.AsNoTracking().Include(x => x.Department).FirstOrDefaultAsync(x => x.Id == employeeId);

        /// <summary>Payslips of approved payrolls only: a draft is not yet a salary.</summary>
        public async Task<List<PayrollRunLine>> GetPayslipsAsync(int employeeId) =>
            await _db.PayrollRunLines.AsNoTracking().Include(x => x.PayrollRun)
                .Where(x => x.EmployeeId == employeeId && x.PayrollRun != null && x.PayrollRun.Status != PayrollRunStatuses.Draft)
                .OrderByDescending(x => x.PayrollRun!.Year).ThenByDescending(x => x.PayrollRun!.Month)
                .ToListAsync();

        public Task<HrService.LeaveBalance> GetLeaveBalanceAsync(Employee employee) =>
            _hr.GetAnnualLeaveBalanceAsync(employee, DateTime.Today);

        public async Task<List<LeaveRequest>> GetLeaveAsync(int employeeId) =>
            await _db.LeaveRequests.AsNoTracking().Where(x => x.EmployeeId == employeeId)
                .OrderByDescending(x => x.StartDate).Take(30).ToListAsync();

        public async Task<List<EmployeeAdvance>> GetAdvancesAsync(int employeeId) =>
            await _db.EmployeeAdvances.AsNoTracking().Where(x => x.EmployeeId == employeeId)
                .OrderByDescending(x => x.AdvanceDate).ToListAsync();

        public async Task<List<EmployeeRequest>> GetRequestsAsync(int employeeId) =>
            await _db.EmployeeRequests.AsNoTracking().Where(x => x.EmployeeId == employeeId)
                .OrderByDescending(x => x.RequestedAt).Take(30).ToListAsync();

        public async Task<(bool Success, string Message)> RequestLeaveAsync(int employeeId, string leaveType, DateTime start, DateTime end, string? reason)
        {
            var employee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId);
            if (employee == null)
                return (false, "Employee not found.");
            if (!LeaveTypes.All.Contains(leaveType))
                return (false, "Choose the type of leave.");
            if (end.Date < start.Date)
                return (false, "The leave cannot end before it starts.");
            if (start.Date < DateTime.Today.AddDays(-30))
                return (false, "Leave that started more than 30 days ago has to be booked by HR.");

            var overlapping = await _db.LeaveRequests.AnyAsync(x => x.EmployeeId == employeeId &&
                (x.Status == LeaveStatuses.Pending || x.Status == LeaveStatuses.Approved) && x.StartDate <= end && x.EndDate >= start);
            if (overlapping)
                return (false, "You already have leave booked over some of those dates.");

            var days = HrService.CountDays(start, end);
            if (leaveType == LeaveTypes.Annual)
            {
                var balance = await _hr.GetAnnualLeaveBalanceAsync(employee, end);
                var free = balance.Available - balance.Pending;
                if (days > free)
                    return (false, $"You have {free:0.##} day(s) of annual leave available by then; {days:0.##} requested.");
            }

            _db.LeaveRequests.Add(new LeaveRequest
            {
                CompanyId = employee.CompanyId,
                EmployeeId = employeeId,
                LeaveType = leaveType,
                StartDate = start.Date,
                EndDate = end.Date,
                Days = days,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                Status = LeaveStatuses.Pending,
                RequestedByName = $"{employee.FullName} (portal)",
                RequestedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            return (true, $"Your {leaveType.ToLowerInvariant()} leave request for {days:0.##} day(s) is sent to HR.");
        }

        public async Task<(bool Success, string Message)> RequestDocumentAsync(int employeeId, string type, string? addressedTo, string? purpose)
        {
            var employee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId);
            if (employee == null)
                return (false, "Employee not found.");
            if (!EmployeeRequestTypes.All.Contains(type))
                return (false, "Choose the document you need.");
            if (await _db.EmployeeRequests.AnyAsync(x => x.EmployeeId == employeeId && x.RequestType == type && x.Status == EmployeeRequestStatuses.Pending))
                return (false, "You already have one of these waiting with HR.");

            _db.EmployeeRequests.Add(new EmployeeRequest
            {
                CompanyId = employee.CompanyId,
                EmployeeId = employeeId,
                RequestType = type,
                AddressedTo = string.IsNullOrWhiteSpace(addressedTo) ? "To whom it may concern" : addressedTo.Trim(),
                Purpose = string.IsNullOrWhiteSpace(purpose) ? null : purpose.Trim(),
                Status = EmployeeRequestStatuses.Pending,
                RequestedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            return (true, $"Your request for a {EmployeeRequestTypes.Describe(type).ToLowerInvariant()} is sent to HR. You can print it here once it is issued.");
        }
    }
}
