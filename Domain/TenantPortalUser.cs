namespace RealEstateManager.Domain
{
    /// <summary>
    /// A tenant's login to the self-service portal.
    ///
    /// Deliberately a SEPARATE table from AppUsers rather than a flag on it. Staff
    /// permissions are granted by rows in AppUserPermissions and enforced by a
    /// controller-to-permission map; any controller missing from that map is open to
    /// every authenticated staff user. If tenants lived in AppUsers, one forgotten map
    /// entry would expose company financials to a tenant. With a separate table there
    /// is nowhere to put a staff permission, so it cannot happen by accident.
    /// </summary>
    public class TenantPortalUser
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int TenantId { get; set; }
        public Tenant? Tenant { get; set; }

        /// <summary>Login name. The tenant's email in practice, but not required to be.</summary>
        public string Username { get; set; } = string.Empty;

        public string PasswordHash { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        /// <summary>Set when access is issued or reset, cleared on first successful change.</summary>
        public bool MustChangePassword { get; set; } = true;

        // Failed-attempt throttling. The portal login is public and tenant passwords
        // will be weak, so it needs a lockout that the staff login does not.
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockedOutUntil { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }

        /// <summary>Who issued the access, for the audit trail.</summary>
        public int? CreatedByAppUserId { get; set; }
    }
}
