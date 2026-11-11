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

        /// <summary>
        /// The tenant who asked. Empty on a maintenance job raised by management or by the
        /// planned maintenance programme: a vacant unit being made ready has no tenant.
        /// </summary>
        public int? TenantId { get; set; }
        public Tenant? Tenant { get; set; }

        /// <summary>What a job raised by management is for. See <see cref="MaintenancePurposes"/>.</summary>
        public string? Purpose { get; set; }

        /// <summary>The planned maintenance programme entry that opened this job, if any.</summary>
        public int? MaintenancePlanId { get; set; }
        public MaintenancePlan? MaintenancePlan { get; set; }

        // ---------------------------------------------------------------------
        //  ENGLISH TRANSLATION (see RequestTranslationService)
        // ---------------------------------------------------------------------

        /// <summary>The language the tenant wrote in, as detected: "Arabic", "Hindi", "English"...</summary>
        public string? OriginalLanguage { get; set; }

        /// <summary>The title and text in English, kept beside the tenant's own words; empty when they wrote in English.</summary>
        public string? SubjectEnglish { get; set; }
        public string? DescriptionEnglish { get; set; }

        /// <summary>See <see cref="TranslationStatuses"/>; empty when it has not been looked at.</summary>
        public string? TranslationStatus { get; set; }

        // ---------------------------------------------------------------------
        //  PAID SERVICE (see ServiceCatalogItem and PaidServiceService)
        // ---------------------------------------------------------------------

        /// <summary>The service on the company's price list this request is an order for; empty for an ordinary request.</summary>
        public int? ServiceCatalogItemId { get; set; }

        /// <summary>The service's name, kind, price before VAT and expected cost as they were when ordered.</summary>
        public string? ServiceName { get; set; }
        public string? ServiceKind { get; set; }
        public decimal? ServicePrice { get; set; }
        public decimal? ServiceExpectedCost { get; set; }

        /// <summary>The work waits until the invoice is paid.</summary>
        public bool ServicePayFirst { get; set; }

        /// <summary>The invoice raised to the tenant for the service, once staff have issued it.</summary>
        public int? ServiceInvoiceId { get; set; }

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

        /// <summary>When the maintenance technician started work on the job.</summary>
        public DateTime? WorkStartedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public DateTime? ClosedAt { get; set; }

        /// <summary>Member of staff handling it.</summary>
        public int? AssignedToAppUserId { get; set; }

        /// <summary>What was done. Shown to the tenant, so it is written for them.</summary>
        public string? ResolutionNotes { get; set; }

        /// <summary>How the request reached us. See <see cref="ServiceRequestSources"/>.</summary>
        public string Source { get; set; } = ServiceRequestSources.Portal;

        // ---------------------------------------------------------------------
        //  THE TENANT'S RATING OF A MAINTENANCE JOB
        // ---------------------------------------------------------------------

        /// <summary>1 to 10, given once the job is done. See <see cref="ServiceRequestRatings"/>.</summary>
        public int? Rating { get; set; }

        /// <summary>What the tenant had to say about the job.</summary>
        public string? RatingFeedback { get; set; }

        public DateTime? RatedAt { get; set; }

        /// <summary>
        /// The member of staff who recorded the rating for a tenant who gave it by
        /// WhatsApp or phone. Empty when the tenant rated the job themselves on the portal.
        /// </summary>
        public int? RatedByAppUserId { get; set; }

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
            (Status == ServiceRequestStatuses.Submitted ||
             Status == ServiceRequestStatuses.Acknowledged) &&
            // An order that has been invoiced is in the accounts: the office cancels it
            // with a credit note, the tenant does not make it vanish.
            !ServiceInvoiceId.HasValue;

        /// <summary>A postponement that was agreed but not yet carried out on the invoice.</summary>
        public bool AwaitingPostponementAction =>
            RequestType == ServiceRequestTypes.PaymentPostponement &&
            Status == ServiceRequestStatuses.Resolved &&
            RelatedInvoiceId.HasValue &&
            RequestedNewDueDate.HasValue &&
            !PostponementApplied;

        /// <summary>A finished maintenance job nobody has rated yet. A job is rated once.</summary>
        public bool CanBeRated =>
            TenantId.HasValue &&
            RequestType == ServiceRequestTypes.Maintenance &&
            (Status == ServiceRequestStatuses.Resolved || Status == ServiceRequestStatuses.Closed) &&
            !Rating.HasValue;
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

        /// <summary>A tenant's message in English, beside what they wrote; empty when it was in English.</summary>
        public string? BodyEnglish { get; set; }
        public string? OriginalLanguage { get; set; }
        public string? TranslationStatus { get; set; }

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

        /// <summary>
        /// What a request from WhatsApp can be sorted into. Not a postponement: that needs
        /// an invoice and a date, and a free-form message gives neither.
        /// </summary>
        public static readonly string[] Classifiable =
            { Maintenance, ContractRenewal, Complaint, Other };

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

    /// <summary>
    /// How a request reached us. A tenant's own request on the portal is "Portal"; the
    /// others are logged by staff for the tenant, so the request still sits on the tenant's
    /// record and is worked and measured like any other.
    /// </summary>
    public static class ServiceRequestSources
    {
        public const string Portal = "Portal";
        public const string WhatsApp = "WhatsApp";
        public const string Phone = "Phone";
        public const string WalkIn = "WalkIn";
        public const string Email = "Email";
        public const string Other = "Other";

        /// <summary>A job management raised itself: a unit to make ready, an inspection, a repair found on a visit.</summary>
        public const string Management = "Management";

        /// <summary>A job opened by the planned maintenance programme.</summary>
        public const string Planned = "Planned";

        /// <summary>What staff can choose when logging a request for a tenant.</summary>
        public static readonly string[] StaffLogged = { WhatsApp, Phone, WalkIn, Email, Other };

        /// <summary>Jobs no tenant asked for.</summary>
        public static bool IsInternal(string source) => source is Management or Planned;

        public static string Describe(string source) => source switch
        {
            Portal => "Tenant portal",
            Management => "Raised by management",
            Planned => "Planned maintenance",
            WhatsApp => "WhatsApp",
            Phone => "Phone call",
            WalkIn => "Office visit",
            Email => "Email",
            Other => "Other source",
            _ => source
        };
    }

    /// <summary>What a maintenance job raised by management is for.</summary>
    public static class MaintenancePurposes
    {
        public const string MakeReady = "MakeReady";
        public const string Repair = "Repair";
        public const string Inspection = "Inspection";
        public const string Preventive = "Preventive";
        public const string Improvement = "Improvement";

        public static readonly string[] All = { MakeReady, Repair, Inspection, Preventive, Improvement };

        public static string Describe(string? purpose) => purpose switch
        {
            MakeReady => "Make ready for the next tenant",
            Repair => "Repair",
            Inspection => "Inspection",
            Preventive => "Preventive maintenance",
            Improvement => "Improvement",
            _ => "Tenant request"
        };
    }

    /// <summary>
    /// A planned maintenance programme entry: work done on a schedule whether or not a
    /// tenant asks - the AC serviced every three months, the fire system every six, the
    /// water tank cleaned once a year. A few days before each due date it opens a job.
    /// </summary>
    public class MaintenancePlan
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>The job's title, e.g. "AC service".</summary>
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        public int PropertyId { get; set; }
        public Property? Property { get; set; }

        /// <summary>One unit; empty for the building as a whole or its common areas.</summary>
        public int? UnitId { get; set; }
        public Unit? Unit { get; set; }

        /// <summary>The trade, from <see cref="ServiceRequestCategories.Maintenance"/>.</summary>
        public string? Category { get; set; }
        public string Priority { get; set; } = ServiceRequestPriorities.Normal;

        /// <summary>Every how many months the work is due: 1 to 24.</summary>
        public int IntervalMonths { get; set; } = 3;

        /// <summary>The next date the work is due. Moves on by the interval each time a job is opened.</summary>
        public DateTime NextDueDate { get; set; }

        /// <summary>How many days before the due date the job is opened, so it can be booked.</summary>
        public int LeadDays { get; set; } = 7;

        /// <summary>A technician who always does this work; empty to give it to the least busy one.</summary>
        public int? AssignToAppUserId { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? LastJobOpenedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByName { get; set; }

        public const int MinInterval = 1;
        public const int MaxInterval = 24;
        public const int MaxLeadDays = 60;
    }

    public static class ServiceRequestRatings
    {
        public const int Lowest = 1;
        public const int Highest = 10;

        /// <summary>At or below this the manager is told at once, and the dashboard counts it as low.</summary>
        public const int LowAtOrBelow = 5;

        public static bool IsLow(int rating) => rating <= LowAtOrBelow;

        /// <summary>Bootstrap badge class, so every screen colours a score alike.</summary>
        public static string BadgeClass(double rating) =>
            rating <= LowAtOrBelow ? "bg-danger" : rating < 8 ? "bg-warning text-dark" : "bg-success";

        public const int MaxFeedbackLength = 1000;
    }

    /// <summary>A photo attached to a request, usually by the tenant to show the problem.</summary>
    public class ServiceRequestPhoto
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int TenantServiceRequestId { get; set; }
        public TenantServiceRequest? TenantServiceRequest { get; set; }

        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "image/jpeg";
        public long SizeBytes { get; set; }

        /// <summary>The picture. Loaded only when the picture itself is asked for.</summary>
        public byte[] Content { get; set; } = Array.Empty<byte>();

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public string? UploadedByName { get; set; }
        public bool UploadedByTenant { get; set; } = true;
    }

    /// <summary>Where the English translation of a tenant's words stands.</summary>
    public static class TranslationStatuses
    {
        /// <summary>Saved and waiting for the translation.</summary>
        public const string Pending = "Pending";
        public const string Done = "Done";

        /// <summary>Written in English already.</summary>
        public const string NotNeeded = "NotNeeded";
        public const string Failed = "Failed";
    }

    public static class ServiceRequestAuthorTypes
    {
        public const string Tenant = "Tenant";
        public const string Staff = "Staff";
    }
}
