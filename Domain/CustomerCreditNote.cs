namespace RealEstateManager.Domain
{
    /// <summary>
    /// A tax credit note against one customer invoice.
    ///
    /// Under e-invoicing it is the ONLY lawful way to reduce or cancel an invoice once
    /// it has been reported: negative invoices are not allowed and an invoice the FTA
    /// already holds cannot be edited or deleted. The credit note reverses the revenue
    /// (or the liability to the landlord) and the output tax, and reduces what the
    /// tenant owes on the invoice it refers to.
    /// </summary>
    public class CustomerCreditNote
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int CustomerInvoiceId { get; set; }
        public CustomerInvoice? CustomerInvoice { get; set; }

        /// <summary>One sequential series per company: CN-2026-000001.</summary>
        public string CreditNoteNumber { get; set; } = string.Empty;

        public DateTime CreditNoteDate { get; set; }

        /// <summary>Statutory reason code (BTAE-03) - see <see cref="CreditNoteReasons"/>.</summary>
        public string ReasonCode { get; set; } = CreditNoteReasons.ConsiderationAltered;

        /// <summary>The explanation printed on the document.</summary>
        public string Reason { get; set; } = string.Empty;

        // Tax treatment is the original invoice's, snapshotted - a credit note reverses
        // the tax that was charged, at the rate it was charged.
        public string VatCategory { get; set; } = VatCategories.Standard;
        public decimal VatRate { get; set; }

        public decimal Subtotal { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// The account the net amount is debited to. Defaults to the account the invoice
        /// credited (unearned revenue, or the landlord liability for a managed property);
        /// a rent discount on revenue already recognised may go to a revenue account.
        /// </summary>
        public int? DebitAccountId { get; set; }
        public Account? DebitAccount { get; set; }

        // Draft, Posted
        public string Status { get; set; } = "Draft";

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int? CreatedByAppUserId { get; set; }

        // ---------------------------------------------------------------------
        // E-invoicing - the same lifecycle as an invoice.
        // ---------------------------------------------------------------------

        public string EInvoiceStatus { get; set; } = EInvoiceStatuses.NotRequired;
        public Guid? EInvoiceUuid { get; set; }
        public string? EInvoiceXml { get; set; }
        public DateTime? EInvoiceGeneratedAt { get; set; }
        public DateTime? EInvoiceSubmittedAt { get; set; }
        public DateTime? EInvoiceResponseAt { get; set; }
        public string? EInvoiceProviderReference { get; set; }
        public string? EInvoiceRejectionReason { get; set; }

        public bool IsPosted => JournalEntry?.IsPosted == true;
    }
}
