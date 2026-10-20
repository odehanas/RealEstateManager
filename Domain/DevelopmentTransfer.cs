namespace RealEstateManager.Domain
{
    /// <summary>
    /// Moving a completed project's cost out of work in progress to where the finished
    /// building now belongs. The cost is shared between the building's units by area,
    /// and each unit goes where its use says:
    ///
    ///   For sale      13200 Completed Properties Held for Sale   inventory, at cost, per unit
    ///   For rental    15100 Investment Property                  cost model: land is not depreciated,
    ///                                                               the building is, over its useful life
    ///   Own use       15200 Fixed Assets                         land not depreciated, building depreciated
    ///
    /// The building part of rental and own-use units is entered in the fixed asset register,
    /// so the monthly depreciation run picks it up. A transfer moves whatever is still in
    /// work in progress; a late invoice after it is moved by a second transfer.
    /// </summary>
    public class DevelopmentTransfer
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int DevelopmentProjectId { get; set; }
        public DevelopmentProject? DevelopmentProject { get; set; }

        public DateTime TransferDate { get; set; }

        /// <summary>Land and acquisition costs moved - never depreciated.</summary>
        public decimal LandCost { get; set; }

        /// <summary>Everything else moved - the building.</summary>
        public decimal BuildingCost { get; set; }

        public decimal TotalCost => LandCost + BuildingCost;

        /// <summary>Useful life of the building part of rental and own-use units.</summary>
        public int BuildingUsefulLifeYears { get; set; }

        public int? RentalFixedAssetId { get; set; }
        public FixedAsset? RentalFixedAsset { get; set; }

        public int? OwnUseFixedAssetId { get; set; }
        public FixedAsset? OwnUseFixedAsset { get; set; }

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByName { get; set; }

        public ICollection<DevelopmentTransferLine> Lines { get; set; } = new List<DevelopmentTransferLine>();
    }

    /// <summary>One unit's share of a transfer.</summary>
    public class DevelopmentTransferLine
    {
        public int Id { get; set; }

        public int DevelopmentTransferId { get; set; }
        public DevelopmentTransfer? DevelopmentTransfer { get; set; }

        public int UnitId { get; set; }
        public Unit? Unit { get; set; }

        /// <summary>Sale, Rental or OwnUse - see <see cref="DevelopmentIntendedUses"/>.</summary>
        public string IntendedUse { get; set; } = DevelopmentIntendedUses.Sale;

        /// <summary>The area the share was worked out on.</summary>
        public decimal AreaSqFt { get; set; }

        public decimal LandCost { get; set; }
        public decimal BuildingCost { get; set; }
        public decimal TotalCost => LandCost + BuildingCost;
    }
}
