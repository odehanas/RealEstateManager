namespace RealEstateManager.Models
{
    /// <summary>A vendor's statement of account: what we owe them, how it arose and what was paid.</summary>
    public class VendorStatementViewModel
    {
        public int? VendorId { get; set; }
        public DateTime FromDate { get; set; } = DateTime.Today;
        public DateTime ToDate { get; set; } = DateTime.Today;

        public string VendorCode { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string? VendorTrn { get; set; }

        /// <summary>What we owed the vendor at the start of the period (a credit balance).</summary>
        public decimal OpeningBalance { get; set; }

        /// <summary>Bills and other amounts that increased what we owe.</summary>
        public decimal TotalBilled { get; set; }

        /// <summary>Payments and other amounts that reduced it.</summary>
        public decimal TotalPaid { get; set; }

        public decimal ClosingBalance { get; set; }

        public List<VendorStatementRowViewModel> Rows { get; set; } = new();

        /// <summary>Posted bills not yet fully paid, as at the end of the period.</summary>
        public List<VendorOpenBillViewModel> OpenBills { get; set; } = new();
    }

    public class VendorStatementRowViewModel
    {
        public DateTime TransactionDate { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public string ReferenceNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>Reduces what we owe (a payment).</summary>
        public decimal Debit { get; set; }

        /// <summary>Increases what we owe (a bill).</summary>
        public decimal Credit { get; set; }

        public decimal RunningBalance { get; set; }
    }

    public class VendorOpenBillViewModel
    {
        public string BillNumber { get; set; } = string.Empty;
        public string? VendorReference { get; set; }
        public DateTime BillDate { get; set; }
        public DateTime DueDate { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal Balance => TotalAmount - AmountPaid;
        public int DaysOverdue { get; set; }
    }
}
