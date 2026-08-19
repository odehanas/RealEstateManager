namespace RealEstateManager.Models
{
    public class CustomerReceiptCreateViewModel
    {
        public int CustomerInvoiceId { get; set; }
        public string? ReceiptNumber { get; set; }
        public DateTime ReceiptDate { get; set; } = DateTime.Today;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = "Bank";
    }
}