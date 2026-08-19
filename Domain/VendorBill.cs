namespace RealEstateManager.Domain
{
    public class VendorBill
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int VendorId { get; set; }
        public Vendor? Vendor { get; set; }

        public int? PropertyId { get; set; }
        public Property? Property { get; set; }

        public string BillNumber { get; set; } = string.Empty;
        public DateTime BillDate { get; set; }
        public DateTime DueDate { get; set; }

        public decimal Subtotal { get; set; } = 0m;
        public decimal VatAmount { get; set; } = 0m;
        public decimal TotalAmount { get; set; } = 0m;
        public decimal AmountPaid { get; set; } = 0m;

        public string ExpenseCategory { get; set; } = string.Empty;

        // ---------------------------------------------------------------------
        // Input tax. Attribution decides how much of VatAmount may be recovered;
        // whatever is not recoverable is capitalised into the expense, because
        // irrecoverable VAT is a cost, not an asset.
        // ---------------------------------------------------------------------

        public string InputTaxAttribution { get; set; } = InputTaxAttributions.Taxable;

        /// <summary>Portion of VatAmount debited to VAT Recoverable.</summary>
        public decimal RecoverableVatAmount { get; set; } = 0m;

        /// <summary>Portion of VatAmount added to the expense account.</summary>
        public decimal NonRecoverableVatAmount { get; set; } = 0m;

        /// <summary>Recovery rate applied to a Residual bill, kept for the annual wash-up.</summary>
        public decimal AppliedRecoveryRate { get; set; } = 100m;

        /// <summary>Supplier TRN captured at entry, as evidence for the input tax claim.</summary>
        public string? SupplierTrn { get; set; }

        // Draft, Posted, PartiallyPaid, Paid, Cancelled
        public string Status { get; set; } = "Draft";

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public ICollection<VendorPayment> Payments { get; set; } = new List<VendorPayment>();
    }
}