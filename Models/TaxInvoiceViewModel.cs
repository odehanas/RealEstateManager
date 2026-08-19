namespace RealEstateManager.Models
{
    /// <summary>
    /// Everything a UAE tax invoice must show, resolved once so the view has no
    /// database access and no formatting decisions of its own.
    /// </summary>
    public class TaxInvoiceViewModel
    {
        // Supplier
        public string SupplierName { get; set; } = string.Empty;
        public string? SupplierLegalName { get; set; }
        public string? SupplierTrn { get; set; }
        public string? SupplierAddressLine1 { get; set; }
        public string? SupplierAddressLine2 { get; set; }
        public string? SupplierCity { get; set; }
        public string? SupplierCountry { get; set; }
        public string? SupplierPhone { get; set; }
        public string? SupplierEmail { get; set; }

        // Recipient
        public string TenantName { get; set; } = string.Empty;
        public string? TenantAddress { get; set; }
        public string? TenantPhone { get; set; }
        public string? TenantEmail { get; set; }
        public string? RecipientTrn { get; set; }

        // Document
        public string DocumentNumber { get; set; } = string.Empty;
        public string? SourceInvoiceNumber { get; set; }
        public string TaxInvoiceType { get; set; } = Domain.TaxInvoiceTypes.NotApplicable;
        public DateTime InvoiceDate { get; set; }
        public DateTime DateOfSupply { get; set; }
        public DateTime DueDate { get; set; }

        // Subject of the supply
        public string PropertyName { get; set; } = string.Empty;
        public string UnitNumber { get; set; } = string.Empty;
        public string ContractNumber { get; set; } = string.Empty;
        public string LeasePeriod { get; set; } = string.Empty;
        public string LineDescription { get; set; } = string.Empty;
        public string LineDetail { get; set; } = string.Empty;

        // Amounts
        public string VatCategory { get; set; } = Domain.VatCategories.Standard;
        public decimal VatRate { get; set; }
        public decimal Subtotal { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal AmountPaid { get; set; }

        public string Currency { get; set; } = "AED";
        public string CurrencyName { get; set; } = "UAE Dirhams (AED)";

        /// <summary>
        /// Reasons this document would not satisfy Article 59 as it stands. Shown on
        /// screen but never printed, so nobody hands a tenant an invalid invoice
        /// without being told first.
        /// </summary>
        public List<string> ComplianceWarnings { get; set; } = new();
    }
}
