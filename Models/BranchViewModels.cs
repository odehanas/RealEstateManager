using System.ComponentModel.DataAnnotations;

namespace RealEstateManager.Models
{
    public class BranchListItemViewModel
    {
        public int CompanyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsMother { get; set; }
        public DateTime CreatedAt { get; set; }
        public int UsersCount { get; set; }

        /// <summary>Whether the person looking at the list can switch to this company.</summary>
        public bool HasAccess { get; set; }
    }

    public class BranchListViewModel
    {
        public int CurrentCompanyId { get; set; }
        public string MotherCompanyName { get; set; } = string.Empty;
        public List<BranchListItemViewModel> Companies { get; set; } = new();
        public bool CanOpenBranch { get; set; }
    }

    public class BranchFormViewModel
    {
        public string MotherCompanyName { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        [Display(Name = "Branch name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gives everyone who works in the mother company the same access to the branch.
        /// Off: only the person opening the branch can enter it, and adds its users after.
        /// </summary>
        public bool GiveMotherCompanyUsersAccess { get; set; } = true;
    }
}
