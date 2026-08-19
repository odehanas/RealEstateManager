namespace RealEstateManager.Models
{
    public class PropertyUnitsHierarchyViewModel
    {
        public int PropertyId { get; set; }
        public string PropertyCode { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string PropertyType { get; set; } = string.Empty;
        public string OwnershipType { get; set; } = string.Empty;
        public string? OwnerName { get; set; }

        public List<UnitHierarchyItemViewModel> Units { get; set; } = new();
    }

    public class UnitHierarchyItemViewModel
    {
        public int UnitId { get; set; }
        public string UnitNumber { get; set; } = string.Empty;
        public string? Floor { get; set; }
        public string? UsageType { get; set; }
        public decimal AreaSqFt { get; set; }
        public decimal StandardAnnualRent { get; set; }
        public string Status { get; set; } = string.Empty;

        public bool HasActiveLease { get; set; }
        public int? ActiveLeaseId { get; set; }
        public string? ActiveContractNumber { get; set; }
        public string? ActiveTenantName { get; set; }
        public DateTime? LeaseStartDate { get; set; }
        public DateTime? LeaseEndDate { get; set; }
    }
}