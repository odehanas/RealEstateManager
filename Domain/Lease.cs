namespace RealEstateManager.Domain
{
    public class Lease
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int UnitId { get; set; }
        public Unit? Unit { get; set; }

        public int TenantId { get; set; }
        public Tenant? Tenant { get; set; }

        public string ContractNumber { get; set; } = string.Empty;

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public decimal AnnualRent { get; set; } = 0m;
        public int BillingFrequencyMonths { get; set; } = 1;

        // VAT treatment for this lease. Derived from the unit (falling back to the
        // property) when the lease is created, and overridable with a recorded reason.
        public string VatCategory { get; set; } = VatCategories.Standard;

        /// <summary>Set when the category was changed away from the derived default.</summary>
        public string? VatOverrideReason { get; set; }

        // Kept in step with VatCategory by VatDeterminationService. IsVatApplicable is
        // true only for Standard; VatRate carries the rate actually applied.
        public bool IsVatApplicable { get; set; } = false;
        public decimal VatRate { get; set; } = 0m;

        public decimal SecurityDepositAmount { get; set; } = 0m;

        // Draft, Active, Terminated, Expired, Renewed
        public string Status { get; set; } = "Draft";

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}