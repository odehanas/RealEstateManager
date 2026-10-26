using System.Security.Claims;

namespace RealEstateManager.Services
{
    public static class ClaimsPrincipalExtensions
    {
        public static int? GetAppUserId(this ClaimsPrincipal user)
        {
            return TryGetIntClaim(user, AppClaimTypes.UserId);
        }

        public static int? GetCurrentCompanyId(this ClaimsPrincipal user)
        {
            return TryGetIntClaim(user, AppClaimTypes.CurrentCompanyId);
        }

        public static int GetRequiredCurrentCompanyId(this ClaimsPrincipal user)
        {
            return user.GetCurrentCompanyId()
                ?? throw new InvalidOperationException("No company is selected for the current user.");
        }

        // ---------------------------------------------------------------------
        //  Tenant self-service portal
        // ---------------------------------------------------------------------

        /// <summary>The tenant this principal represents, or null if it is staff.</summary>
        public static int? GetPortalTenantId(this ClaimsPrincipal user)
        {
            return TryGetIntClaim(user, AppClaimTypes.PortalTenantId);
        }

        public static int GetRequiredPortalTenantId(this ClaimsPrincipal user)
        {
            return user.GetPortalTenantId()
                ?? throw new InvalidOperationException("This session is not a tenant portal session.");
        }

        public static int? GetPortalUserId(this ClaimsPrincipal user)
        {
            return TryGetIntClaim(user, AppClaimTypes.PortalUserId);
        }

        /// <summary>
        /// True when the principal is a tenant rather than a member of staff. Presence
        /// of the tenant claim is the only test - staff sign-in never issues it.
        /// </summary>
        public static bool IsTenantPortalUser(this ClaimsPrincipal user)
        {
            return user.GetPortalTenantId().HasValue;
        }

        /// <summary>True when the principal is an employee signed in to the self-service portal.</summary>
        public static bool IsEmployeePortalUser(this ClaimsPrincipal user) =>
            TryGetIntClaim(user, AppClaimTypes.EmployeePortalEmployeeId).HasValue;

        public static int? GetPortalEmployeeId(this ClaimsPrincipal user) =>
            TryGetIntClaim(user, AppClaimTypes.EmployeePortalEmployeeId);

        public static int? GetEmployeePortalUserId(this ClaimsPrincipal user) =>
            TryGetIntClaim(user, AppClaimTypes.EmployeePortalUserId);

        public static string GetPortalTenantName(this ClaimsPrincipal user)
        {
            return user.FindFirstValue(AppClaimTypes.PortalTenantName) ?? string.Empty;
        }

        private static int? TryGetIntClaim(ClaimsPrincipal user, string claimType)
        {
            var value = user.FindFirstValue(claimType);
            return int.TryParse(value, out var parsed) ? parsed : null;
        }
    }
}
