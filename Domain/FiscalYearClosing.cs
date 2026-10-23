namespace RealEstateManager.Domain
{
    /// <summary>
    /// The year-end close of one fiscal year: the closing entry that clears revenue and
    /// expense into retained earnings, and the balance sheet carried into the next year
    /// (FiscalYearCarryForwards). One row per company and year; when a period of a closed
    /// year is reopened the close is undone (Status Reopened, closing entry removed) and
    /// the same row is used again when the year is closed again.
    /// </summary>
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

        public decimal TotalRevenue { get; set; } = 0m;
        public decimal TotalExpenses { get; set; } = 0m;
        public decimal ProfitOrLossAmount { get; set; } = 0m;
        public DateTime ClosedAt { get; set; } = DateTime.UtcNow;
        public string? ClosedByName { get; set; }

        /// <summary>Closed, or Reopened while one of its periods is open again.</summary>
        public string Status { get; set; } = FiscalYearClosingStatuses.Closed;
        public DateTime? ReopenedAt { get; set; }
        public string? ReopenedByName { get; set; }

        public string? Notes { get; set; }
    }

    public static class FiscalYearClosingStatuses
    {
        public const string Closed = "Closed";
        public const string Reopened = "Reopened";
    }

    /// <summary>
    /// A balance sheet account's balance at the end of a closed fiscal year: the opening
    /// balance it carries into the next year. The ledger is perpetual, so these figures
    /// are not posted again - they are the record of what was carried forward, kept for
    /// review and audit, and rebuilt each time the year is closed.
    /// </summary>
    public class FiscalYearCarryForward
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int FiscalYearClosingId { get; set; }
        public FiscalYearClosing? FiscalYearClosing { get; set; }

        /// <summary>The year the balance opens (the closed year + 1).</summary>
        public int OpeningFiscalYear { get; set; }

        public int AccountId { get; set; }
        public Account? Account { get; set; }

        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;

        public decimal DebitBalance { get; set; }
        public decimal CreditBalance { get; set; }
    }
}
