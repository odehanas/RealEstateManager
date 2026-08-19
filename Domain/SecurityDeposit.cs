namespace RealEstateManager.Domain
{
    public class SecurityDeposit
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int LeaseId { get; set; }
        public Lease? Lease { get; set; }

        public string ReferenceNumber { get; set; } = string.Empty;
        public DateTime ReceiptDate { get; set; }
        public decimal Amount { get; set; } = 0m;
        public string PaymentMethod { get; set; } = "Bank";

        // Held, Applied, Refunded, Forfeited
        public string Status { get; set; } = "Held";

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }
    }
}