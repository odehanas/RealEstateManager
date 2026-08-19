namespace RealEstateManager.Domain
{
    public class Unit
    {
        public int Id { get; set; }

        public int PropertyId { get; set; }
        public Property? Property { get; set; }

        public string UnitNumber { get; set; } = string.Empty;
        public string? Floor { get; set; }
        public string? UsageType { get; set; }

        public decimal AreaSqFt { get; set; } = 0m;
        public decimal StandardAnnualRent { get; set; } = 0m;

        // VAT treatment for this unit. Null means inherit the property's category.
        public string? VatCategory { get; set; }

        // Vacant, Occupied, UnderMaintenance, Reserved
        public string Status { get; set; } = "Vacant";

        public bool IsActive { get; set; } = true;

        public ICollection<Lease> Leases { get; set; } = new List<Lease>();
    }
}