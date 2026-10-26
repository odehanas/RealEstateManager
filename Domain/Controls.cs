namespace RealEstateManager.Domain
{
    // =========================================================================
    //  INTERNAL CONTROLS
    //
    //  An authority matrix for releasing payments (each approval level may
    //  release up to its own limit), temporary delegation of authority while an
    //  owner is away, maker-checker on every payment, and the employee
    //  self-service portal.
    // =========================================================================

    /// <summary>
    /// One rung of the company's approval ladder, e.g. Accountant up to 1,000, Manager up
    /// to 5,000, Owner without limit. Users are placed on a level per company.
    /// </summary>
    public class ApprovalLevel
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>The largest single payment this level may release. Blank means no limit.</summary>
        public decimal? MaxAmount { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// Authority lent for a period - for example by an owner travelling abroad to the
    /// manager. While it runs, the delegate may release payments up to its limit.
    /// </summary>
    public class ApprovalDelegation
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int FromAppUserId { get; set; }
        public string FromName { get; set; } = string.Empty;
        public int ToAppUserId { get; set; }
        public string ToName { get; set; } = string.Empty;

        /// <summary>The largest payment the delegate may release while it runs. Blank means no limit.</summary>
        public decimal? MaxAmount { get; set; }
        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime EndDate { get; set; } = DateTime.Today.AddDays(7);
        public string? Reason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? RevokedByName { get; set; }
        public DateTime? RevokedAt { get; set; }

        public bool IsActiveOn(DateTime day) =>
            RevokedAt == null && StartDate.Date <= day.Date && EndDate.Date >= day.Date;
    }

    /// <summary>
    /// A payment waiting for release. The payment's journal stays a draft - out of the
    /// accounts - until someone whose authority covers the amount approves it.
    /// </summary>
    public class PaymentApproval
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public string SourceType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }

        /// <summary>The lowest level whose limit covers the amount, as it was when requested.</summary>
        public string RequiredLevel { get; set; } = string.Empty;

        public string Status { get; set; } = PaymentApprovalStatuses.Pending;

        public int? RequestedByAppUserId { get; set; }
        public string RequestedByName { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        public int? DecidedByAppUserId { get; set; }
        public string? DecidedByName { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? Comment { get; set; }
    }

    public static class PaymentApprovalStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";

        /// <summary>Released by the person who entered it, within their own limit.</summary>
        public const string SelfReleased = "SelfReleased";
    }

    /// <summary>An employee's login to the self-service portal. Separate from staff users, as tenants are.</summary>
    public class EmployeePortalUser
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int EmployeeId { get; set; }
        public Employee? Employee { get; set; }

        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public bool MustChangePassword { get; set; } = true;
        public int FailedLoginAttempts { get; set; }
        public DateTime? LockedOutUntil { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }
    }

    /// <summary>A document an employee asks HR for through the portal - a salary certificate, for example.</summary>
    public class EmployeeRequest
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int EmployeeId { get; set; }
        public Employee? Employee { get; set; }

        public string RequestType { get; set; } = EmployeeRequestTypes.SalaryCertificate;

        /// <summary>Who the document is addressed to, e.g. "Emirates NBD" or "To whom it may concern".</summary>
        public string? AddressedTo { get; set; }
        public string? Purpose { get; set; }

        public string Status { get; set; } = EmployeeRequestStatuses.Pending;
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public string? DecidedByName { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? Comment { get; set; }

        /// <summary>The certificate reference printed on the document once issued.</summary>
        public string? ReferenceNumber { get; set; }
    }

    public static class EmployeeRequestTypes
    {
        public const string SalaryCertificate = "SalaryCertificate";
        public const string SalaryTransferLetter = "SalaryTransferLetter";
        public const string EmploymentCertificate = "EmploymentCertificate";

        public static readonly string[] All = { SalaryCertificate, SalaryTransferLetter, EmploymentCertificate };

        public static string Describe(string type) => type switch
        {
            SalaryCertificate => "Salary certificate",
            SalaryTransferLetter => "Salary transfer letter",
            EmploymentCertificate => "Employment certificate (no salary)",
            _ => type
        };
    }

    public static class EmployeeRequestStatuses
    {
        public const string Pending = "Pending";
        public const string Issued = "Issued";
        public const string Declined = "Declined";
    }
}
