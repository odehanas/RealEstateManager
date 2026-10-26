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

        /// <summary>The standard role the permissions amount to, e.g. Manager (Approver), or Custom.</summary>
        public string RoleDisplay { get; set; } = string.Empty;
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

        /// <summary>Administers this company - manages its users. Only meaningful when selected.</summary>
        public bool IsCompanyAdmin { get; set; }
    }

    /// <summary>
    /// Whose users the person at the screen may manage. A System Admin manages everyone
    /// (CompanyIds is null). A company administrator manages only users whose every company
    /// is one they administer - never a System Admin, and never someone who also works for
    /// a company they do not administer, because resetting that person's password would
    /// open the other company to them.
    /// </summary>
    public sealed class UserAdminScope
    {
        public int UserId { get; init; }
        public bool IsSystemAdmin { get; init; }
        public bool IsPlatformOwner { get; init; }

        /// <summary>The companies this person administers; null means all of them.</summary>
        public HashSet<int>? CompanyIds { get; init; }

        public bool IsCompanyAdminOnly => CompanyIds != null;

        public bool CoversAll(IEnumerable<int> companyIds) =>
            CompanyIds == null || companyIds.All(CompanyIds.Contains);
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
