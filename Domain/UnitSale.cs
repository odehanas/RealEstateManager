namespace RealEstateManager.Domain
{
    /// <summary>
    /// A sale and purchase agreement for one unit. Revenue is recognised at handover:
    ///
    ///   Before handover   each payment is an advance from the buyer (a liability), with
    ///                     VAT accounted on it at receipt when the sale is standard rated.
    ///   At handover       the price becomes revenue, the unit's cost leaves completed
    ///                     properties held for sale into cost of sales, and whatever is
    ///                     still unpaid becomes a receivable from the buyer.
    ///   After handover    payments settle that receivable.
    ///
    /// The VAT treatment is decided when the contract is signed and kept, with the reason:
    /// the first supply of a new residential unit within three years of completion is zero
    /// rated, later residential supplies are exempt, commercial units are standard rated.
    /// </summary>
    public class UnitSale
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>SPA-yyyy-0001, per company.</summary>
        public string ContractNumber { get; set; } = string.Empty;

        public int UnitId { get; set; }
        public Unit? Unit { get; set; }

        /// <summary>The buyer, from the customer register (Master Data ▸ Tenants).</summary>
        public int BuyerId { get; set; }
        public Tenant? Buyer { get; set; }

        public DateTime ContractDate { get; set; }

        /// <summary>The price before VAT.</summary>
        public decimal SalePrice { get; set; }

        public string VatCategory { get; set; } = VatCategories.Exempt;
        public decimal VatRate { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalPrice { get; set; }

        /// <summary>Why the VAT treatment is what it is, as decided when the contract was signed.</summary>
        public string? VatReason { get; set; }

        public string Status { get; set; } = UnitSaleStatuses.Active;

        public DateTime? HandoverDate { get; set; }

        /// <summary>The unit's cost taken to cost of sales at handover.</summary>
        public decimal CostOfSales { get; set; }

        /// <summary>Price and VAT still unpaid at handover, recognised then.</summary>
        public decimal HandoverNetAmount { get; set; }
        public decimal HandoverVatAmount { get; set; }

        public int? HandoverJournalEntryId { get; set; }
        public JournalEntry? HandoverJournalEntry { get; set; }

        /// <summary>The buyer's new title deed, once registered.</summary>
        public string? BuyerTitleDeedNumber { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByName { get; set; }

        public ICollection<UnitSaleInstalment> Instalments { get; set; } = new List<UnitSaleInstalment>();
        public ICollection<UnitSaleReceipt> Receipts { get; set; } = new List<UnitSaleReceipt>();
    }

    public static class UnitSaleStatuses
    {
        public const string Active = "Active";
        public const string HandedOver = "HandedOver";
        public const string Cancelled = "Cancelled";

        public static string Describe(string status) => status switch
        {
            Active => "Signed - before handover",
            HandedOver => "Handed over",
            _ => status
        };
    }

    /// <summary>One step of the payment plan: an amount due by a date, VAT included.</summary>
    public class UnitSaleInstalment
    {
        public int Id { get; set; }

        public int UnitSaleId { get; set; }
        public UnitSale? UnitSale { get; set; }

        public int Sequence { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>A payment from the buyer.</summary>
    public class UnitSaleReceipt
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }

        public int UnitSaleId { get; set; }
        public UnitSale? UnitSale { get; set; }

        /// <summary>SR-yyyy-0001, per company.</summary>
        public string ReceiptNumber { get; set; } = string.Empty;

        /// <summary>From the company's tax invoice series, for a standard-rated payment before handover.</summary>
        public string? TaxInvoiceNumber { get; set; }

        public DateTime ReceiptDate { get; set; }

        /// <summary>What the buyer paid, VAT included.</summary>
        public decimal Amount { get; set; }

        /// <summary>The price part and the VAT part, when received before handover.</summary>
        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }

        /// <summary>Before handover the payment is an advance; after it, it settles the receivable.</summary>
        public bool IsBeforeHandover { get; set; }

        public string PaymentMethod { get; set; } = "Bank Transfer";
        public int ReceivedIntoAccountId { get; set; }
        public string? Reference { get; set; }

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
