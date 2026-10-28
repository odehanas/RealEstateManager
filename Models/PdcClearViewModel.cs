namespace RealEstateManager.Models
{
    public class PdcClearViewModel
    {
        public int PdcReceiptId { get; set; }

        public string ChequeNumber { get; set; } = string.Empty;
        public string? BankName { get; set; }

        public DateTime ChequeDate { get; set; }
        public DateTime ReceivedDate { get; set; }
        public DateTime ClearedDate { get; set; } = DateTime.Today;

        public decimal Amount { get; set; }

        public string SettlementMode { get; set; } = "Advance";

        /// <summary>Deposited for collection first: clearing only moves it from collection into the bank.</summary>
        public DateTime? DepositedDate { get; set; }

        public string? LeaseDisplay { get; set; }
        public string? InvoiceDisplay { get; set; }

        /// <summary>
        /// Ticked by the user to go ahead with clearing a cheque before the date written
        /// on it. Not persisted - it only unlocks the second submit.
        /// </summary>
        public bool ConfirmEarlyClearance { get; set; }

        /// <summary>
        /// Set by the controller when the cleared date precedes the cheque date, so the
        /// view knows to show the confirmation rather than the normal submit button.
        /// </summary>
        public bool RequiresEarlyClearanceConfirmation { get; set; }

        /// <summary>How many days before the cheque date the clearance would fall.</summary>
        public int DaysEarly => (int)(ChequeDate.Date - ClearedDate.Date).TotalDays;
    }
}
