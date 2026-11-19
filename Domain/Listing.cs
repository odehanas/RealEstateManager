namespace RealEstateManager.Domain
{
    /// <summary>
    /// The company's public vacancy website: a page anyone can open, without signing in,
    /// that shows the units the office has chosen to advertise while they are vacant.
    /// Each company has its own, at /vacancies/{Slug}; nothing is public until it is enabled.
    /// </summary>
    public class ListingSite
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>The last part of the public address: lower-case letters, digits and hyphens.</summary>
        public string Slug { get; set; } = string.Empty;

        public bool IsEnabled { get; set; }

        /// <summary>The name shown at the top of the page. Empty means the company's name.</summary>
        public string? DisplayName { get; set; }
        public string? DisplayNameAr { get; set; }

        public string? Intro { get; set; }
        public string? IntroAr { get; set; }

        public string? ContactPhone { get; set; }

        /// <summary>International format, digits only matter: the "Chat on WhatsApp" button opens a chat with it.</summary>
        public string? WhatsAppNumber { get; set; }

        public string? ContactEmail { get; set; }

        /// <summary>Off: the rent is not printed and the visitor is asked to enquire.</summary>
        public bool ShowRent { get; set; } = true;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>A photo of a unit for its advertisement. The first one is the cover.</summary>
    public class UnitPhoto
    {
        public const int MaxPerUnit = 12;
        public const int MaxFileBytes = 5 * 1024 * 1024;

        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int UnitId { get; set; }
        public Unit? Unit { get; set; }

        public int SortOrder { get; set; }

        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "image/jpeg";
        public long SizeBytes { get; set; }

        /// <summary>The image. Loaded only when the picture itself is requested.</summary>
        public byte[] Content { get; set; } = Array.Empty<byte>();

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public string? UploadedByName { get; set; }
    }

    /// <summary>What a visitor of the vacancy website asks - a lead for the leasing office.</summary>
    public class UnitEnquiry
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>The unit asked about; empty for a general enquiry.</summary>
        public int? UnitId { get; set; }
        public Unit? Unit { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Message { get; set; }

        /// <summary>The language the visitor was reading: "en" or "ar".</summary>
        public string Language { get; set; } = "en";

        /// <summary>See <see cref="EnquiryStatuses"/>.</summary>
        public string Status { get; set; } = EnquiryStatuses.New;

        /// <summary>The office's own note: what was agreed, when the viewing is.</summary>
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? HandledAt { get; set; }
        public string? HandledByName { get; set; }
    }

    public static class EnquiryStatuses
    {
        public const string New = "New";
        public const string Contacted = "Contacted";
        public const string Closed = "Closed";

        public static readonly string[] All = { New, Contacted, Closed };

        public static string Clean(string? value) =>
            All.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? New;
    }
}
