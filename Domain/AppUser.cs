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

        // Two-step sign-in: after the password, a code from the authenticator app on the
        // user's phone. See TwoStepSignInService.
        public bool TwoFactorEnabled { get; set; }

        /// <summary>The key shared with the user's authenticator app, encrypted.</summary>
        public string? TwoFactorSecret { get; set; }
        public DateTime? TwoFactorEnabledAt { get; set; }

        /// <summary>Hashes of the recovery codes not yet used, separated by ';'.</summary>
        public string? TwoFactorRecoveryCodes { get; set; }

        /// <summary>The time step of the last code accepted, so a code cannot be used twice.</summary>
        public long? TwoFactorLastStep { get; set; }

        /// <summary>Screen language, "en" or "ar"; null until the user chooses. Applied at sign-in.</summary>
        public string? PreferredLanguage { get; set; }

        public ICollection<AppUserCompany> CompanyAccesses { get; set; } = new List<AppUserCompany>();
        public ICollection<AppUserPermission> Permissions { get; set; } = new List<AppUserPermission>();
        public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
        public ICollection<UserFeedback> SubmittedFeedback { get; set; } = new List<UserFeedback>();
        public ICollection<UserFeedback> ReviewedFeedback { get; set; } = new List<UserFeedback>();
    }
}
