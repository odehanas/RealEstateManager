namespace RealEstateManager.Models
{
    public class ClosingDashboardViewModel
    {
        public int FiscalYear { get; set; }
        public List<AccountingPeriodItemViewModel> Periods { get; set; } = new();
        public List<FiscalYearClosingItemViewModel> FiscalYearClosings { get; set; } = new();
    }

    public class AccountingPeriodItemViewModel
    {
        public int PeriodNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsClosed { get; set; }
        public DateTime? ClosedAt { get; set; }
    }

    public class FiscalYearClosingItemViewModel
    {
        public int FiscalYear { get; set; }
        public decimal ProfitOrLossAmount { get; set; }
        public DateTime ClosedAt { get; set; }
        public string? JournalEntryNumber { get; set; }
    }
}