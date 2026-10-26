namespace RealEstateManager.Domain
{
    /// <summary>
    /// A building under construction. Its costs are collected in a work-in-progress
    /// account chosen by what the building is for, because the accounting standards
    /// classify it by intended use from the first dirham:
    ///
    ///   For sale      13100 Properties Under Development - For Sale   inventory (IAS 2)
    ///   For rental    15500 Investment Property Under Construction    investment property (IAS 40)
    ///   Own use       15600 Capital Work in Progress                  property, plant and equipment (IAS 16)
    ///
    /// Every cost line carries the project and a cost category, so the project's cost
    /// to date is read straight from the ledger and always agrees with it. On completion
    /// the cost is transferred to inventory, investment property or fixed assets.
    /// </summary>
    public class DevelopmentProject
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>DEV-0001, per company.</summary>
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>The property record the project builds - its plot, title deed and permits.</summary>
        public int? PropertyId { get; set; }
        public Property? Property { get; set; }

        public string IntendedUse { get; set; } = DevelopmentIntendedUses.Sale;
        public string Status { get; set; } = DevelopmentProjectStatuses.Planning;

        /// <summary>The account the project's costs are collected in.</summary>
        public int WipAccountId { get; set; }
        public Account? WipAccount { get; set; }

        public DateTime? StartDate { get; set; }
        public DateTime? ExpectedCompletionDate { get; set; }

        /// <summary>The date on the completion certificate.</summary>
        public DateTime? CompletionDate { get; set; }
        public string? CompletionCertificateNumber { get; set; }

        /// <summary>
        /// How input VAT on this project's costs is treated, applied to every bill line
        /// tagged to it: Taxable (commercial, or first supply of new residential that is
        /// zero rated - recovered), Exempt (residential to be let or resold later - not
        /// recovered, so it becomes part of the cost) or Residual (mixed - apportioned).
        /// Null leaves it to each bill line.
        /// </summary>
        public string? InputTaxAttribution { get; set; }

        public int? MainContractorVendorId { get; set; }
        public Vendor? MainContractorVendor { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<DevelopmentBudgetLine> BudgetLines { get; set; } = new List<DevelopmentBudgetLine>();

        public decimal TotalBudget => BudgetLines.Sum(x => x.Amount);

        /// <summary>A project not yet completed or cancelled.</summary>
        public bool IsOpen => Status is DevelopmentProjectStatuses.Planning or DevelopmentProjectStatuses.UnderConstruction;

        /// <summary>Costs may still be tagged to it: anything but a cancelled project.</summary>
        public bool TakesCosts => Status != DevelopmentProjectStatuses.Cancelled;
    }

    /// <summary>The budget for one cost category of a project.</summary>
    public class DevelopmentBudgetLine
    {
        public int Id { get; set; }

        public int DevelopmentProjectId { get; set; }
        public DevelopmentProject? DevelopmentProject { get; set; }

        public string CostCategory { get; set; } = DevelopmentCostCategories.Construction;
        public decimal Amount { get; set; }
    }

    public static class DevelopmentIntendedUses
    {
        public const string Sale = "Sale";
        public const string Rental = "Rental";
        public const string OwnUse = "OwnUse";

        public static readonly string[] All = { Sale, Rental, OwnUse };

        public static string Describe(string use) => use switch
        {
            Sale => "For sale (inventory)",
            Rental => "For rental (investment property)",
            OwnUse => "Own use (fixed asset)",
            _ => use
        };

        /// <summary>The work-in-progress account a project of this use collects its cost in.</summary>
        public static string DefaultWipAccountCode(string use) => use switch
        {
            Rental => "15500",
            OwnUse => "15600",
            _ => "13100"
        };
    }

    public static class DevelopmentProjectStatuses
    {
        public const string Planning = "Planning";
        public const string UnderConstruction = "UnderConstruction";
        public const string Completed = "Completed";
        public const string Cancelled = "Cancelled";

        public static string Describe(string status) => status switch
        {
            UnderConstruction => "Under construction",
            _ => status
        };
    }

    /// <summary>
    /// What a development cost was for. Only costs directly attributable to bringing the
    /// building to completion belong in the project: marketing, sales commissions and
    /// general overheads are expenses, never project cost.
    /// </summary>
    public static class DevelopmentCostCategories
    {
        public const string Land = "Land";
        public const string Design = "Design";
        public const string Permits = "Permits";
        public const string Construction = "Construction";
        public const string Infrastructure = "Infrastructure";
        public const string Supervision = "Supervision";
        public const string FinanceCosts = "FinanceCosts";
        public const string Other = "Other";

        public static readonly string[] All =
        {
            Land, Design, Permits, Construction, Infrastructure, Supervision, FinanceCosts, Other
        };

        public static string Describe(string? category) => category switch
        {
            Land => "Land and acquisition costs",
            Design => "Design and consultancy",
            Permits => "Permits and authority fees",
            Construction => "Construction - contractors",
            Infrastructure => "Infrastructure and utility connections",
            Supervision => "Project management and supervision",
            FinanceCosts => "Borrowing costs (capitalised)",
            Other => "Other directly attributable costs",
            null or "" => "(no category)",
            _ => category
        };
    }
}
