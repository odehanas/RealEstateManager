using System.ComponentModel.DataAnnotations;

namespace RealEstateManager.Models
{
    public class VatSettingsViewModel
    {
        /// <summary>How costs of owners' managed properties are passed on: Recharge or Disbursement.</summary>
        public string OwnerCostTreatment { get; set; } = RealEstateManager.Domain.OwnerCostTreatments.Recharge;

        public string CompanyName { get; set; } = string.Empty;

        [Display(Name = "Registered Legal Name")]
        [StringLength(200)]
        public string? LegalName { get; set; }

        [Display(Name = "TRN (Tax Registration Number)")]
        [StringLength(30)]
        public string? TrnNumber { get; set; }

        [Display(Name = "Address Line 1")]
        [StringLength(200)]
        public string? AddressLine1 { get; set; }

        [Display(Name = "Address Line 2")]
        [StringLength(200)]
        public string? AddressLine2 { get; set; }

        [Display(Name = "City / Emirate")]
        [StringLength(100)]
        public string? City { get; set; }

        [Display(Name = "Country")]
        [StringLength(100)]
        public string? Country { get; set; }

        [Display(Name = "Phone")]
        [StringLength(50)]
        public string? Phone { get; set; }

        [Display(Name = "Email")]
        [StringLength(200)]
        public string? Email { get; set; }

        [Display(Name = "Provisional Input Tax Recovery Rate (%)")]
        public decimal ProvisionalVatRecoveryRate { get; set; } = 100m;

        // Readiness counters, display only.
        public int StandardRatedProperties { get; set; }
        public int ExemptProperties { get; set; }
        public int TenantsWithTrn { get; set; }
        public int TotalTenants { get; set; }
        public int TaxInvoicesIssued { get; set; }
        public int BillsAwaitingAttribution { get; set; }

        public bool IsReadyToIssueTaxInvoices =>
            !string.IsNullOrWhiteSpace(TrnNumber) && !string.IsNullOrWhiteSpace(AddressLine1);
    }
}
