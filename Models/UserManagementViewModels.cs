using System.ComponentModel.DataAnnotations;

namespace RealEstateManager.Models
{
    public class AppUserListItemViewModel
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsSystemAdmin { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public string CompaniesDisplay { get; set; } = string.Empty;
        public string PermissionsDisplay { get; set; } = string.Empty;
    }

    public class AppUserFormViewModel
    {
        public int? Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string DisplayName { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        public string? Password { get; set; }

        [DataType(DataType.Password)]
        [Compare(nameof(Password))]
        public string? ConfirmPassword { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsSystemAdmin { get; set; }

        [Required]
        public int? DefaultCompanyId { get; set; }

        public List<SelectableCompanyViewModel> Companies { get; set; } = new();
        public List<SelectablePermissionGroupViewModel> PermissionGroups { get; set; } = new();
    }

    public class SelectableCompanyViewModel
    {
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }

    public class SelectablePermissionGroupViewModel
    {
        public string GroupName { get; set; } = string.Empty;
        public List<SelectablePermissionViewModel> Permissions { get; set; } = new();
    }

    public class SelectablePermissionViewModel
    {
        public string PermissionKey { get; set; } = string.Empty;
        public string PermissionName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }
}
