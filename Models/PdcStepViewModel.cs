namespace RealEstateManager.Models
{
    /// <summary>One step in a cheque's life after it is received: deposit, bounce or withdraw.</summary>
    public class PdcStepViewModel
    {
        public const string Deposit = "deposit";
        public const string Bounce = "bounce";
        public const string Withdraw = "withdraw";

        public int PdcReceiptId { get; set; }
        public string Kind { get; set; } = Deposit;

        public DateTime StepDate { get; set; } = DateTime.Today;

        /// <summary>Charge to the tenant before VAT (bounce and withdrawal only); 0 for none.</summary>
        public decimal Charge { get; set; }

        /// <summary>The bank's reason for a bounce, or a note on a withdrawal.</summary>
        public string? Reason { get; set; }

        // Shown, not posted back.
        public string ChequeNumber { get; set; } = string.Empty;
        public string? BankName { get; set; }
        public DateTime ChequeDate { get; set; }
        public DateTime ReceivedDate { get; set; }
        public DateTime? DepositedDate { get; set; }
        public decimal Amount { get; set; }
        public string SettlementMode { get; set; } = "Advance";
        public string Status { get; set; } = string.Empty;
        public string? LeaseDisplay { get; set; }
        public string? InvoiceDisplay { get; set; }
        public decimal VatRate { get; set; }

        public string Title => Kind switch
        {
            Bounce => "Record Bounced Cheque",
            Withdraw => "Withdraw Cheque",
            _ => "Deposit Cheque to Bank"
        };

        public bool TakesCharge => Kind is Bounce or Withdraw;
    }
}
