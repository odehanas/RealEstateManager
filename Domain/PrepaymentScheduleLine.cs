namespace RealEstateManager.Domain
{
    public class PrepaymentScheduleLine
    {
        public int Id { get; set; }

        public int PrepaymentScheduleId { get; set; }
        public PrepaymentSchedule? PrepaymentSchedule { get; set; }

        public DateTime RecognitionDate { get; set; }
        public decimal Amount { get; set; } = 0m;

        // Planned, Posted, Cancelled
        public string Status { get; set; } = "Planned";

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public DateTime? PostedAt { get; set; }
    }
}
