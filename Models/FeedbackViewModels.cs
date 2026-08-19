using System.ComponentModel.DataAnnotations;

namespace RealEstateManager.Models
{
    public class FeedbackFormViewModel
    {
        [Required]
        [StringLength(50)]
        public string Category { get; set; } = "General";

        [Required]
        [StringLength(200)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        [StringLength(2000)]
        [Display(Name = "Feedback Details")]
        public string Message { get; set; } = string.Empty;
    }

    public class FeedbackPageViewModel
    {
        public FeedbackFormViewModel Form { get; set; } = new();
        public List<FeedbackListItemViewModel> RecentItems { get; set; } = new();
    }

    public class FeedbackAdminFilterViewModel
    {
        public string? Status { get; set; }
        public string? Category { get; set; }
        public string? Username { get; set; }
        public bool OnlyOpenItems { get; set; }
    }

    public class FeedbackAdminListViewModel
    {
        public FeedbackAdminFilterViewModel Filter { get; set; } = new();
        public List<string> AvailableStatuses { get; set; } = new();
        public List<string> AvailableCategories { get; set; } = new();
        public int TotalItems { get; set; }
        public int NewItems { get; set; }
        public int OpenItems { get; set; }
        public int PlannedItems { get; set; }
        public int ClosedItems { get; set; }
        public List<FeedbackListItemViewModel> Items { get; set; } = new();
    }

    public class FeedbackListItemViewModel
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? AdminNotes { get; set; }
        public DateTime SubmittedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewedByName { get; set; }
    }

    public class FeedbackReviewViewModel
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }

        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "Reviewed";

        [StringLength(1000)]
        [Display(Name = "Admin Notes")]
        public string? AdminNotes { get; set; }

        public DateTime? ReviewedAt { get; set; }
        public string? ReviewedByName { get; set; }
    }
}
