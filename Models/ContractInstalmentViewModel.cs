namespace RealEstateManager.Models
{
    /// <summary>One row of the payment schedule printed on the draft contract.</summary>
    public class ContractInstalmentViewModel
    {
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public decimal Subtotal { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
