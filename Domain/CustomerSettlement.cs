namespace RealEstateManager.Domain
{
    /// <summary>
    /// One payment received from a tenant, and the record of which invoices it settled.
    /// The money is applied invoice by invoice: each <see cref="CustomerReceipt"/> under
    /// the settlement is the part applied to one invoice, with its own journal. This is
    /// the document that matches a payment to its invoices - what SAP calls a clearing
    /// document.
    /// </summary>
    public class CustomerSettlement
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int TenantId { get; set; }
        public Tenant? Tenant { get; set; }

        /// <summary>The receipt number given to the tenant for the whole payment.</summary>
        public string SettlementNumber { get; set; } = string.Empty;
        public DateTime SettlementDate { get; set; }

        /// <summary>The whole amount received; the receipts under it add up to this.</summary>
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = "Bank";

        /// <summary>The reference the bank or the tenant gave for the payment.</summary>
        public string? Reference { get; set; }
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }
        public string? CreatedByName { get; set; }

        public ICollection<CustomerReceipt> Receipts { get; set; } = new List<CustomerReceipt>();
    }
}
