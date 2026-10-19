using System.ComponentModel.DataAnnotations;

namespace RealEstateManager.Models
{
    public class PortalLoginViewModel
    {
        [Required]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;
    }

    public class PortalChangePasswordViewModel
    {
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare(nameof(NewPassword), ErrorMessage = "The two passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        /// <summary>True when the tenant is being forced to change a temporary password.</summary>
        public bool IsForced { get; set; }
    }

    public class PortalDashboardViewModel
    {
        public DateTime AsOfDate { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public string TenantCode { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string? CompanyPhone { get; set; }
        public string? CompanyEmail { get; set; }

        public decimal OpeningBalance { get; set; }
        public decimal TotalInvoiced { get; set; }
        public decimal TotalPaid { get; set; }

        /// <summary>Reductions by credit note - shown apart from money paid.</summary>
        public decimal TotalCredited { get; set; }

        public decimal OutstandingBalance { get; set; }

        public decimal OverdueAmount { get; set; }
        public int OverdueCount { get; set; }

        public decimal SecurityDepositHeld { get; set; }

        public List<PortalContractViewModel> ActiveContracts { get; set; } = new();
        public PortalInvoiceViewModel? NextDueInvoice { get; set; }
        public List<PortalPdcViewModel> PdcsOnHand { get; set; } = new();

        public bool IsInCredit => OutstandingBalance < 0m;
        public bool IsSettled => OutstandingBalance == 0m;
        public decimal PdcsOnHandTotal => PdcsOnHand.Sum(x => x.Amount);
    }

    public class PortalContractViewModel
    {
        public int LeaseId { get; set; }
        public string ContractNumber { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string? PropertyAddress { get; set; }
        public string UnitCode { get; set; } = string.Empty;
        public string? Floor { get; set; }
        public decimal AreaSqFt { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal AnnualRent { get; set; }
        public int BillingFrequencyMonths { get; set; }
        public string VatCategory { get; set; } = string.Empty;
        public decimal SecurityDepositAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime AsOfDate { get; set; }

        public bool IsCurrent => Status == "Active" && StartDate <= AsOfDate && EndDate >= AsOfDate;
        public int DaysToExpiry => (int)(EndDate - AsOfDate).TotalDays;
        public bool IsExpiringSoon => IsCurrent && DaysToExpiry <= 90;
    }

    public class PortalInvoiceViewModel
    {
        public int InvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public string? TaxInvoiceNumber { get; set; }
        public string ContractNumber { get; set; } = string.Empty;
        public string UnitCode { get; set; } = string.Empty;
        public DateTime InvoiceDate { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Subtotal { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }
        /// <summary>Everything that settled the invoice, credit notes included.</summary>
        public decimal AmountPaid { get; set; }

        public decimal CreditedAmount { get; set; }
        public string Status { get; set; } = string.Empty;

        /// <summary>Money actually received.</summary>
        public decimal CashPaid => AmountPaid - CreditedAmount;

        public decimal Outstanding => TotalAmount - AmountPaid;
        public bool IsOverdue => Outstanding > 0m && DueDate < DateTime.Today;
        public int DaysOverdue => IsOverdue ? (int)(DateTime.Today - DueDate).TotalDays : 0;
    }

    public class PortalReceiptViewModel
    {
        public string ReceiptNumber { get; set; } = string.Empty;
        public DateTime ReceiptDate { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string AgainstInvoice { get; set; } = string.Empty;
    }

    public class PortalPdcViewModel
    {
        public string ChequeNumber { get; set; } = string.Empty;
        public string? BankName { get; set; }
        public DateTime ChequeDate { get; set; }
        public DateTime ReceivedDate { get; set; }
        public DateTime? ClearedDate { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? AgainstInvoice { get; set; }
    }

    public class PortalStatementLineViewModel
    {
        public DateTime Date { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Charge { get; set; }
        public decimal Payment { get; set; }
        public decimal RunningBalance { get; set; }
    }

    public class PortalStatementViewModel
    {
        public string TenantName { get; set; } = string.Empty;
        public string TenantCode { get; set; } = string.Empty;
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public decimal OpeningBalance { get; set; }
        public List<PortalStatementLineViewModel> Lines { get; set; } = new();
        public decimal ClosingBalance { get; set; }

        public decimal TotalCharges => Lines.Sum(x => x.Charge);
        public decimal TotalPayments => Lines.Sum(x => x.Payment);
    }

    /// <summary>Staff-side view of a tenant's portal access.</summary>
    public class TenantPortalAccessViewModel
    {
        public int TenantId { get; set; }
        public string TenantCode { get; set; } = string.Empty;
        public string TenantName { get; set; } = string.Empty;
        public string? TenantEmail { get; set; }

        public bool HasAccess { get; set; }
        public string? Username { get; set; }
        public bool IsActive { get; set; }
        public bool MustChangePassword { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime? LockedOutUntil { get; set; }

        [Display(Name = "Portal username")]
        public string NewUsername { get; set; } = string.Empty;

        /// <summary>Shown once after issuing, never stored in readable form.</summary>
        public string? IssuedPassword { get; set; }
    }
}
