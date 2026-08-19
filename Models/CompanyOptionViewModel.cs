namespace RealEstateManager.Models
{
    public class CompanyOptionViewModel
    {
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public bool IsCurrent { get; set; }
    }
}
