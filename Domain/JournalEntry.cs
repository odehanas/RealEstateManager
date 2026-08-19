namespace RealEstateManager.Domain
{
    public class JournalEntry
    {
        public int Id { get; set; }

        public int? CompanyId { get; set; }
        public Company? Company { get; set; }

        public int? AccountingPeriodId { get; set; }
        public AccountingPeriod? AccountingPeriod { get; set; }

        public string EntryNumber { get; set; } = string.Empty;
        public DateTime EntryDate { get; set; }

        public string JournalType { get; set; } = "Auto"; // Manual, Auto, Closing, PDC, Depreciation
        public string Status { get; set; } = "Posted";    // Draft, Posted, Reversed

        public string Description { get; set; } = string.Empty;
        public string? DocumentNumber { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Remarks { get; set; }

        public string? SourceType { get; set; }
        public int? SourceId { get; set; }

        public bool IsPosted { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PostedAt { get; set; }
        public string? PostedBy { get; set; }

        public int? ReversalJournalEntryId { get; set; }
        public JournalEntry? ReversalJournalEntry { get; set; }

        public ICollection<JournalEntry> ReversedEntries { get; set; } = new List<JournalEntry>();
        public ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();
    }
}