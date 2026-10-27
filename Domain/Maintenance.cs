namespace RealEstateManager.Domain
{
    /// <summary>
    /// Material used on a maintenance job: taken from the maintenance store (posted
    /// out of the Maintenance Materials Stock account) or bought for the job (booked
    /// through the vendor bill or petty cash voucher that paid for it).
    /// </summary>
    public class MaintenanceMaterial
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int TenantServiceRequestId { get; set; }
        public TenantServiceRequest? ServiceRequest { get; set; }

        public DateTime MaterialDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; } = 1m;
        public decimal UnitCost { get; set; }
        public decimal Amount { get; set; }

        /// <summary>See <see cref="MaintenanceMaterialSources"/>.</summary>
        public string Source { get; set; } = MaintenanceMaterialSources.Stock;

        /// <summary>The bill or receipt it was bought on, for a purchased item.</summary>
        public string? Reference { get; set; }

        /// <summary>"Owner" for a managed property, "Company" for an owned one.</summary>
        public string ChargedTo { get; set; } = "Company";

        /// <summary>The entry that took a stock item out of the store.</summary>
        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByName { get; set; }
    }

    public static class MaintenanceMaterialSources
    {
        public const string Stock = "Stock";
        public const string Purchased = "Purchased";

        public static readonly string[] All = { Stock, Purchased };
    }

    /// <summary>
    /// The maintenance store account and the target times the maintenance dashboard
    /// measures jobs against.
    /// </summary>
    public static class MaintenanceStandards
    {
        /// <summary>Asset account holding materials kept for maintenance (not a full inventory).</summary>
        public const string StockAccountCode = "11650";
        public const string StockAccountName = "Maintenance Materials Stock";

        /// <summary>Company expense for maintenance of its own properties.</summary>
        public const string MaintenanceExpenseAccountCode = "52100";

        /// <summary>Hours to the first response, and to the job being resolved, by priority.</summary>
        public static (int ResponseHours, int ResolveHours) TargetFor(string? priority) => priority switch
        {
            ServiceRequestPriorities.Urgent => (2, 24),
            ServiceRequestPriorities.High => (8, 72),
            ServiceRequestPriorities.Low => (48, 240),
            _ => (24, 120)
        };
    }
}
