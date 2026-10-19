using System.ComponentModel.DataAnnotations;
using RealEstateManager.Domain;

namespace RealEstateManager.Models
{
    /// <summary>The e-invoicing work queue and its 14-day deadlines.</summary>
    public class EInvoiceMonitorViewModel
    {
        public DateTime Today { get; set; }

        public bool Enabled { get; set; }
        public string? RevenueBand { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? AppointmentDeadline { get; set; }
        public string? ProviderName { get; set; }
        public int CompanySetupProblems { get; set; }
        public int TenantsNeedingAttention { get; set; }

        public List<EInvoiceRowViewModel> Outstanding { get; set; } = new();
        public List<EInvoiceRowViewModel> AwaitingResponse { get; set; } = new();
        public List<EInvoiceRowViewModel> RecentlyAccepted { get; set; } = new();

        /// <summary>In-scope invoices whose supply date has passed but which are still drafts.</summary>
        public int UnpostedInScope { get; set; }

        public int OverdueCount => Outstanding.Count(x => x.IsOverdue);
        public int DueSoonCount => Outstanding.Count(x => !x.IsOverdue && x.DaysLeft <= 3);
        public int ReadyCount => Outstanding.Count(x => x.Status == EInvoiceStatuses.Generated);
        public int RejectedCount => Outstanding.Count(x => x.Status == EInvoiceStatuses.Rejected);
    }

    public class EInvoiceRowViewModel
    {
        public string Kind { get; set; } = string.Empty;
        public int Id { get; set; }
        public string Number { get; set; } = string.Empty;
        public DateTime IssueDate { get; set; }
        public DateTime SupplyDate { get; set; }
        public DateTime Deadline { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ProviderReference { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ResponseAt { get; set; }
        public int DaysLeft { get; set; }

        public bool IsOverdue => DaysLeft < 0;
        public bool IsCreditNote => Kind == "CreditNote";
        public string Key => $"{Kind}:{Id}";
    }

    /// <summary>The company's e-invoicing identity, switch and provider.</summary>
    public class EInvoicingSettingsViewModel
    {
        public string CompanyName { get; set; } = string.Empty;

        [Display(Name = "Registered legal name")]
        public string? LegalName { get; set; }

        [Display(Name = "TRN (Tax Registration Number)")]
        public string? TrnNumber { get; set; }

        [Display(Name = "TIN (Tax Identification Number)")]
        public string? TaxIdentificationNumber { get; set; }

        [Display(Name = "Registration type")]
        public string LegalRegistrationType { get; set; } = LegalRegistrationTypes.TradeLicence;

        [Display(Name = "Trade licence / registration number")]
        public string? TradeLicenseNumber { get; set; }

        [Display(Name = "Issuing authority")]
        public string? LicensingAuthority { get; set; }

        [Display(Name = "Street address")]
        public string? AddressLine1 { get; set; }

        [Display(Name = "Address line 2")]
        public string? AddressLine2 { get; set; }

        public string? City { get; set; }
        public string? Emirate { get; set; }

        public string? Phone { get; set; }
        public string? Email { get; set; }

        [Display(Name = "How tenants pay")]
        public string EInvoicePaymentMeansCode { get; set; } = EInvoicePaymentMeans.CreditTransfer;

        [Display(Name = "IBAN")]
        public string? BankIban { get; set; }

        [Display(Name = "Account name")]
        public string? BankAccountName { get; set; }

        [Display(Name = "E-invoicing is switched on")]
        public bool EInvoicingEnabled { get; set; }

        [Display(Name = "Revenue band")]
        public string? EInvoicingRevenueBand { get; set; }

        [Display(Name = "E-invoice supplies from")]
        public DateTime? EInvoicingStartDate { get; set; }

        [Display(Name = "Accredited Service Provider")]
        public string? EInvoicingProviderName { get; set; }

        [Display(Name = "Appointed on")]
        public DateTime? EInvoicingProviderAppointedOn { get; set; }

        // ---- Readiness ----------------------------------------------------------
        public string? DerivedTin { get; set; }
        public List<string> CompanyProblems { get; set; } = new();
        public int BusinessTenants { get; set; }
        public int GovernmentTenants { get; set; }
        public int IndividualTenants { get; set; }
        public List<EInvoiceTenantIssueViewModel> TenantIssues { get; set; } = new();
    }

    public class EInvoiceTenantIssueViewModel
    {
        public int TenantId { get; set; }
        public string TenantCode { get; set; } = string.Empty;
        public string TenantName { get; set; } = string.Empty;
        public List<string> Problems { get; set; } = new();
    }

    public class ReceivedEInvoiceListItemViewModel
    {
        public int Id { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public string DocumentTypeCode { get; set; } = string.Empty;
        public DateTime IssueDate { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public string? SupplierTrn { get; set; }
        public string? VendorName { get; set; }
        public int? VendorId { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public int? VendorBillId { get; set; }
        public string? VendorBillNumber { get; set; }
        public DateTime ReceivedAt { get; set; }

        public bool IsCreditNote => DocumentTypeCode is "381" or "81";
    }
}
