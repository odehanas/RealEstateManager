namespace RealEstateManager.Domain
{
    public class CustomerReceipt
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int CustomerInvoiceId { get; set; }
        public CustomerInvoice? CustomerInvoice { get; set; }

        public string ReceiptNumber { get; set; } = string.Empty;
        public DateTime ReceiptDate { get; set; }
        public decimal Amount { get; set; } = 0m;
        public string PaymentMethod { get; set; } = "Bank";

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }
    }
}