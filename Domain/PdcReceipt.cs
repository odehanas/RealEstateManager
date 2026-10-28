namespace RealEstateManager.Domain
{
    public static class PdcStatuses
    {
        public const string Draft = "Draft";
        public const string Received = "Received";
        public const string Deposited = "Deposited";
        public const string Cleared = "Cleared";
        public const string Bounced = "Bounced";
        public const string Withdrawn = "Withdrawn";
        public const string Replaced = "Replaced";
        public const string Cancelled = "Cancelled";

        public static string Describe(string status) => status switch
        {
            Received => "On hand",
            Deposited => "At bank",
            _ => status
        };
    }

    /// <summary>The kind of a charge invoice: a charge to the tenant, not rent.</summary>
    public static class InvoiceChargeTypes
    {
        public const string PdcWithdrawal = "Cheque withdrawal charge";
        public const string PdcBounce = "Returned cheque charge";
    }

    public class PdcReceipt
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int LeaseId { get; set; }
        public Lease? Lease { get; set; }

        public int? TenantId { get; set; }
        public Tenant? Tenant { get; set; }

        public int? CustomerInvoiceId { get; set; }
        public CustomerInvoice? CustomerInvoice { get; set; }

        public string ChequeNumber { get; set; } = string.Empty;
        public string? BankName { get; set; }

        public DateTime ChequeDate { get; set; }
        public DateTime ReceivedDate { get; set; } = DateTime.Today;
        public DateTime? ClearedDate { get; set; }

        public decimal Amount { get; set; } = 0m;

        // Advance or Receivable
        public string SettlementMode { get; set; } = "Advance";

        /// <summary>See PdcStatuses: Draft, Received, Deposited, Cleared, Bounced, Withdrawn, Replaced, Cancelled.</summary>
        public string Status { get; set; } = "Draft";

        /// <summary>Sent to the bank for collection: the cheque moves to Cheques Under Collection.</summary>
        public DateTime? DepositedDate { get; set; }
        public int? DepositJournalEntryId { get; set; }
        public JournalEntry? DepositJournalEntry { get; set; }

        /// <summary>Returned unpaid by the bank: the amount is charged back to the tenant.</summary>
        public DateTime? BouncedDate { get; set; }
        public string? BounceReason { get; set; }
        public int? BounceJournalEntryId { get; set; }
        public JournalEntry? BounceJournalEntry { get; set; }

        /// <summary>Handed back to the tenant at their request before it was deposited.</summary>
        public DateTime? WithdrawnDate { get; set; }
        public int? WithdrawalJournalEntryId { get; set; }
        public JournalEntry? WithdrawalJournalEntry { get; set; }

        /// <summary>The invoice charging the tenant for a withdrawal or a bounce, when one was raised.</summary>
        public int? ChargeInvoiceId { get; set; }
        public CustomerInvoice? ChargeInvoice { get; set; }

        /// <summary>The tenant was credited when the cheque was deposited, not when it cleared.</summary>
        public bool WentThroughCollection => DepositJournalEntryId.HasValue;

        public string? Notes { get; set; }

        public int? MemorandumJournalEntryId { get; set; }
        public JournalEntry? MemorandumJournalEntry { get; set; }

        public int? ClearanceJournalEntryId { get; set; }
        public JournalEntry? ClearanceJournalEntry { get; set; }
    }
}