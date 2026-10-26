using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    public class AppAuthService
    {
        private readonly ApplicationDbContext _db;
        private readonly PasswordHashingService _passwordHashingService;

        public AppAuthService(
            ApplicationDbContext db,
            PasswordHashingService passwordHashingService)
        {
            _db = db;
            _passwordHashingService = passwordHashingService;
        }

        public const int MaxFailedAttempts = 5;
        public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        /// <summary>The password every new installation's ADMIN starts with; it must not survive the first sign-in.</summary>
        public const string BootstrapAdminPassword = "Admin@123";

        /// <summary>
        /// Checks a staff sign-in. Five wrong passwords lock the account for fifteen minutes,
        /// which stops anyone guessing passwords from outside. A user still on the bootstrap
        /// password, or one an administrator set, is flagged to choose their own.
        /// </summary>
        public async Task<(AppUser? User, string? Error)> ValidateCredentialsAsync(string username, string password)
        {
            const string failure = "Invalid username or password.";
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return (null, failure);

            var normalizedUsername = username.Trim().ToUpperInvariant();

            var user = await _db.AppUsers
                .FirstOrDefaultAsync(x => x.Username == normalizedUsername && x.IsActive);

            if (user == null)
                return (null, failure);

            if (user.LockedOutUntil.HasValue && user.LockedOutUntil > DateTime.UtcNow)
            {
                var minutes = Math.Max(1, (int)Math.Ceiling((user.LockedOutUntil.Value - DateTime.UtcNow).TotalMinutes));
                return (null, $"This account is locked after {MaxFailedAttempts} wrong passwords. Try again in {minutes} minute(s), or ask an administrator.");
            }

            if (!_passwordHashingService.VerifyPassword(password, user.PasswordHash))
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= MaxFailedAttempts)
                {
                    user.LockedOutUntil = DateTime.UtcNow.Add(LockoutDuration);
                    user.FailedLoginAttempts = 0;
                }

                await _db.SaveChangesAsync();
                return (null, failure);
            }

            user.FailedLoginAttempts = 0;
            user.LockedOutUntil = null;
            if (password == BootstrapAdminPassword)
                user.MustChangePassword = true;
            await _db.SaveChangesAsync();
            return (user, null);
        }

        public async Task<List<CompanyOptionViewModel>> GetAccessibleCompaniesAsync(int userId, int? currentCompanyId = null)
        {
            return await _db.AppUserCompanies
                .Where(x => x.AppUserId == userId)
                .Include(x => x.Company)
                .OrderByDescending(x => x.IsDefault)
                .ThenBy(x => x.Company!.Name)
                .Select(x => new CompanyOptionViewModel
                {
                    CompanyId = x.CompanyId,
                    CompanyName = x.Company != null ? x.Company.Name : "-",
                    IsDefault = x.IsDefault,
                    IsCurrent = currentCompanyId.HasValue && x.CompanyId == currentCompanyId.Value
                })
                .ToListAsync();
        }

        public async Task<int?> GetDefaultCompanyIdAsync(int userId)
        {
            return await _db.AppUserCompanies
                .Where(x => x.AppUserId == userId)
                .OrderByDescending(x => x.IsDefault)
                .ThenBy(x => x.CompanyId)
                .Select(x => (int?)x.CompanyId)
                .FirstOrDefaultAsync();
        }

        /// <summary>Whether the user administers this company (manages its users).</summary>
        public Task<bool> IsCompanyAdminAsync(int userId, int companyId) =>
            UserManagerAuthorizationHandler.IsCompanyAdminAsync(_db, userId, companyId);

        public async Task<bool> UserHasAccessToCompanyAsync(int userId, int companyId)
        {
            return await _db.AppUserCompanies
                .AnyAsync(x => x.AppUserId == userId && x.CompanyId == companyId);
        }

        public async Task SignInAsync(HttpContext httpContext, AppUser user, int companyId, bool isPersistent = false)
        {
            var company = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == companyId);

            if (company == null)
                throw new InvalidOperationException("Selected company was not found.");

            var identity = new ClaimsIdentity(
                BuildClaims(user, company),
                CookieAuthenticationDefaults.AuthenticationScheme);

            var principal = new ClaimsPrincipal(identity);

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = isPersistent,
                    AllowRefresh = true
                });

            user.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        public async Task SignOutAsync(HttpContext httpContext)
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        private static IEnumerable<Claim> BuildClaims(AppUser user, Company company)
        {
            var claims = new List<Claim>
            {
                new(AppClaimTypes.UserId, user.Id.ToString()),
                new(AppClaimTypes.Username, user.Username),
                new(AppClaimTypes.DisplayName, user.DisplayName),
                new(AppClaimTypes.CurrentCompanyId, company.Id.ToString()),
                new(AppClaimTypes.CurrentCompanyName, company.Name),
                new(AppClaimTypes.IsSystemAdmin, user.IsSystemAdmin.ToString()),
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.DisplayName),
                new(ClaimTypes.GivenName, user.DisplayName)
            };

            if (user.IsSystemAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, "SystemAdmin"));
            }

            if (user.MustChangePassword)
            {
                claims.Add(new Claim(AppClaimTypes.MustChangePassword, "true"));
            }

            return claims;
        }
    }
}
