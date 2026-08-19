namespace RealEstateManager.Domain
{
    public class FixedAssetDepreciation
    {
        public int Id { get; set; }

        public int FixedAssetId { get; set; }
        public FixedAsset? FixedAsset { get; set; }

        public int PeriodYear { get; set; }
        public int PeriodMonth { get; set; }
        public DateTime DepreciationDate { get; set; }
        public decimal Amount { get; set; } = 0m;

        // Planned, Posted, Skipped
        public string Status { get; set; } = "Planned";

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public DateTime? PostedAt { get; set; }
    }
}
