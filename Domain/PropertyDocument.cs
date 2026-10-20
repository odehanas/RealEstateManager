namespace RealEstateManager.Domain
{
    /// <summary>
    /// A scanned document kept against a property or one of its units: the title deed,
    /// affection plan, permits, completion certificate, insurance policy. The file itself
    /// is stored in the database with the record, so it travels with the company's data.
    ///
    /// Documents with an expiry date (insurance, permits, registrations) are flagged on
    /// the property page as they approach it.
    /// </summary>
    public class PropertyDocument
    {
        public const int MaxFileBytes = 5 * 1024 * 1024;

        public static readonly string[] AllowedExtensions = { ".pdf", ".jpg", ".jpeg", ".png" };

        public static readonly string[] DocumentTypes =
        {
            "Title deed",
            "Affection / site plan",
            "Building permit",
            "Completion certificate",
            "Insurance policy",
            "Mortgage document",
            "Floor plan",
            "Lease registration",
            "Service charge / owners association",
            "Other"
        };

        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int PropertyId { get; set; }
        public Property? Property { get; set; }

        /// <summary>Set when the document belongs to one unit, such as a unit title deed.</summary>
        public int? UnitId { get; set; }
        public Unit? Unit { get; set; }

        public string DocumentType { get; set; } = "Other";

        /// <summary>The number printed on the document: deed, permit or policy number.</summary>
        public string? DocumentNumber { get; set; }

        public string? Description { get; set; }

        public DateTime? IssueDate { get; set; }
        public DateTime? ExpiryDate { get; set; }

        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/octet-stream";
        public long SizeBytes { get; set; }

        /// <summary>The file. Loaded only when downloaded - lists never select it.</summary>
        public byte[] Content { get; set; } = Array.Empty<byte>();

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public string? UploadedByName { get; set; }

        public bool IsExpired(DateTime today) => ExpiryDate.HasValue && ExpiryDate.Value.Date < today.Date;

        /// <summary>Expires within the next 60 days.</summary>
        public bool IsExpiringSoon(DateTime today) =>
            ExpiryDate.HasValue && !IsExpired(today) && ExpiryDate.Value.Date <= today.Date.AddDays(60);
    }
}
