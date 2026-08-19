namespace RealEstateManager.Domain
{
    public class Property
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }

        // Examples: Residential, Commercial, Warehouse, MixedUse
        public string PropertyType { get; set; } = string.Empty;

        // Owned or Managed
        public string OwnershipType { get; set; } = "Owned";

        // Default VAT treatment for units in this property. Units may override it,
        // which is what makes a mixed-use building work: commercial units standard
        // rated, residential units exempt, in the same building.
        public string VatCategory { get; set; } = VatCategories.Standard;

        public string? OwnerName { get; set; }
        public decimal AssetValue { get; set; } = 0m;
        public decimal CommissionPercentage { get; set; } = 0m;

        public bool IsActive { get; set; } = true;

        public ICollection<Unit> Units { get; set; } = new List<Unit>();
    }
}