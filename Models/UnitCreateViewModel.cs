namespace RealEstateManager.Models
{
    public class UnitCreateViewModel
    {
        public int PropertyId { get; set; }
        public string UnitNumber { get; set; } = string.Empty;
        public string? Floor { get; set; }
        public string? UsageType { get; set; }
        public decimal AreaSqFt { get; set; }
        public decimal StandardAnnualRent { get; set; }
        public string Status { get; set; } = "Vacant";

        /// <summary>
        /// VAT treatment override for this unit. Empty means inherit the property's
        /// treatment, which is what you want for everything except a mixed-use building.
        /// </summary>
        public string? VatCategory { get; set; }
    }
}