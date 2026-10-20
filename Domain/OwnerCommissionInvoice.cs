namespace RealEstateManager.Domain
{
    /// <summary>
    /// Our tax invoice to the landlord of a managed property for the management
    /// commission earned in a month.
    ///
    /// The commission is a separate supply from the rent: a real-estate service we
    /// provide to the owner, standard rated at 5% even when the rent itself is exempt
    /// residential rent. One invoice is issued per owner at each month end, covering
    /// the commission recognised up to that date that has not been invoiced before.
    ///
    /// The commission itself is already in revenue (revenue recognition moves it from
    /// Due to Landlords to Management Fee Revenue each month). This invoice adds the VAT:
    ///
    ///     Dr  Due to Landlords        VAT is taken from the owner's money
    ///         Cr  VAT Payable         output tax on the commission
    /// </summary>
    public class OwnerCommissionInvoice
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>CI-yyyy-0001, per company.</summary>
        public string InvoiceNumber { get; set; } = string.Empty;

        /// <summary>From the company's single tax invoice series, shared with tenant invoices.</summary>
        public string? TaxInvoiceNumber { get; set; }

        public string OwnerName { get; set; } = string.Empty;

        /// <summary>The landlord's TRN, when registered: a full tax invoice must show it.</summary>
        public string? RecipientTrn { get; set; }

        /// <summary>The month end the invoice is issued at; also the date of supply.</summary>
        public DateTime InvoiceDate { get; set; }

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        public decimal Commission { get; set; }
        public decimal VatRate { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByName { get; set; }

        public ICollection<OwnerCommissionInvoiceLine> Lines { get; set; } = new List<OwnerCommissionInvoiceLine>();

        /// <summary>A zero-VAT invoice (company not VAT registered) has no journal and always counts.</summary>
        public bool IsPosted => JournalEntryId == null || JournalEntry?.IsPosted == true;
    }

    /// <summary>One month's commission on one contract.</summary>
    public class OwnerCommissionInvoiceLine
    {
        public int Id { get; set; }

        public int OwnerCommissionInvoiceId { get; set; }
        public OwnerCommissionInvoice? OwnerCommissionInvoice { get; set; }

        /// <summary>The commission recognition this line invoices. Each can be invoiced only once.</summary>
        public int RevenueRecognitionScheduleId { get; set; }

        public int PropertyId { get; set; }
        public int LeaseId { get; set; }

        public DateTime RecognitionDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Commission { get; set; }
    }
}
