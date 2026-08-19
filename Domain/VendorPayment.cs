namespace RealEstateManager.Domain
{
    public class VendorPayment
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int VendorBillId { get; set; }
        public VendorBill? VendorBill { get; set; }

        public string PaymentNumber { get; set; } = string.Empty;
        public DateTime PaymentDate { get; set; }
        public decimal Amount { get; set; } = 0m;
        public string PaymentMethod { get; set; } = "Bank";

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }
    }
}