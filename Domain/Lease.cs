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

        /// <summary>
        /// Annual for an ordinary tenancy; Monthly, Weekly or Daily for a stay priced at
        /// <see cref="RentRate"/> and billed for exactly its days. See <see cref="RentBases"/>.
        /// For a stay, AnnualRent holds the rate as a yearly figure, for the rent reports.
        /// </summary>
        public string RentBasis { get; set; } = RentBases.Annual;

        /// <summary>The rate per day, week or month of a stay; 0 for an annual contract.</summary>
        public decimal RentRate { get; set; } = 0m;

        /// <summary>"60,000.00 per year" or "350.00 per day".</summary>
        public string RentDescription() => RentBases.IsStay(RentBasis)
            ? $"{RentRate:N2} per {RentBases.Unit(RentBasis)}"
            : $"{AnnualRent:N2} per year";

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

        /// <summary>See <see cref="LeaseStatuses"/>.</summary>
        public string Status { get; set; } = LeaseStatuses.Draft;

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Who created the contract - who may therefore not approve it.</summary>
        public int? CreatedByAppUserId { get; set; }
    }

    /// <summary>
    /// A contract starts as a Draft, which can be corrected freely and has no financial
    /// effect. Activating it - straight away, or through approval when the company
    /// requires it - creates its invoices and income schedule and locks its terms.
    /// </summary>
    public static class LeaseStatuses
    {
        public const string Draft = "Draft";
        public const string PendingApproval = "PendingApproval";
        public const string Active = "Active";
        public const string Terminated = "Terminated";
        public const string Expired = "Expired";
        public const string Renewed = "Renewed";

        /// <summary>Not yet in force: no invoices, not shown to the tenant, terms still editable.</summary>
        public static bool IsBeforeActivation(string status) => status is Draft or PendingApproval;

        public static string Describe(string status) => status switch
        {
            PendingApproval => "Awaiting approval",
            _ => status
        };
    }
}