using Microsoft.AspNetCore.Mvc.Rendering;
using RealEstateManager.Domain;

namespace RealEstateManager.Models
{
    /// <summary>Raise or amend a draft credit note against one invoice.</summary>
    public class CreditNoteEditViewModel
    {
        public int Id { get; set; }
        public int CustomerInvoiceId { get; set; }

        public DateTime CreditNoteDate { get; set; } = DateTime.Today;
        public string ReasonCode { get; set; } = CreditNoteReasons.ConsiderationAltered;
        public string Reason { get; set; } = string.Empty;

        /// <summary>Net amount to credit, before VAT.</summary>
        public decimal NetAmount { get; set; }

        /// <summary>Credit everything still outstanding, ignoring NetAmount.</summary>
        public bool CreditFullOutstanding { get; set; }

        /// <summary>Optional override of the account the net amount reverses into.</summary>
        public int? DebitAccountId { get; set; }

        /// <summary>Post straight away instead of leaving a draft.</summary>
        public bool PostNow { get; set; }

        // ---- Read-only context ------------------------------------------------
        public string? CreditNoteNumber { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public string? TaxInvoiceNumber { get; set; }
        public DateTime InvoiceDate { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public string PropertyUnit { get; set; } = string.Empty;
        public string ContractNumber { get; set; } = string.Empty;
        public string VatCategory { get; set; } = VatCategories.Standard;
        public decimal VatRate { get; set; }
        public decimal InvoiceSubtotal { get; set; }
        public decimal InvoiceVat { get; set; }
        public decimal InvoiceTotal { get; set; }
        public decimal AlreadySettled { get; set; }
        public decimal Outstanding { get; set; }
        public string InvoiceEInvoiceStatus { get; set; } = EInvoiceStatuses.NotRequired;
        public string? DefaultAccountName { get; set; }

        public List<SelectListItem> Accounts { get; set; } = new();
    }

    public class CreditNoteListItemViewModel
    {
        public int Id { get; set; }
        public string CreditNoteNumber { get; set; } = string.Empty;
        public DateTime CreditNoteDate { get; set; }
        public int CustomerInvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public string TenantName { get; set; } = string.Empty;
        public string ReasonCode { get; set; } = string.Empty;
        public decimal Subtotal { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public bool IsPosted { get; set; }
        public string EInvoiceStatus { get; set; } = EInvoiceStatuses.NotRequired;
    }

    /// <summary>The printable tax credit note.</summary>
    public class CreditNoteDocumentViewModel
    {
        public string Title { get; set; } = "Tax Credit Note";
        public string CreditNoteNumber { get; set; } = string.Empty;
        public DateTime CreditNoteDate { get; set; }
        public string ReasonCode { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public bool IsPosted { get; set; }

        public string SupplierName { get; set; } = string.Empty;
        public string? SupplierTrn { get; set; }
        public string? SupplierAddress { get; set; }

        public string TenantName { get; set; } = string.Empty;
        public string? TenantAddress { get; set; }
        public string? TenantTrn { get; set; }

        public string OriginalInvoiceNumber { get; set; } = string.Empty;
        public DateTime OriginalInvoiceDate { get; set; }
        public string PropertyUnit { get; set; } = string.Empty;
        public string ContractNumber { get; set; } = string.Empty;

        public string VatCategory { get; set; } = VatCategories.Standard;
        public decimal VatRate { get; set; }
        public decimal Subtotal { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }

        public string EInvoiceStatus { get; set; } = EInvoiceStatuses.NotRequired;
        public Guid? EInvoiceUuid { get; set; }
    }
}
