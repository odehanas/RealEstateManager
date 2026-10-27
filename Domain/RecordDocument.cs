namespace RealEstateManager.Domain
{
    /// <summary>
    /// A scanned document kept with a tenant or a contract - Emirates ID, passport,
    /// the contract attested by the municipality - so every paper of a tenancy is in
    /// one place, with its expiry date watched.
    /// </summary>
    public class RecordDocument
    {
        public const int MaxFileBytes = 5 * 1024 * 1024;
        public static readonly string[] AllowedExtensions = { ".pdf", ".jpg", ".jpeg", ".png" };

        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>See <see cref="RecordDocumentEntities"/>.</summary>
        public string EntityType { get; set; } = RecordDocumentEntities.Tenant;
        public int EntityId { get; set; }

        public string DocumentType { get; set; } = "Other";
        public string? DocumentNumber { get; set; }
        public DateTime? IssueDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string? Notes { get; set; }

        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/octet-stream";
        public long SizeBytes { get; set; }
        public byte[] Content { get; set; } = Array.Empty<byte>();

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public string? UploadedByName { get; set; }
    }

    public static class RecordDocumentEntities
    {
        public const string Tenant = "Tenant";
        public const string Lease = "Lease";
    }

    public static class RecordDocumentTypes
    {
        public const string EmiratesId = "Emirates ID";
        public const string Passport = "Passport";
        public const string AttestedContract = "Attested contract (Municipality)";

        public static readonly string[] ForTenant = { EmiratesId, Passport, "Residence visa", "Trade licence", "Other" };
        public static readonly string[] ForLease = { AttestedContract, "Ejari / registration certificate", "Signed contract", "Other" };

        public static string[] For(string entityType) =>
            entityType == RecordDocumentEntities.Lease ? ForLease : ForTenant;

        /// <summary>Documents the system can read automatically (OCR).</summary>
        public static bool IsReadable(string type) => type is EmiratesId or Passport;
    }
}
