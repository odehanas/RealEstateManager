namespace RealEstateManager.Domain
{
    public class SubscriptionActivationCode
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Code { get; set; } = string.Empty;
        public int DurationDays { get; set; }
        public string? Notes { get; set; }
        public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ActivatedAt { get; set; }
        public bool IsCancelled { get; set; }
    }
}
