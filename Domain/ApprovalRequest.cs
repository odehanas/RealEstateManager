namespace RealEstateManager.Domain
{
    /// <summary>
    /// One request for a second person to approve a document - today a lease contract,
    /// later any document that needs a maker and a checker. The document stays in its
    /// own table; this records who asked, who decided, when and why.
    ///
    /// A document can be submitted more than once (rejected, corrected, submitted
    /// again); every attempt is its own row, so the full history is kept.
    /// </summary>
    public class ApprovalRequest
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>What kind of document - see <see cref="ApprovalDocumentTypes"/>.</summary>
        public string DocumentType { get; set; } = string.Empty;
        public int DocumentId { get; set; }

        /// <summary>The document's own number, e.g. the contract number, for lists and history.</summary>
        public string DocumentNumber { get; set; } = string.Empty;

        /// <summary>One line saying what is being approved, e.g. tenant, unit and rent.</summary>
        public string Summary { get; set; } = string.Empty;

        public string Status { get; set; } = ApprovalStatuses.Pending;

        public int RequestedByAppUserId { get; set; }
        public string RequestedByName { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Who created the document. Neither its creator nor the person who submitted it
        /// may approve it - the four-eyes rule.
        /// </summary>
        public int? DocumentCreatedByAppUserId { get; set; }

        public int? DecidedByAppUserId { get; set; }
        public string? DecidedByName { get; set; }
        public DateTime? DecidedAt { get; set; }

        /// <summary>The approver's remark; required when rejecting.</summary>
        public string? DecisionComment { get; set; }
    }

    public static class ApprovalStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";

        /// <summary>Taken back by the person who submitted it, before anyone decided.</summary>
        public const string Withdrawn = "Withdrawn";
    }

    public static class ApprovalDocumentTypes
    {
        public const string Lease = "Lease";
    }
}
