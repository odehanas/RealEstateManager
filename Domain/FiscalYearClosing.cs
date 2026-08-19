namespace RealEstateManager.Domain
{
    public class FiscalYearClosing
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int FiscalYear { get; set; }

        public int RetainedEarningsAccountId { get; set; }
        public Account? RetainedEarningsAccount { get; set; }

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public decimal ProfitOrLossAmount { get; set; } = 0m;
        public DateTime ClosedAt { get; set; } = DateTime.UtcNow;

        public string? Notes { get; set; }
    }
}