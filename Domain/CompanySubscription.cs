namespace RealEstateManager.Domain
{
    public class CompanySubscription
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public DateTime TrialStartDate { get; set; }
        public DateTime TrialEndDate { get; set; }
        public DateTime? SubscriptionEndDate { get; set; }
        public bool IsSuspended { get; set; }
        public DateTime? LastActivatedAt { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
