using System.ComponentModel.DataAnnotations;

namespace RealEstateManager.Models
{
    public class RegisterCompanyViewModel
    {
        [Required]
        [StringLength(200)]
        [Display(Name = "Company Name")]
        public string CompanyName { get; set; } = string.Empty;

        [Required]
        [StringLength(10)]
        [Display(Name = "Base Currency")]
        public string BaseCurrency { get; set; } = "AED";

        [Range(1, 12)]
        [Display(Name = "Fiscal Year Start Month")]
        public int FiscalYearStartMonth { get; set; } = 1;

        [Required]
        [StringLength(200)]
        [Display(Name = "Admin Full Name")]
        public string DisplayName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        [Display(Name = "Admin Username")]
        public string Username { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        [Compare(nameof(Password), ErrorMessage = "Password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
