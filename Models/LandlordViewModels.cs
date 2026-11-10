using System.ComponentModel.DataAnnotations;
using RealEstateManager.Domain;

namespace RealEstateManager.Models
{
    public class LandlordListItemViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Trn { get; set; }
        public string? Phone { get; set; }
        public string? Iban { get; set; }
        public string DefaultCollectionMode { get; set; } = CollectionModes.Company;
        public bool IsActive { get; set; }
        public int PropertyCount { get; set; }
    }

    public class LandlordEditViewModel
    {
        public int Id { get; set; }

        [Display(Name = "Name")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "TRN")]
        public string? Trn { get; set; }

        [Display(Name = "Email")]
        public string? Email { get; set; }

        [Display(Name = "Phone")]
        public string? Phone { get; set; }

        [Display(Name = "Bank")]
        public string? BankName { get; set; }

        [Display(Name = "Account name")]
        public string? AccountName { get; set; }

        [Display(Name = "IBAN")]
        public string? Iban { get; set; }

        [Display(Name = "Who holds the rent, by default")]
        public string DefaultCollectionMode { get; set; } = CollectionModes.Company;

        [Display(Name = "Notes")]
        public string? Notes { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;
    }

    public class BankAccountListItemViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? BankName { get; set; }
        public string? Iban { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string Purpose { get; set; } = BankAccountPurposes.Operating;
        public string? LandlordName { get; set; }
        public bool IsActive { get; set; }
        public decimal Balance { get; set; }
    }

    public class BankAccountEditViewModel
    {
        public int Id { get; set; }

        [Display(Name = "Name")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Bank")]
        public string? BankName { get; set; }

        [Display(Name = "IBAN")]
        public string? Iban { get; set; }

        [Display(Name = "Ledger account")]
        public int AccountId { get; set; }

        [Display(Name = "What it is for")]
        public string Purpose { get; set; } = BankAccountPurposes.Operating;

        [Display(Name = "Kept for one landlord only")]
        public int? LandlordId { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;
    }
}
