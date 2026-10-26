namespace RealEstateManager.Domain
{
    /// <summary>
    /// Money paid to a vendor. Either:
    ///
    ///  * a payment of open bills (SAP F-53): applied through its clearing document
    ///    (<see cref="VendorClearing"/>) to one or more of the vendor's posted bills, each in
    ///    full or in part:      Dr Accounts Payable (per bill)   Cr Bank / Cash
    ///
    ///  * an advance / down payment (SAP F-48, special G/L indicator A), made before any bill:
    ///                         Dr 11550 Advances to Suppliers    Cr Bank / Cash
    ///    It stays open on its own account, apart from Accounts Payable, until it is applied
    ///    to the vendor's bills (SAP F-54).
    ///
    /// Payments recorded before clearing documents existed carry a single
    /// <see cref="VendorBillId"/> instead.
    /// </summary>
    public class VendorPayment
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>The vendor paid. Older payments leave it empty and take it from their bill.</summary>
        public int? VendorId { get; set; }
        public Vendor? Vendor { get; set; }

        /// <summary>The single bill of a payment recorded before clearing documents.</summary>
        public int? VendorBillId { get; set; }
        public VendorBill? VendorBill { get; set; }

        public string PaymentNumber { get; set; } = string.Empty;
        public DateTime PaymentDate { get; set; }
        public decimal Amount { get; set; } = 0m;
        public string PaymentMethod { get; set; } = "Bank";

        /// <summary>A down payment, kept on Advances to Suppliers until applied to bills.</summary>
        public bool IsAdvance { get; set; }

        /// <summary>For an advance: how much has been applied to bills so far.</summary>
        public decimal AdvanceApplied { get; set; }
        public decimal AdvanceOpen => IsAdvance ? Amount - AdvanceApplied : 0m;

        /// <summary>The bank or cash account paid from; empty means the company's default cash account.</summary>
        public int? PaidFromAccountId { get; set; }
        public Account? PaidFromAccount { get; set; }

        /// <summary>Cheque number or transfer reference.</summary>
        public string? Reference { get; set; }
        public string? Notes { get; set; }

        /// <summary>The clearing document number this payment created (CLR-yyyy-nnnnnn).</summary>
        public string? ClearingNumber { get; set; }

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }
    }

    /// <summary>
    /// A clearing document: one application of money to a vendor's open bills - a payment
    /// (SAP F-53) or an advance applied to bills (SAP F-54). Its number is the reference
    /// that ties the payment to the bills it settled.
    /// </summary>
    public class VendorClearing
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string ClearingNumber { get; set; } = string.Empty;
        public DateTime ClearingDate { get; set; }

        public int VendorId { get; set; }
        public Vendor? Vendor { get; set; }

        /// <summary>Payment or Advance.</summary>
        public string ClearingType { get; set; } = VendorClearingTypes.Payment;

        /// <summary>The payment whose money is applied (for a payment clearing).</summary>
        public int? VendorPaymentId { get; set; }
        public VendorPayment? VendorPayment { get; set; }

        /// <summary>The advance whose money is applied (for an advance clearing).</summary>
        public int? AdvancePaymentId { get; set; }
        public VendorPayment? AdvancePayment { get; set; }

        /// <summary>The entry that moved the money: the payment's own, or Dr AP / Cr Advances for an advance.</summary>
        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public decimal Amount { get; set; }

        /// <summary>Pending until its entry is posted (a payment may wait for approval), then Posted; Reversed if reset.</summary>
        public string Status { get; set; } = VendorClearingStatuses.Pending;

        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByName { get; set; }

        public ICollection<VendorClearingLine> Lines { get; set; } = new List<VendorClearingLine>();
    }

    /// <summary>How much of one bill a clearing document settles.</summary>
    public class VendorClearingLine
    {
        public int Id { get; set; }

        public int VendorClearingId { get; set; }
        public VendorClearing? VendorClearing { get; set; }

        public int VendorBillId { get; set; }
        public VendorBill? VendorBill { get; set; }

        public decimal Amount { get; set; }
    }

    public static class VendorClearingTypes
    {
        public const string Payment = "Payment";
        public const string Advance = "Advance";
    }

    public static class VendorClearingStatuses
    {
        public const string Pending = "Pending";
        public const string Posted = "Posted";
        public const string Reversed = "Reversed";
    }
}
