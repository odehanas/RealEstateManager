namespace RealEstateManager.Models
{
    public class LeaseCreateViewModel
    {
        public int UnitId { get; set; }
        public int TenantId { get; set; }

        public string ContractNumber { get; set; } = string.Empty;

        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime EndDate { get; set; } = DateTime.Today.AddYears(1).AddDays(-1);

        public decimal AnnualRent { get; set; }
        public int BillingFrequencyMonths { get; set; } = 1;

        // VAT is derived from the unit / property. These two exist only to record a
        // deliberate exception, and the reason is mandatory when one is used.
        public string? VatCategoryOverride { get; set; }
        public string? VatOverrideReason { get; set; }

        /// <summary>Category derived from the selected unit, shown read-only on the form.</summary>
        public string? DerivedVatCategory { get; set; }

        // Retained so existing screens bind without change; no longer the source of truth.
        public bool IsVatApplicable { get; set; } = false;
        public decimal VatRate { get; set; } = 0m;

        public decimal SecurityDepositAmount { get; set; } = 0m;
        public string? Notes { get; set; }
    }
}