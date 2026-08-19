namespace RealEstateManager.Domain
{
    public class PdcReceipt
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int LeaseId { get; set; }
        public Lease? Lease { get; set; }

        public int? TenantId { get; set; }
        public Tenant? Tenant { get; set; }

        public int? CustomerInvoiceId { get; set; }
        public CustomerInvoice? CustomerInvoice { get; set; }

        public string ChequeNumber { get; set; } = string.Empty;
        public string? BankName { get; set; }

        public DateTime ChequeDate { get; set; }
        public DateTime ReceivedDate { get; set; } = DateTime.Today;
        public DateTime? ClearedDate { get; set; }

        public decimal Amount { get; set; } = 0m;

        // Advance or Receivable
        public string SettlementMode { get; set; } = "Advance";

        // Draft, Received, Cleared, Bounced, Cancelled, Replaced
        public string Status { get; set; } = "Draft";

        public string? Notes { get; set; }

        public int? MemorandumJournalEntryId { get; set; }
        public JournalEntry? MemorandumJournalEntry { get; set; }

        public int? ClearanceJournalEntryId { get; set; }
        public JournalEntry? ClearanceJournalEntry { get; set; }
    }
}