namespace RealEstateManager.Models
{
    public class AccountTreeViewModel
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public string ReportingGroup { get; set; } = string.Empty;
        public bool IsGroup { get; set; }
        public bool IsPostable { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }

        public decimal OpeningBalance { get; set; }
        public string? OpeningBalanceSource { get; set; }
        public bool IsControlAccount { get; set; }

        public List<AccountTreeViewModel> Children { get; set; } = new();
    }
}