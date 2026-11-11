namespace RealEstateManager.Domain
{
    /// <summary>
    /// The handover of a unit when its contract ends. Three people have a say, in order:
    /// whoever inspects the unit against the checklist, the Maintenance Manager who prices
    /// any damage, and the accounts, who settle the security deposit - applied to what the
    /// tenant still owes, the rest refunded. Settling closes the contract and frees the unit.
    /// </summary>
    public class LeaseHandover
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int LeaseId { get; set; }
        public Lease? Lease { get; set; }

        /// <summary>Reference shown on the report, e.g. HND-2026-0001.</summary>
        public string HandoverNumber { get; set; } = string.Empty;

        /// <summary>See <see cref="HandoverReasons"/>.</summary>
        public string Reason { get; set; } = HandoverReasons.EndOfContract;

        /// <summary>The day the tenant gives the unit back.</summary>
        public DateTime HandoverDate { get; set; }

        /// <summary>See <see cref="HandoverStatuses"/>.</summary>
        public string Status { get; set; } = HandoverStatuses.Open;

        // ---- Step 1: the inspection -----------------------------------------
        public int? KeysReturned { get; set; }
        public string? ElectricityReading { get; set; }
        public string? WaterReading { get; set; }
        public string? InspectionNotes { get; set; }
        public DateTime? InspectedAt { get; set; }
        public string? InspectedByName { get; set; }

        // ---- Step 2: the Maintenance Manager --------------------------------
        public string? MaintenanceNotes { get; set; }
        public DateTime? MaintenanceSignedAt { get; set; }
        public string? MaintenanceSignedByName { get; set; }

        // ---- Step 3: the accounts -------------------------------------------

        /// <summary>The invoice raised to the tenant for the damage charges.</summary>
        public int? ChargesInvoiceId { get; set; }
        public string? AccountsNotes { get; set; }

        /// <summary>The settlement as it was made: the deposit held, what of it paid invoices, what was refunded, what the tenant still owes.</summary>
        public decimal DepositHeld { get; set; }
        public decimal DepositApplied { get; set; }
        public decimal DepositRefunded { get; set; }
        public decimal BalanceStillDue { get; set; }

        public int? RefundJournalEntryId { get; set; }
        public DateTime? SettledAt { get; set; }
        public string? SettledByName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByName { get; set; }

        public List<LeaseHandoverItem> Items { get; set; } = new();
        public List<LeaseHandoverCharge> Charges { get; set; } = new();

        public bool IsInspected => InspectedAt.HasValue;
        public bool IsMaintenanceSigned => MaintenanceSignedAt.HasValue;
        public bool IsOpen => Status == HandoverStatuses.Open;
    }

    /// <summary>One line of the checklist as answered on a handover.</summary>
    public class LeaseHandoverItem
    {
        public int Id { get; set; }
        public int LeaseHandoverId { get; set; }
        public LeaseHandover? LeaseHandover { get; set; }

        public int SortOrder { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>See <see cref="HandoverItemResults"/>.</summary>
        public string Result { get; set; } = HandoverItemResults.Pending;
        public string? Notes { get; set; }
    }

    /// <summary>Something the tenant is charged for: damage beyond fair wear, a missing key, cleaning.</summary>
    public class LeaseHandoverCharge
    {
        public int Id { get; set; }
        public int LeaseHandoverId { get; set; }
        public LeaseHandover? LeaseHandover { get; set; }

        public string Description { get; set; } = string.Empty;

        /// <summary>Before VAT.</summary>
        public decimal Amount { get; set; }
        public string? CreatedByName { get; set; }
    }

    /// <summary>A line of the company's own handover checklist, copied onto each new handover.</summary>
    public class HandoverChecklistItem
    {
        public const int MaxNameLength = 150;

        public int Id { get; set; }
        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>What a company starts with; it changes the list to its own.</summary>
        public static readonly string[] Standard =
        {
            "All keys and access cards returned",
            "Parking cards and remote controls returned",
            "Electricity and water final bills settled by the tenant",
            "Electricity and water meter readings taken",
            "Walls and paint in good condition",
            "Floors, tiles and carpets in good condition",
            "Doors, windows and locks working",
            "Kitchen cabinets, worktop and sink in good condition",
            "Bathrooms and sanitary ware in good condition",
            "Air conditioning units working and clean",
            "Lights, switches and sockets working",
            "Unit cleaned and free of the tenant's belongings"
        };
    }

    public static class HandoverReasons
    {
        public const string EndOfContract = "EndOfContract";
        public const string EarlyTermination = "EarlyTermination";

        public static readonly string[] All = { EndOfContract, EarlyTermination };

        public static string Describe(string reason) => reason switch
        {
            EarlyTermination => "Early termination",
            _ => "End of contract"
        };
    }

    public static class HandoverStatuses
    {
        public const string Open = "Open";
        public const string Settled = "Settled";
        public const string Cancelled = "Cancelled";

        public static string BadgeClass(string status) => status switch
        {
            Settled => "bg-success",
            Cancelled => "bg-secondary",
            _ => "bg-warning text-dark"
        };
    }

    public static class HandoverItemResults
    {
        public const string Pending = "Pending";
        public const string Ok = "Ok";
        public const string Issue = "Issue";
        public const string NotApplicable = "NotApplicable";

        public static readonly string[] All = { Pending, Ok, Issue, NotApplicable };

        public static string Describe(string result) => result switch
        {
            Ok => "OK",
            Issue => "Problem found",
            NotApplicable => "Not applicable",
            _ => "Not checked yet"
        };
    }

    /// <summary>How a receipt was paid when it was not money coming in.</summary>
    public static class ReceiptMethods
    {
        /// <summary>The tenant's security deposit applied to an invoice at the handover.</summary>
        public const string SecurityDeposit = "Security deposit";
    }
}
