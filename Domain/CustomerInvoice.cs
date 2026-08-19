namespace RealEstateManager.Domain
{
    public class CustomerInvoice
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int LeaseId { get; set; }
        public Lease? Lease { get; set; }

        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime InvoiceDate { get; set; }
        public DateTime DueDate { get; set; }

        // ---------------------------------------------------------------------
        // VAT. The category is snapshotted when the invoice is generated so that a
        // later change to the lease or property never rewrites history.
        // ---------------------------------------------------------------------

        public string VatCategory { get; set; } = VatCategories.Standard;
        public decimal VatRate { get; set; } = 0m;

        /// <summary>
        /// Sequential number in the company's single tax invoice series, assigned when
        /// the invoice is posted. Article 59 requires a sequential unique number.
        /// </summary>
        public string? TaxInvoiceNumber { get; set; }

        /// <summary>Full, Simplified, or NotApplicable when no output tax was charged.</summary>
        public string TaxInvoiceType { get; set; } = TaxInvoiceTypes.NotApplicable;

        /// <summary>Tax point. Defaults to the invoice date.</summary>
        public DateTime? DateOfSupply { get; set; }

        /// <summary>Recipient TRN captured at issue, so reprints stay faithful.</summary>
        public string? RecipientTrn { get; set; }

        public decimal Subtotal { get; set; } = 0m;
        public decimal VatAmount { get; set; } = 0m;
        public decimal TotalAmount { get; set; } = 0m;
        public decimal AmountPaid { get; set; } = 0m;

        // Draft, Posted, PartiallyPaid, Paid, Cancelled
        public string Status { get; set; } = "Draft";

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public ICollection<CustomerReceipt> Receipts { get; set; } = new List<CustomerReceipt>();
    }
}