using System.ComponentModel.DataAnnotations;

namespace RealEstateManager.Models
{
    public class CompanyListItemViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string BaseCurrency { get; set; } = string.Empty;
        public int FiscalYearStartMonth { get; set; }
        public DateTime CreatedAt { get; set; }
        public int UsersCount { get; set; }
        public string SubscriptionStatus { get; set; } = string.Empty;
        public DateTime? AccessEndDate { get; set; }
        public int DaysRemaining { get; set; }
        public int ActivationCodesIssued { get; set; }
        public int ActivationCodesUsed { get; set; }
    }

    public class CompanyListPageViewModel
    {
        public List<CompanyListItemViewModel> Items { get; set; } = new();
        public List<CompanySubscriptionAlertItemViewModel> ExpiredCompanies { get; set; } = new();
        public List<CompanySubscriptionAlertItemViewModel> ExpiringSoonCompanies { get; set; } = new();
    }

    public class CompanySubscriptionAlertItemViewModel
    {
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string SubscriptionStatus { get; set; } = string.Empty;
        public DateTime? AccessEndDate { get; set; }
        public int DaysRemaining { get; set; }
    }

    public class CompanyFormViewModel
    {
        public int? Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(10)]
        public string BaseCurrency { get; set; } = "AED";

        [Range(1, 12)]
        public int FiscalYearStartMonth { get; set; } = 1;

        public string SubscriptionStatus { get; set; } = string.Empty;
        public DateTime? TrialStartDate { get; set; }
        public DateTime? TrialEndDate { get; set; }
        public DateTime? SubscriptionEndDate { get; set; }
        public DateTime? AccessEndDate { get; set; }
        public int DaysRemaining { get; set; }
        public bool IsSuspended { get; set; }

        /// <summary>The latest suspension or reactivation, with who did it and why. Owner-only.</summary>
        public string? SubscriptionNotes { get; set; }

        [Range(1, 3650)]
        [Display(Name = "Code Duration (Days)")]
        public int ActivationCodeDurationDays { get; set; } = 30;

        [StringLength(300)]
        [Display(Name = "Code Notes")]
        public string? ActivationCodeNotes { get; set; }

        public List<SubscriptionActivationCodeListItemViewModel> ActivationCodes { get; set; } = new();
    }
}
