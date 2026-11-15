using RealEstateManager.Domain;

namespace RealEstateManager.Models
{
    /// <summary>The Receive Payment form: a tenant, the money, and the invoices it is for.</summary>
    public class CustomerSettlementCreateViewModel
    {
        public int TenantId { get; set; }

        /// <summary>
        /// The invoices the tenant says they are paying. They are paid first; anything
        /// left over, or the whole amount when none is chosen, goes to the invoices
        /// nearest their due date.
        /// </summary>
        public List<int> InvoiceIds { get; set; } = new();

        public string? ReceiptNumber { get; set; }
        public DateTime ReceiptDate { get; set; } = DateTime.Today;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = "Bank";
        public string? Reference { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>A tenant in the picker of the Receive Payment form.</summary>
    public class SettlementTenantOption
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public decimal Outstanding { get; set; }
    }

    /// <summary>What the Receive Payment page shows besides the form itself.</summary>
    public class CustomerSettlementPageViewModel
    {
        public CustomerSettlementCreateViewModel Form { get; set; } = new();
        public List<SettlementTenantOption> Tenants { get; set; } = new();
        public Tenant? Tenant { get; set; }
        public List<CustomerInvoice> OpenInvoices { get; set; } = new();
        public string Currency { get; set; } = "AED";
    }
}
