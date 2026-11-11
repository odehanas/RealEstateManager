namespace RealEstateManager.Domain
{
    /// <summary>
    /// A service the company sells to its tenants, on the company's own price list:
    /// special maintenance (work that is not the landlord's to do free of charge - a
    /// tenant's own appliance, an extra key, repainting to the tenant's taste) or a
    /// special request the company may subcontract (cleaning, pest control, moving help).
    ///
    /// The tenant orders it, is invoiced for it, and - where the service says so - pays
    /// before the work is done. The income is the company's own, whoever owns the
    /// building, like a cheque charge.
    /// </summary>
    public class ServiceCatalogItem
    {
        public const int MaxNameLength = 100;

        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>See <see cref="ServiceKinds"/>.</summary>
        public string Kind { get; set; } = ServiceKinds.SpecialMaintenance;

        /// <summary>The kind of maintenance work its jobs are sorted under (Cleaning, Plumbing...).</summary>
        public string? Category { get; set; }

        /// <summary>What the tenant reads when choosing: what is included.</summary>
        public string? Description { get; set; }

        /// <summary>What the tenant is charged, before VAT. Zero: priced when the invoice is issued.</summary>
        public decimal SellingPrice { get; set; }

        /// <summary>What the company expects the service to cost it: the subcontractor's price, or materials and time.</summary>
        public decimal ExpectedCost { get; set; }

        /// <summary>The income account credited; empty for the company's Other Income account.</summary>
        public int? IncomeAccountId { get; set; }
        public Account? IncomeAccount { get; set; }

        /// <summary>Done by an outside supplier rather than the company's own team.</summary>
        public bool Subcontracted { get; set; }

        /// <summary>The work does not start until the tenant has paid the invoice.</summary>
        public bool PayBeforeWork { get; set; } = true;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByName { get; set; }
    }

    public static class ServiceKinds
    {
        public const string SpecialMaintenance = "SpecialMaintenance";
        public const string SpecialRequest = "SpecialRequest";

        public static readonly string[] All = { SpecialMaintenance, SpecialRequest };

        public static string Describe(string? kind) => kind switch
        {
            SpecialMaintenance => "Special maintenance",
            SpecialRequest => "Special request",
            _ => "Paid service"
        };
    }
}
