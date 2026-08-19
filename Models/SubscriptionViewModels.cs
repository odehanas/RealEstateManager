using System.ComponentModel.DataAnnotations;

namespace RealEstateManager.Models
{
    public class CurrentSubscriptionViewModel
    {
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string StatusLabel { get; set; } = string.Empty;
        public bool IsInTrial { get; set; }
        public bool IsExpired { get; set; }
        public bool IsSuspended { get; set; }
        public int DaysRemaining { get; set; }
        public DateTime TrialStartDate { get; set; }
        public DateTime TrialEndDate { get; set; }
        public DateTime? SubscriptionEndDate { get; set; }
        public DateTime? AccessEndDate { get; set; }
        public string? Notes { get; set; }

        [Display(Name = "Activation Code")]
        public string ActivationCode { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
    }

    public class SubscriptionActivationCodeListItemViewModel
    {
        public string Code { get; set; } = string.Empty;
        public int DurationDays { get; set; }
        public string? Notes { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime? ActivatedAt { get; set; }
        public bool IsCancelled { get; set; }
    }
}
