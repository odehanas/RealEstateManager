namespace RealEstateManager.Models
{
    public class TenantStatementViewModel
    {
        public int? TenantId { get; set; }
        public int? LeaseId { get; set; }

        public DateTime FromDate { get; set; } = DateTime.Today;
        public DateTime ToDate { get; set; } = DateTime.Today;

        public string TenantCode { get; set; } = string.Empty;
        public string TenantName { get; set; } = string.Empty;
        public string LeaseDisplay { get; set; } = "All Leases";

        public decimal OpeningBalance { get; set; }
        public decimal TotalCharges { get; set; }
        public decimal TotalCredits { get; set; }
        public decimal ClosingBalance { get; set; }
        public decimal NotedPdcTotal { get; set; }

        public List<TenantStatementRowViewModel> Rows { get; set; } = new();
        public List<TenantStatementPdcItemViewModel> NotedPdcItems { get; set; } = new();
    }

    public class TenantStatementRowViewModel
    {
        /// <summary>The original document, for the cross reference.</summary>
        public string? SourceType { get; set; }
        public int? SourceId { get; set; }

        public DateTime TransactionDate { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public string ReferenceNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal RunningBalance { get; set; }
    }

    public class TenantStatementPdcItemViewModel
    {
        public DateTime ReceivedDate { get; set; }
        public DateTime ChequeDate { get; set; }
        public string ChequeNumber { get; set; } = string.Empty;
        public string? BankName { get; set; }
        public decimal Amount { get; set; }
        public string SettlementMode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string LeaseDisplay { get; set; } = string.Empty;
        public string InvoiceDisplay { get; set; } = "-";
    }
}
