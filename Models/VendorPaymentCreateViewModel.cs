namespace RealEstateManager.Models
{
    public class VendorPaymentCreateViewModel
    {
        public int VendorBillId { get; set; }
        public string? PaymentNumber { get; set; }
        public DateTime PaymentDate { get; set; } = DateTime.Today;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = "Bank";
    }
}