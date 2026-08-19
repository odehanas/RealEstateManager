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

        /// <summary>
        /// Our own internal reference, issued by the system in a single sequence per
        /// company - BILL-2026-0001. Equivalent to Odoo's BILL/2026/0001.
        /// </summary>
        public string BillNumber { get; set; } = string.Empty;

        /// <summary>
        /// The supplier's own document number, as printed on the paper they sent.
        /// Odoo calls this the Bill Reference. Kept distinct from BillNumber because
        /// two suppliers routinely use the same numbering, and because the audit trail
        /// needs to tie our record back to their document.
        /// </summary>
        public string? VendorReference { get; set; }

        /// <summary>Date on the supplier's document.</summary>
        public DateTime BillDate { get; set; }

        /// <summary>
        /// The date the journal is booked to, which decides the accounting period.
        /// Normally the same as BillDate, but separated deliberately: a supplier
        /// document that arrives after a period has closed must still be recorded, and
        /// it belongs in the open period rather than being forced into a closed one.
        /// </summary>
        public DateTime AccountingDate { get; set; }

        public DateTime DueDate { get; set; }

        public decimal Subtotal { get; set; } = 0m;
        public decimal VatAmount { get; set; } = 0m;
        public decimal TotalAmount { get; set; } = 0m;
        public decimal AmountPaid { get; set; } = 0m;

        /// <summary>
        /// Free-text grouping kept from the original single-amount bill. It no longer
        /// decides which account is charged - each line names its own account - but it
        /// still labels the document on lists and reports.
        /// </summary>
        public string ExpenseCategory { get; set; } = string.Empty;

        /// <summary>Narration printed on the document and carried onto the journal.</summary>
        public string? Notes { get; set; }

        /// <summary>
        /// Whether the supplier was VAT registered when the bill was entered, snapshotted
        /// so a later change to the vendor record never rewrites a document already
        /// booked. False means no tax may be charged and the document is a commercial
        /// invoice: the gross amount is a direct cost.
        /// </summary>
        public bool VendorIsVatRegistered { get; set; }

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

        public ICollection<VendorBillLine> Lines { get; set; } = new List<VendorBillLine>();
        public ICollection<VendorPayment> Payments { get; set; } = new List<VendorPayment>();

        /// <summary>Outstanding amount owed to the supplier.</summary>
        public decimal BalanceDue => TotalAmount - AmountPaid;

        /// <summary>
        /// A bill may only be amended while it is a draft. Once posted it is part of
        /// the accounts; correcting it means resetting to draft first, which the
        /// workflow refuses if a payment already depends on it.
        /// </summary>
        public bool IsEditable => !JournalEntryId.HasValue || Status == "Draft";

        /// <summary>
        /// What the supplier's document actually is. An unregistered supplier cannot
        /// issue a tax invoice, so there is nothing to reclaim and the cost goes direct.
        /// </summary>
        public string DocumentType => VendorIsVatRegistered && VatAmount > 0m
            ? "Tax Invoice"
            : "Commercial Invoice";
    }
}