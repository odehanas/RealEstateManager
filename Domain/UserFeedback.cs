namespace RealEstateManager.Domain
{
    public class UserFeedback
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int AppUserId { get; set; }
        public AppUser? AppUser { get; set; }

        public int? ReviewedByAppUserId { get; set; }
        public AppUser? ReviewedByAppUser { get; set; }

        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = "New";
        public string? AdminNotes { get; set; }
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReviewedAt { get; set; }
    }
}
