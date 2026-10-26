namespace RealEstateManager.Domain
{
    public class AppUser
    {
        public int Id { get; set; }

        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public bool IsSystemAdmin { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }

        // Sign-in protection: repeated wrong passwords lock the account for a while,
        // and a password set by someone else (or the built-in default) must be changed.
        public int FailedLoginAttempts { get; set; }
        public DateTime? LockedOutUntil { get; set; }
        public bool MustChangePassword { get; set; }
        public DateTime? PasswordChangedAt { get; set; }

        public ICollection<AppUserCompany> CompanyAccesses { get; set; } = new List<AppUserCompany>();
        public ICollection<AppUserPermission> Permissions { get; set; } = new List<AppUserPermission>();
        public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
        public ICollection<UserFeedback> SubmittedFeedback { get; set; } = new List<UserFeedback>();
        public ICollection<UserFeedback> ReviewedFeedback { get; set; } = new List<UserFeedback>();
    }
}
