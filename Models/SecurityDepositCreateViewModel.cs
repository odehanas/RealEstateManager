namespace RealEstateManager.Models
{
    public class SecurityDepositCreateViewModel
    {
        public int LeaseId { get; set; }
        public string? ReferenceNumber { get; set; }
        public DateTime ReceiptDate { get; set; } = DateTime.Today;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = "Bank";
        public string Status { get; set; } = "Held";
    }
}