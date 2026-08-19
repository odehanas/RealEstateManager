namespace RealEstateManager.Services
{
    public static class AppClaimTypes
    {
        public const string UserId = "app_user_id";
        public const string Username = "app_username";
        public const string DisplayName = "app_display_name";
        public const string CurrentCompanyId = "current_company_id";
        public const string CurrentCompanyName = "current_company_name";
        public const string IsSystemAdmin = "is_system_admin";

        // Tenant self-service portal. A principal carrying PortalTenantId is a TENANT,
        // never a member of staff.
        public const string PortalUserId = "portal_user_id";
        public const string PortalTenantId = "portal_tenant_id";
        public const string PortalTenantName = "portal_tenant_name";
    }

    /// <summary>Authentication scheme names.</summary>
    public static class AppAuthSchemes
    {
        /// <summary>Staff. The application default.</summary>
        public const string Staff = "Cookies";

        /// <summary>Tenant self-service portal. Its own cookie, its own lifetime.</summary>
        public const string TenantPortal = "TenantPortalCookies";
    }
}
