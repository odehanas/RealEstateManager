namespace RealEstateManager.Domain
{
    public class CustomerReceipt
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int CustomerInvoiceId { get; set; }
        public CustomerInvoice? CustomerInvoice { get; set; }

        public string ReceiptNumber { get; set; } = string.Empty;
        public DateTime ReceiptDate { get; set; }
        public decimal Amount { get; set; } = 0m;
        public string PaymentMethod { get; set; } = "Bank";

        /// <summary>Who received the money, taken from the property when the receipt is posted. See <see cref="CollectionModes"/>.</summary>
        public string? CollectionMode { get; set; }

        /// <summary>The payment this receipt is part of, when it was received by tenant.</summary>
        public int? CustomerSettlementId { get; set; }
        public CustomerSettlement? CustomerSettlement { get; set; }

        /// <summary>What the invoice still owed just before this receipt. Null on receipts recorded before it was kept.</summary>
        public decimal? OutstandingBefore { get; set; }

        /// <summary>
        /// A payment on account: it paid part of the invoice and left a balance due,
        /// rather than settling the invoice in full.
        /// </summary>
        public bool IsOnAccount => OutstandingBefore.HasValue && Amount < OutstandingBefore.Value;

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }
    }
}