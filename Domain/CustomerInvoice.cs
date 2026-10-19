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

        /// <summary>
        /// The rental period this invoice bills. Shown on the e-invoice as the invoicing
        /// period. Null on invoices raised before it was recorded; it is then derived
        /// from the lease's billing frequency.
        /// </summary>
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }

        public decimal Subtotal { get; set; } = 0m;
        public decimal VatAmount { get; set; } = 0m;
        public decimal TotalAmount { get; set; } = 0m;

        /// <summary>
        /// Everything that has SETTLED the invoice: receipts, cleared cheques and posted
        /// credit notes. Outstanding is always TotalAmount - AmountPaid, which is why a
        /// credit note is counted here rather than in a separate balance every report
        /// would have to remember to subtract.
        /// </summary>
        public decimal AmountPaid { get; set; } = 0m;

        /// <summary>The part of AmountPaid that came from posted credit notes, not cash.</summary>
        public decimal CreditedAmount { get; set; } = 0m;

        /// <summary>What the tenant has actually paid in money.</summary>
        public decimal CashReceived => AmountPaid - CreditedAmount;

        // Draft, Posted, PartiallyPaid, Paid, Credited, Cancelled
        public string Status { get; set; } = "Draft";

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        // ---------------------------------------------------------------------
        // E-invoicing. See EInvoiceService.
        // ---------------------------------------------------------------------

        /// <summary>NotRequired, Pending, Generated, Submitted, Accepted, Rejected.</summary>
        public string EInvoiceStatus { get; set; } = EInvoiceStatuses.NotRequired;

        /// <summary>Globally unique identifier (BTAE-07). Allocated once and never changed.</summary>
        public Guid? EInvoiceUuid { get; set; }

        /// <summary>The exact file produced. What was sent is what is kept.</summary>
        public string? EInvoiceXml { get; set; }

        public DateTime? EInvoiceGeneratedAt { get; set; }
        public DateTime? EInvoiceSubmittedAt { get; set; }
        public DateTime? EInvoiceResponseAt { get; set; }

        /// <summary>The provider's own reference for the transmission.</summary>
        public string? EInvoiceProviderReference { get; set; }

        /// <summary>Why the provider rejected it, as they reported it.</summary>
        public string? EInvoiceRejectionReason { get; set; }

        public ICollection<CustomerReceipt> Receipts { get; set; } = new List<CustomerReceipt>();
        public ICollection<CustomerCreditNote> CreditNotes { get; set; } = new List<CustomerCreditNote>();
    }
}