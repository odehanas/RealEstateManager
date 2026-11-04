using System.ComponentModel.DataAnnotations;

namespace RealEstateManager.Models
{
    public class OwnerWithdrawalLocationViewModel
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Code")]
        [StringLength(30)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Location Name")]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Description")]
        [StringLength(500)]
        public string? Description { get; set; }

        [Display(Name = "Sort Order")]
        public int SortOrder { get; set; } = 0;

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;
    }

    public class OwnerWithdrawalLocationListItemViewModel
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }

        /// <summary>Number of posted journal lines already using this location.</summary>
        public int UsageCount { get; set; }

        /// <summary>Net amount posted against this location (debits less credits).</summary>
        public decimal NetAmount { get; set; }
    }
}
