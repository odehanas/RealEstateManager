namespace RealEstateManager.Domain
{
    /// <summary>
    /// A building under the Capital Assets Scheme (VAT Executive Regulation): a capital asset
    /// of AED 5 million or more before VAT. The input tax recovered when it was built or bought
    /// is reviewed every year for ten years; if the building is used more for exempt supplies
    /// than when the tax was recovered (residential leases after the zero-rated first lease),
    /// part of that tax is paid back - and if it is used more for taxable supplies, more can
    /// be recovered.
    ///
    ///   yearly adjustment = total input tax x (taxable use this year - taxable use at recovery) / 10
    /// </summary>
    public class CapitalAssetVatRecord
    {
        public const decimal Threshold = 5_000_000m;
        public const int AdjustmentYears = 10;

        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Name { get; set; } = string.Empty;

        public int PropertyId { get; set; }
        public Property? Property { get; set; }

        public int? DevelopmentProjectId { get; set; }
        public DevelopmentProject? DevelopmentProject { get; set; }

        /// <summary>Cost of the asset before VAT.</summary>
        public decimal AssetValue { get; set; }

        /// <summary>All input tax charged on it.</summary>
        public decimal InputVat { get; set; }

        /// <summary>The input tax recovered at the time.</summary>
        public decimal RecoveredVat { get; set; }

        /// <summary>Taxable use when the tax was recovered: recovered / input, in %.</summary>
        public decimal OriginalRecoveryPercent { get; set; }

        /// <summary>The first day of the first adjustment year - normally the date the building is first used.</summary>
        public DateTime StartDate { get; set; }

        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<CapitalAssetVatAdjustment> Adjustments { get; set; } = new List<CapitalAssetVatAdjustment>();

        public DateTime EndDate => StartDate.AddYears(AdjustmentYears).AddDays(-1);
    }

    /// <summary>One year's review of a capital asset's use.</summary>
    public class CapitalAssetVatAdjustment
    {
        public int Id { get; set; }

        public int CapitalAssetVatRecordId { get; set; }
        public CapitalAssetVatRecord? CapitalAssetVatRecord { get; set; }

        /// <summary>1 to 10.</summary>
        public int YearNumber { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        /// <summary>Share of the building used for taxable (standard or zero-rated) supplies in the year, in %.</summary>
        public decimal TaxableUsePercent { get; set; }

        /// <summary>Positive: more tax recoverable. Negative: tax to pay back.</summary>
        public decimal Adjustment { get; set; }

        /// <summary>The date the adjustment is accounted - it goes in that period's VAT return.</summary>
        public DateTime AdjustmentDate { get; set; }

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
