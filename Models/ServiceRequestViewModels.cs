using RealEstateManager.Domain;

namespace RealEstateManager.Models
{
    /// <summary>What the tenant fills in to raise a request.</summary>
    public class ServiceRequestCreateViewModel
    {
        public string RequestType { get; set; } = ServiceRequestTypes.Maintenance;
        public string? Category { get; set; }

        public string Subject { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public string Priority { get; set; } = ServiceRequestPriorities.Normal;

        /// <summary>Which contract it concerns. Pre-selected when the tenant has only one.</summary>
        public int? LeaseId { get; set; }

        public DateTime? PreferredVisitDate { get; set; }

        // ---- Payment postponement only --------------------------------------
        public int? RelatedInvoiceId { get; set; }
        public DateTime? RequestedNewDueDate { get; set; }

        // ---- Logged by staff only; ignored on the portal --------------------
        public string Source { get; set; } = ServiceRequestSources.WhatsApp;

        /// <summary>When the message or call came in, so a late entry does not hide the wait.</summary>
        public DateTime? ReceivedAt { get; set; }

        // ---- Options for the form -------------------------------------------
        public List<ServiceRequestLeaseOption> Leases { get; set; } = new();
        public List<ServiceRequestInvoiceOption> OutstandingInvoices { get; set; } = new();
    }

    /// <summary>A tenant in the list staff choose from when logging a request.</summary>
    public class ServiceRequestTenantOption
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Phone { get; set; }

        public string Display => string.IsNullOrWhiteSpace(Phone) ? $"{Code} - {Name}" : $"{Code} - {Name} - {Phone}";
    }

    public class ServiceRequestLeaseOption
    {
        public int LeaseId { get; set; }
        public string ContractNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class ServiceRequestInvoiceOption
    {
        public int InvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public decimal Outstanding { get; set; }

        public string Display =>
            $"{InvoiceNumber} — due {DueDate:dd/MM/yyyy} — {Outstanding:N2} outstanding";
    }

    /// <summary>One row in either side's list of requests.</summary>
    public class ServiceRequestListItemViewModel
    {
        public int Id { get; set; }
        public string RequestNumber { get; set; } = string.Empty;
        public string RequestType { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }

        /// <summary>Staff list only.</summary>
        public string? TenantName { get; set; }
        public string? TenantCode { get; set; }
        public string? UnitDisplay { get; set; }

        public int MessageCount { get; set; }

        public string Source { get; set; } = ServiceRequestSources.Portal;

        /// <summary>Tenant list only: a finished job still waiting for their rating.</summary>
        public bool CanBeRated { get; set; }

        /// <summary>Days since submission, for the staff queue's ageing column.</summary>
        public int AgeInDays => (int)(DateTime.UtcNow.Date - SubmittedAt.Date).TotalDays;

        public bool AwaitingPostponementAction { get; set; }
    }

    /// <summary>A single request with its conversation.</summary>
    public class ServiceRequestDetailViewModel
    {
        public int Id { get; set; }
        public string RequestNumber { get; set; } = string.Empty;
        public string RequestType { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;

        public DateTime SubmittedAt { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public DateTime? ClosedAt { get; set; }

        public DateTime? PreferredVisitDate { get; set; }

        public string? ContractNumber { get; set; }
        public string? UnitDisplay { get; set; }
        public string? PropertyName { get; set; }

        public string? ResolutionNotes { get; set; }

        public string Source { get; set; } = ServiceRequestSources.Portal;

        // ---- Rating of a maintenance job -------------------------------------
        public int? Rating { get; set; }
        public string? RatingFeedback { get; set; }
        public DateTime? RatedAt { get; set; }
        public bool CanBeRated { get; set; }

        /// <summary>Staff only: who recorded the rating for the tenant; empty when the tenant rated it.</summary>
        public string? RatedByStaffName { get; set; }

        // ---- Postponement ----------------------------------------------------
        public string? RelatedInvoiceNumber { get; set; }
        public DateTime? RelatedInvoiceDueDate { get; set; }
        public decimal? RelatedInvoiceOutstanding { get; set; }
        public DateTime? RequestedNewDueDate { get; set; }
        public bool PostponementApplied { get; set; }
        public DateTime? PostponementAppliedAt { get; set; }

        // ---- Staff only ------------------------------------------------------
        public string? TenantName { get; set; }
        public string? TenantCode { get; set; }
        public string? TenantEmail { get; set; }
        public string? TenantPhone { get; set; }
        public int? AssignedToAppUserId { get; set; }
        public string? AssignedToName { get; set; }

        public bool CanTenantCancel { get; set; }
        public bool AwaitingPostponementAction { get; set; }

        public List<ServiceRequestCommentViewModel> Comments { get; set; } = new();
    }

    public class ServiceRequestCommentViewModel
    {
        public string AuthorType { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsInternal { get; set; }

        public bool IsFromTenant => AuthorType == ServiceRequestAuthorTypes.Tenant;
    }

    /// <summary>Counts for the staff queue's filter chips.</summary>
    public class ServiceRequestQueueViewModel
    {
        public List<ServiceRequestListItemViewModel> Requests { get; set; } = new();

        public string? StatusFilter { get; set; }
        public string? TypeFilter { get; set; }

        public int OpenCount { get; set; }
        public int SubmittedCount { get; set; }
        public int InProgressCount { get; set; }
        public int AwaitingPostponementCount { get; set; }
    }
}
