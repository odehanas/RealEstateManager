namespace RealEstateManager.Domain
{
    /// <summary>
    /// Something a tenant has asked the landlord for: a repair, more time to pay, a
    /// renewal, a complaint, or anything else.
    ///
    /// This is a WORKFLOW record, not an accounting one. Nothing here touches the
    /// ledger. A tenant asking to postpone a payment does not move a due date, does not
    /// change an invoice and does not write a journal - it records the request and
    /// nothing more. If the request is granted, a member of staff applies the new due
    /// date explicitly from the staff screen, and that action is stamped on the record.
    /// Letting the portal change financial data on its own would mean a tenant could
    /// alter the receivables ledger by filling in a form.
    /// </summary>
    public class TenantServiceRequest
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int TenantId { get; set; }
        public Tenant? Tenant { get; set; }

        /// <summary>The contract this concerns. Optional - a tenant may ask a general question.</summary>
        public int? LeaseId { get; set; }
        public Lease? Lease { get; set; }

        /// <summary>
        /// Resolved from the contract when the request is raised and stored, so the
        /// request still says which building it was about after the tenancy ends.
        /// </summary>
        public int? PropertyId { get; set; }
        public Property? Property { get; set; }

        public int? UnitId { get; set; }
        public Unit? Unit { get; set; }

        /// <summary>Reference shown to both sides, e.g. REQ-2026-0001.</summary>
        public string RequestNumber { get; set; } = string.Empty;

        /// <summary>See <see cref="ServiceRequestTypes"/>.</summary>
        public string RequestType { get; set; } = ServiceRequestTypes.Maintenance;

        /// <summary>Sub-category, used for maintenance: Plumbing, Electrical, AC...</summary>
        public string? Category { get; set; }

        public string Subject { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Set by the tenant when raising the request and adjustable by staff
        /// afterwards. A tenant's sense of urgency is information, not an instruction.
        /// </summary>
        public string Priority { get; set; } = ServiceRequestPriorities.Normal;

        /// <summary>When the tenant would like someone to visit, for a maintenance call.</summary>
        public DateTime? PreferredVisitDate { get; set; }

        // ---------------------------------------------------------------------
        //  PAYMENT POSTPONEMENT
        // ---------------------------------------------------------------------

        /// <summary>The invoice the tenant is asking for more time on.</summary>
        public int? RelatedInvoiceId { get; set; }
        public CustomerInvoice? RelatedInvoice { get; set; }

        /// <summary>The date the tenant is asking to pay by.</summary>
        public DateTime? RequestedNewDueDate { get; set; }

        /// <summary>
        /// True once a member of staff has actually moved the invoice's due date. Keeps
        /// the action idempotent, so the button cannot be pressed twice, and makes it
        /// obvious on the record whether the agreement was carried out or merely agreed.
        /// </summary>
        public bool PostponementApplied { get; set; }

        public DateTime? PostponementAppliedAt { get; set; }
        public int? PostponementAppliedByAppUserId { get; set; }

        // ---------------------------------------------------------------------
        //  WORKFLOW
        // ---------------------------------------------------------------------

        /// <summary>See <see cref="ServiceRequestStatuses"/>.</summary>
        public string Status { get; set; } = ServiceRequestStatuses.Submitted;

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Which portal login raised it, for the audit trail.</summary>
        public int? SubmittedByPortalUserId { get; set; }

        public DateTime? AcknowledgedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public DateTime? ClosedAt { get; set; }

        /// <summary>Member of staff handling it.</summary>
        public int? AssignedToAppUserId { get; set; }

        /// <summary>What was done. Shown to the tenant, so it is written for them.</summary>
        public string? ResolutionNotes { get; set; }

        public ICollection<TenantServiceRequestComment> Comments { get; set; }
            = new List<TenantServiceRequestComment>();

        // ---------------------------------------------------------------------
        //  DERIVED
        // ---------------------------------------------------------------------

        /// <summary>A request still being worked on.</summary>
        public bool IsOpen =>
            Status != ServiceRequestStatuses.Closed &&
            Status != ServiceRequestStatuses.Cancelled &&
            Status != ServiceRequestStatuses.Rejected;

        /// <summary>
        /// A tenant may withdraw a request only before anyone has started on it. Once
        /// work is under way, withdrawing it would hide a job that may already have
        /// cost money.
        /// </summary>
        public bool CanTenantCancel =>
            Status == ServiceRequestStatuses.Submitted ||
            Status == ServiceRequestStatuses.Acknowledged;

        /// <summary>A postponement that was agreed but not yet carried out on the invoice.</summary>
        public bool AwaitingPostponementAction =>
            RequestType == ServiceRequestTypes.PaymentPostponement &&
            Status == ServiceRequestStatuses.Resolved &&
            RelatedInvoiceId.HasValue &&
            RequestedNewDueDate.HasValue &&
            !PostponementApplied;
    }

    /// <summary>
    /// One message on a request. Both sides write here, which keeps the whole
    /// conversation in one place instead of scattered across email.
    /// </summary>
    public class TenantServiceRequestComment
    {
        public int Id { get; set; }

        public int TenantServiceRequestId { get; set; }
        public TenantServiceRequest? TenantServiceRequest { get; set; }

        /// <summary>"Tenant" or "Staff".</summary>
        public string AuthorType { get; set; } = ServiceRequestAuthorTypes.Staff;

        /// <summary>Snapshotted so the thread still reads correctly if the author leaves.</summary>
        public string AuthorName { get; set; } = string.Empty;

        public int? AppUserId { get; set; }
        public int? TenantPortalUserId { get; set; }

        public string Body { get; set; } = string.Empty;

        /// <summary>
        /// A note staff write to each other. NEVER sent to the portal - the portal
        /// service filters these out rather than relying on the view to hide them,
        /// because a note that leaks is a note read by the person it was about.
        /// </summary>
        public bool IsInternal { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public static class ServiceRequestTypes
    {
        public const string Maintenance = "Maintenance";
        public const string PaymentPostponement = "PaymentPostponement";
        public const string ContractRenewal = "ContractRenewal";
        public const string Complaint = "Complaint";
        public const string Other = "Other";

        public static readonly string[] All =
            { Maintenance, PaymentPostponement, ContractRenewal, Complaint, Other };

        public static string Describe(string type) => type switch
        {
            Maintenance => "Maintenance / repair",
            PaymentPostponement => "Request more time to pay",
            ContractRenewal => "Contract renewal",
            Complaint => "Complaint",
            Other => "Other request",
            _ => type
        };
    }

    public static class ServiceRequestCategories
    {
        /// <summary>Offered for maintenance requests so the queue can be sorted by trade.</summary>
        public static readonly string[] Maintenance =
            { "Plumbing", "Electrical", "Air Conditioning", "Appliances", "Carpentry",
              "Pest Control", "Cleaning", "Common Areas", "Other" };
    }

    public static class ServiceRequestPriorities
    {
        public const string Low = "Low";
        public const string Normal = "Normal";
        public const string High = "High";
        public const string Urgent = "Urgent";

        public static readonly string[] All = { Low, Normal, High, Urgent };

        /// <summary>Sort weight, so the staff queue puts the worst first.</summary>
        public static int Weight(string priority) => priority switch
        {
            Urgent => 0,
            High => 1,
            Normal => 2,
            Low => 3,
            _ => 4
        };
    }

    public static class ServiceRequestStatuses
    {
        public const string Submitted = "Submitted";
        public const string Acknowledged = "Acknowledged";
        public const string InProgress = "InProgress";
        public const string Resolved = "Resolved";
        public const string Rejected = "Rejected";
        public const string Cancelled = "Cancelled";
        public const string Closed = "Closed";

        public static readonly string[] All =
            { Submitted, Acknowledged, InProgress, Resolved, Rejected, Cancelled, Closed };

        public static string Describe(string status) => status switch
        {
            Submitted => "Submitted",
            Acknowledged => "Acknowledged",
            InProgress => "In progress",
            Resolved => "Resolved",
            Rejected => "Declined",
            Cancelled => "Withdrawn",
            Closed => "Closed",
            _ => status
        };

        /// <summary>Bootstrap badge class, so the two screens colour statuses alike.</summary>
        public static string BadgeClass(string status) => status switch
        {
            Submitted => "bg-warning text-dark",
            Acknowledged => "bg-info text-dark",
            InProgress => "bg-primary",
            Resolved => "bg-success",
            Rejected => "bg-danger",
            Cancelled => "bg-secondary",
            Closed => "bg-dark",
            _ => "bg-secondary"
        };
    }

    public static class ServiceRequestAuthorTypes
    {
        public const string Tenant = "Tenant";
        public const string Staff = "Staff";
    }
}
