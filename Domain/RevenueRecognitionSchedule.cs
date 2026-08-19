namespace RealEstateManager.Domain
{
    public class RevenueRecognitionSchedule
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int LeaseId { get; set; }
        public Lease? Lease { get; set; }

        public DateTime RecognitionDate { get; set; }
        public decimal Amount { get; set; } = 0m;

        // Planned, Posted, Cancelled
        public string Status { get; set; } = "Planned";

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }
    }
}