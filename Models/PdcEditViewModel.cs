using System.ComponentModel.DataAnnotations;

namespace RealEstateManager.Models
{
    /// <summary>
    /// Correction screen for a PDC that has already been recorded. Only fields with no
    /// bearing on what was posted are editable; amount, lease, invoice and settlement
    /// mode are shown read-only because changing them would require the journal to be
    /// reversed and re-posted.
    /// </summary>
    public class PdcEditViewModel
    {
        public int PdcReceiptId { get; set; }

        [Required]
        [Display(Name = "Cheque Number")]
        [StringLength(100)]
        public string ChequeNumber { get; set; } = string.Empty;

        [Display(Name = "Bank")]
        [StringLength(200)]
        public string? BankName { get; set; }

        [Display(Name = "Cheque Date")]
        [DataType(DataType.Date)]
        public DateTime ChequeDate { get; set; }

        [Display(Name = "Received Date")]
        [DataType(DataType.Date)]
        public DateTime ReceivedDate { get; set; }

        [Display(Name = "Notes")]
        [StringLength(1000)]
        public string? Notes { get; set; }

        // Read-only context.
        public decimal Amount { get; set; }
        public string SettlementMode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? LeaseDisplay { get; set; }
        public string? InvoiceDisplay { get; set; }
        public DateTime? ClearedDate { get; set; }

        /// <summary>Date the memorandum journal currently carries.</summary>
        public DateTime? MemoPostedOn { get; set; }
        public string? MemoEntryNumber { get; set; }
        public string? MemoPeriodName { get; set; }
        public bool MemoPeriodClosed { get; set; }

        public bool IsCleared => Status == "Cleared";

        /// <summary>True when the register date and the posted journal date disagree.</summary>
        public bool IsOutOfStep => MemoPostedOn.HasValue && MemoPostedOn.Value.Date != ReceivedDate.Date;
    }
}
