namespace RealEstateManager.Domain
{
    public static class UnitSides
    {
        public static readonly string[] All = { "Front", "Back", "Left", "Right" };
    }

    public class Unit
    {
        public int Id { get; set; }

        public int PropertyId { get; set; }
        public Property? Property { get; set; }

        public string UnitNumber { get; set; } = string.Empty;
        public string? Floor { get; set; }

        /// <summary>
        /// The floor as a number, for the building view: 0 is the ground floor, negative is a
        /// basement. Empty means it is read from <see cref="Floor"/> ("G" = 0, "3" = 3, "B1" = -1).
        /// </summary>
        public int? FloorNumber { get; set; }

        /// <summary>Where the unit sits along its floor in the building view, from the left (1, 2, 3...).</summary>
        public int? FloorPosition { get; set; }

        /// <summary>
        /// The side of the building the unit faces, for the 3D view: Front, Back, Left or Right.
        /// Empty means the 3D view spreads the floor's units between front and back.
        /// </summary>
        public string? FloorSide { get; set; }
        public string? UsageType { get; set; }

        public decimal AreaSqFt { get; set; } = 0m;
        public decimal StandardAnnualRent { get; set; } = 0m;

        // VAT treatment for this unit. Null means inherit the property's category.
        public string? VatCategory { get; set; }

        // Vacant, Occupied, UnderMaintenance, Reserved
        public string Status { get; set; } = "Vacant";

        public bool IsActive { get; set; } = true;

        /// <summary>The unit's own title deed, for a unit registered separately (strata title).</summary>
        public string? TitleDeedNumber { get; set; }

        public int? ParkingBays { get; set; }

        /// <summary>The unit's electricity / water account, when metered separately.</summary>
        public string? UtilityAccountNumber { get; set; }

        /// <summary>
        /// The unit's first supply - its first sale or first lease. For a new residential unit
        /// that supply alone can be zero rated (within three years of completion); every later
        /// supply of it is exempt. Recorded when the first contract takes effect.
        /// </summary>
        public DateTime? FirstSupplyDate { get; set; }

        /// <summary>Sale or Lease.</summary>
        public string? FirstSupplyType { get; set; }

        /// <summary>The contract number of the first supply.</summary>
        public string? FirstSupplyReference { get; set; }

        // ---- The vacancy website ------------------------------------------------

        public int? Bedrooms { get; set; }
        public int? Bathrooms { get; set; }
        public bool IsFurnished { get; set; }

        /// <summary>The rent asked on the website. Empty means the standard annual rent.</summary>
        public decimal? AskingRent { get; set; }

        /// <summary>What the unit offers, one item per line: "Balcony", "Sea view", "Covered parking"...</summary>
        public string? Amenities { get; set; }

        public string? ListingDescription { get; set; }
        public string? ListingDescriptionAr { get; set; }

        /// <summary>
        /// Ticked by the office to advertise the unit. It is shown to the public only while it
        /// is also vacant and active, so a unit that is let leaves the website by itself and
        /// comes back when it is vacant again.
        /// </summary>
        public bool IsListed { get; set; }

        public DateTime? ListedAt { get; set; }

        /// <summary>The rent a visitor is shown.</summary>
        public decimal AdvertisedRent => AskingRent is > 0 ? AskingRent.Value : StandardAnnualRent;

        public ICollection<UnitPhoto> Photos { get; set; } = new List<UnitPhoto>();

        public ICollection<Lease> Leases { get; set; } = new List<Lease>();
    }
}