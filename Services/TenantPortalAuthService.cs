using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Authentication for the tenant self-service portal. Entirely separate from
    /// <see cref="AppAuthService"/>: different table, different cookie, different
    /// claims. A principal issued here can never satisfy a staff permission check.
    /// </summary>
    public class TenantPortalAuthService
    {
        private readonly ApplicationDbContext _db;
        private readonly PasswordHashingService _passwordHashingService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>Attempts before the account locks. The portal login is public.</summary>
        private const int MaxFailedAttempts = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        public TenantPortalAuthService(
            ApplicationDbContext db,
            PasswordHashingService passwordHashingService,
            IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _passwordHashingService = passwordHashingService;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<TenantPortalLoginResult> SignInAsync(string username, string password)
        {
            var httpContext = _httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("No HTTP context.");

            var normalised = (username ?? string.Empty).Trim();

            // IgnoreQueryFilters: at login there is no company context yet, so the
            // company filter would hide every row.
            var user = await _db.TenantPortalUsers
                .IgnoreQueryFilters()
                .Include(x => x.Tenant)
                .Include(x => x.Company)
                .FirstOrDefaultAsync(x => x.Username == normalised);

            // Same message whether the user is missing, inactive or the password is
            // wrong - a public login must not reveal which usernames exist.
            const string failureMessage = "Incorrect username or password.";

            if (user == null)
                return TenantPortalLoginResult.Failed(failureMessage);

            if (user.LockedOutUntil.HasValue && user.LockedOutUntil.Value > DateTime.UtcNow)
            {
                var minutes = Math.Max(1, (int)(user.LockedOutUntil.Value - DateTime.UtcNow).TotalMinutes);
                return TenantPortalLoginResult.Failed(
                    $"This account is temporarily locked. Try again in {minutes} minute(s).");
            }

            if (!user.IsActive || user.Tenant == null || !user.Tenant.IsActive)
                return TenantPortalLoginResult.Failed(failureMessage);

            if (!_passwordHashingService.VerifyPassword(password, user.PasswordHash))
            {
                user.FailedLoginAttempts++;

                if (user.FailedLoginAttempts >= MaxFailedAttempts)
                {
                    user.LockedOutUntil = DateTime.UtcNow.Add(LockoutDuration);
                    user.FailedLoginAttempts = 0;
                }

                await _db.SaveChangesAsync();
                return TenantPortalLoginResult.Failed(failureMessage);
            }

            user.FailedLoginAttempts = 0;
            user.LockedOutUntil = null;
            user.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, user.Tenant.FullName),
                new(AppClaimTypes.PortalUserId, user.Id.ToString()),
                new(AppClaimTypes.PortalTenantId, user.TenantId.ToString()),
                new(AppClaimTypes.PortalTenantName, user.Tenant.FullName),

                // The existing EF query filters read this claim through
                // ICurrentCompanyAccessor, so setting it scopes every query to the
                // tenant's company without touching any of that code.
                new(AppClaimTypes.CurrentCompanyId, user.CompanyId.ToString()),
                new(AppClaimTypes.CurrentCompanyName, user.Company?.Name ?? string.Empty)
            };

            var identity = new ClaimsIdentity(claims, AppAuthSchemes.TenantPortal);

            await httpContext.SignInAsync(
                AppAuthSchemes.TenantPortal,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = false });

            return TenantPortalLoginResult.Succeeded(user.MustChangePassword);
        }

        public async Task SignOutAsync()
        {
            var httpContext = _httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("No HTTP context.");

            await httpContext.SignOutAsync(AppAuthSchemes.TenantPortal);
        }

        public async Task<string?> ChangePasswordAsync(int portalUserId, string currentPassword, string newPassword)
        {
            var user = await _db.TenantPortalUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == portalUserId);

            if (user == null)
                return "Account not found.";

            if (!_passwordHashingService.VerifyPassword(currentPassword, user.PasswordHash))
                return "Your current password is incorrect.";

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
                return "The new password must be at least 8 characters.";

            if (_passwordHashingService.VerifyPassword(newPassword, user.PasswordHash))
                return "The new password must be different from the current one.";

            user.PasswordHash = _passwordHashingService.HashPassword(newPassword);
            user.MustChangePassword = false;

            await _db.SaveChangesAsync();
            return null;
        }

        // =====================================================================
        //  STAFF-SIDE ADMINISTRATION
        // =====================================================================

        /// <summary>
        /// Issues or resets portal access for a tenant and returns the temporary
        /// password, which is shown once to the member of staff to hand over. It is
        /// never stored in readable form.
        /// </summary>
        public async Task<TenantPortalAccessResult> IssueAccessAsync(int tenantId, string username, int? createdByAppUserId)
        {
            var tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId);
            if (tenant == null)
                return TenantPortalAccessResult.Failed("Tenant not found.");

            var normalised = (username ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(normalised))
                return TenantPortalAccessResult.Failed("A username is required. The tenant's email address is the usual choice.");

            var takenByAnother = await _db.TenantPortalUsers
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Username == normalised && x.TenantId != tenantId);

            if (takenByAnother)
                return TenantPortalAccessResult.Failed("That username is already used by another tenant.");

            var temporaryPassword = GenerateTemporaryPassword();

            var existing = await _db.TenantPortalUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId);

            if (existing == null)
            {
                _db.TenantPortalUsers.Add(new TenantPortalUser
                {
                    CompanyId = tenant.CompanyId,
                    TenantId = tenant.Id,
                    Username = normalised,
                    PasswordHash = _passwordHashingService.HashPassword(temporaryPassword),
                    IsActive = true,
                    MustChangePassword = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByAppUserId = createdByAppUserId
                });
            }
            else
            {
                existing.Username = normalised;
                existing.PasswordHash = _passwordHashingService.HashPassword(temporaryPassword);
                existing.IsActive = true;
                existing.MustChangePassword = true;
                existing.FailedLoginAttempts = 0;
                existing.LockedOutUntil = null;
            }

            await _db.SaveChangesAsync();
            return TenantPortalAccessResult.Succeeded(normalised, temporaryPassword);
        }

        public async Task SetActiveAsync(int tenantId, bool isActive)
        {
            var user = await _db.TenantPortalUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId);

            if (user == null)
                return;

            user.IsActive = isActive;

            if (isActive)
            {
                user.FailedLoginAttempts = 0;
                user.LockedOutUntil = null;
            }

            await _db.SaveChangesAsync();
        }

        public async Task<TenantPortalUser?> GetByTenantAsync(int tenantId)
        {
            return await _db.TenantPortalUsers
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId);
        }

        /// <summary>
        /// Readable but unguessable: two letters, six digits, two letters. Long enough
        /// to resist guessing and short enough to read down a phone line.
        /// </summary>
        private static string GenerateTemporaryPassword()
        {
            const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ"; // no I or O
            const string digits = "23456789";                  // no 0 or 1

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

    public class TenantPortalLoginResult
    {
        public bool Success { get; private init; }
        public string? ErrorMessage { get; private init; }
        public bool MustChangePassword { get; private init; }

        public static TenantPortalLoginResult Succeeded(bool mustChangePassword) =>
            new() { Success = true, MustChangePassword = mustChangePassword };

        public static TenantPortalLoginResult Failed(string message) =>
            new() { Success = false, ErrorMessage = message };
    }

    public class TenantPortalAccessResult
    {
        public bool Success { get; private init; }
        public string? ErrorMessage { get; private init; }
        public string? Username { get; private init; }
        public string? TemporaryPassword { get; private init; }

        public static TenantPortalAccessResult Succeeded(string username, string temporaryPassword) =>
            new() { Success = true, Username = username, TemporaryPassword = temporaryPassword };

        public static TenantPortalAccessResult Failed(string message) =>
            new() { Success = false, ErrorMessage = message };
    }
}
