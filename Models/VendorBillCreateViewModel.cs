namespace RealEstateManager.Models
{
    public class VendorBillCreateViewModel
    {
        public int VendorId { get; set; }
        public int? PropertyId { get; set; }

        public string BillNumber { get; set; } = string.Empty;
        public DateTime BillDate { get; set; } = DateTime.Today;
        public DateTime DueDate { get; set; } = DateTime.Today;

        public decimal Subtotal { get; set; }
        public decimal VatAmount { get; set; }
        public string ExpenseCategory { get; set; } = string.Empty;

        /// <summary>
        /// How this purchase relates to the supplies the business makes. Decides how
        /// much of the VAT can be reclaimed; whatever cannot be becomes part of the cost.
        /// </summary>
        public string InputTaxAttribution { get; set; } = Domain.InputTaxAttributions.Taxable;

        /// <summary>Supplier TRN, evidence supporting the input tax claim.</summary>
        public string? SupplierTrn { get; set; }
    }
}