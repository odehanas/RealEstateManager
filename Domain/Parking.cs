namespace RealEstateManager.Domain
{
    /// <summary>A numbered car parking bay of a property.</summary>
    public class ParkingBay
    {
        public const int MaxNumberLength = 30;

        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int PropertyId { get; set; }
        public Property? Property { get; set; }

        /// <summary>As painted on the floor or the sign: "B1-014", "P-7".</summary>
        public string BayNumber { get; set; } = string.Empty;

        /// <summary>See <see cref="ParkingBayTypes"/>.</summary>
        public string? BayType { get; set; }
        public string? Notes { get; set; }

        /// <summary>Off: no longer let (closed, converted, reserved for the building).</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<ParkingAssignment> Assignments { get; set; } = new();
    }

    public static class ParkingBayTypes
    {
        public static readonly string[] All = { "Covered", "Uncovered", "Basement", "Visitor" };
    }

    /// <summary>
    /// A bay given to a contract, and so to its unit and tenant, from a date until the
    /// contract ends or the bay is released.
    ///
    /// The charge is rent for part of the property, so it is billed and accounted for as
    /// rent is: invoices on the contract (in one payment, or every so many months, as the
    /// company chooses - the same choice as the contract's own payment terms), the income
    /// spread over the months, and for a managed property the money the owner's less the
    /// commission. A bay can also be given free of charge.
    /// </summary>
    public class ParkingAssignment
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int ParkingBayId { get; set; }
        public ParkingBay? ParkingBay { get; set; }

        public int LeaseId { get; set; }
        public Lease? Lease { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        /// <summary>The charge for a year, before VAT. Zero: the bay comes with the unit.</summary>
        public decimal AnnualCharge { get; set; }

        /// <summary>Billed every so many months (1, 2, 3, 4, 6, 12), or 0 for one payment covering the whole period.</summary>
        public int BillingFrequencyMonths { get; set; }

        public string VatCategory { get; set; } = VatCategories.Standard;

        public string? VehiclePlate { get; set; }
        public string? Notes { get; set; }

        /// <summary>See <see cref="ParkingAssignmentStatuses"/>.</summary>
        public string Status { get; set; } = ParkingAssignmentStatuses.Active;
        public DateTime? ReleasedOn { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByName { get; set; }

        public static readonly int[] Frequencies = { 0, 1, 2, 3, 4, 6, 12 };

        public static string DescribeFrequency(int months) => months switch
        {
            0 => "One payment",
            1 => "Monthly",
            2 => "Every 2 Months",
            3 => "Quarterly",
            4 => "Every 4 Months",
            6 => "Semi-Annual",
            12 => "Annual",
            _ => $"Every {months} months"
        };
    }

    public static class ParkingAssignmentStatuses
    {
        public const string Active = "Active";
        public const string Released = "Released";
    }
}
