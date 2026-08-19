namespace RealEstateManager.Models
{
    public class PdcReceiptCreateViewModel
    {
        public int LeaseId { get; set; }
        public int? TenantId { get; set; }
        public int? CustomerInvoiceId { get; set; }

        public string ChequeNumber { get; set; } = string.Empty;
        public string? BankName { get; set; }

        public DateTime ChequeDate { get; set; } = DateTime.Today;
        public DateTime ReceivedDate { get; set; } = DateTime.Today;

        public decimal Amount { get; set; }

        // Advance or Receivable
        public string SettlementMode { get; set; } = "Advance";

        public string? Notes { get; set; }
    }
}
