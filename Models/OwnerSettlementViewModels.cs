using System.ComponentModel.DataAnnotations;
using RealEstateManager.Domain;

namespace RealEstateManager.Models
{
    /// <summary>
    /// One landlord's settlement statement at a cut-off date: per contract, the rent
    /// billed, our commission and the owner's share, then what was paid already and
    /// what is still owed.
    /// </summary>
    public class OwnerSettlementStatementViewModel
    {
        public string OwnerName { get; set; } = string.Empty;
        public DateTime AsOfDate { get; set; } = DateTime.Today;
        public string CompanyName { get; set; } = string.Empty;

        public List<OwnerSettlementContractRow> Contracts { get; set; } = new();
        public List<OwnerSettlement> Settlements { get; set; } = new();

        /// <summary>Total rent of the agreements themselves, for the whole contract term.</summary>
        public decimal AgreementRent => Contracts.Sum(x => x.AgreementRent);

        public decimal RentBilled => Contracts.Sum(x => x.RentBilled);
        public decimal RentCollected => Contracts.Sum(x => x.RentCollected);
        public decimal Commission => Contracts.Sum(x => x.Commission);
        public decimal CommissionVat => Contracts.Sum(x => x.CommissionVat);

        /// <summary>5 when the company is VAT registered, otherwise 0.</summary>
        public decimal CommissionVatRate { get; set; }
        /// <summary>VAT on rent invoiced in the owner's name: collected for the owner, theirs to declare.</summary>
        public decimal RentVatForOwner => Contracts.Sum(x => x.RentVatForOwner);

        /// <summary>Maintenance and other costs of the owner's properties charged to the owner.</summary>
        public List<OwnerExpenseRow> Expenses { get; set; } = new();
        public decimal OwnerExpenses => Expenses.Sum(x => x.Amount);

        public decimal OwnerShare => Contracts.Sum(x => x.OwnerShare) - OwnerExpenses;
        public decimal OwnerShareCollected => Contracts.Sum(x => x.OwnerShareCollected) - OwnerExpenses;

        /// <summary>Settlements posted on or before the cut-off.</summary>
        public decimal PreviouslySettled { get; set; }

        /// <summary>Settlements saved but whose journal is not posted yet.</summary>
        public decimal PendingSettlements { get; set; }

        /// <summary>What the owner is still owed on the rent billed.</summary>
        public decimal BalancePayable => OwnerShare - PreviouslySettled;

        /// <summary>
        /// The part of the balance we actually hold in cash: the owner's share of rent
        /// collected, less what was paid. Paying more than this means paying the owner
        /// rent the tenant has not paid us yet.
        /// </summary>
        public decimal AvailableFromCollections => Math.Max(0m, Math.Min(OwnerShareCollected, OwnerShare) - PreviouslySettled);

        /// <summary>The most that may be paid now, after settlements still in draft.</summary>
        public decimal MaximumPayable => Math.Max(0m, BalancePayable - PendingSettlements);

        public bool HasProperties => PropertyCount > 0;

        /// <summary>The owner's managed properties, whether or not they are let yet.</summary>
        public int PropertyCount { get; set; }
    }

    /// <summary>A cost of an owner's property charged to the owner (Due to Landlords).</summary>
    public class OwnerExpenseRow
    {
        public DateTime Date { get; set; }
        public string PropertyCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string EntryNumber { get; set; } = string.Empty;
        public int JournalEntryId { get; set; }
        public decimal Amount { get; set; }
    }

    public class OwnerSettlementContractRow
    {
        /// <summary>VAT on the rent when it was invoiced in the owner's name (part of the owner's share).</summary>
        public decimal RentVatForOwner { get; set; }

        public int LeaseId { get; set; }
        public string PropertyCode { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string UnitNumber { get; set; } = string.Empty;
        public string ContractNumber { get; set; } = string.Empty;
        public string TenantName { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal AnnualRent { get; set; }

        /// <summary>Annual rent over the whole term of the contract.</summary>
        public decimal AgreementRent { get; set; }

        public decimal CommissionPercentage { get; set; }

        /// <summary>Rent invoiced to the tenant up to the cut-off, before VAT, after credit notes.</summary>
        public decimal RentBilled { get; set; }

        /// <summary>The part of the billed rent the tenant has paid, before VAT.</summary>
        public decimal RentCollected { get; set; }

        public decimal Commission { get; set; }
        public decimal CommissionVat { get; set; }
        public decimal OwnerShare { get; set; }
        public decimal OwnerShareCollected { get; set; }
    }

    /// <summary>One line on the owners overview.</summary>
    public class OwnerSettlementSummaryRow
    {
        public string OwnerName { get; set; } = string.Empty;
        public int PropertyCount { get; set; }
        public int ContractCount { get; set; }
        public decimal RentBilled { get; set; }
        public decimal Commission { get; set; }
        public decimal CommissionVat { get; set; }
        public decimal OwnerShare { get; set; }
        public decimal PreviouslySettled { get; set; }
        public decimal BalancePayable { get; set; }
        public decimal AvailableFromCollections { get; set; }
    }

    public class OwnerSettlementIndexViewModel
    {
        public DateTime AsOfDate { get; set; } = DateTime.Today;
        public List<OwnerSettlementSummaryRow> Owners { get; set; } = new();
        public List<OwnerSettlement> RecentSettlements { get; set; } = new();
        public List<OwnerCommissionInvoice> CommissionInvoices { get; set; } = new();
        public List<RealEstateManager.Services.PendingCommission> PendingCommission { get; set; } = new();

        /// <summary>The month end offered for the next commission invoice run.</summary>
        public DateTime InvoiceMonthEnd { get; set; }
    }

    /// <summary>The payment form at the foot of an owner's statement.</summary>
    public class OwnerSettlementPaymentInput
    {
        [Required]
        public string OwnerName { get; set; } = string.Empty;

        [DataType(DataType.Date)]
        public DateTime AsOfDate { get; set; } = DateTime.Today;

        [DataType(DataType.Date)]
        [Display(Name = "Payment date")]
        public DateTime SettlementDate { get; set; } = DateTime.Today;

        [Range(0.01, 999999999)]
        [Display(Name = "Amount to pay")]
        public decimal Amount { get; set; }

        [Display(Name = "Payment method")]
        public string PaymentMethod { get; set; } = "Bank Transfer";

        [Display(Name = "Paid from")]
        public int PaidFromAccountId { get; set; }

        [StringLength(100)]
        [Display(Name = "Cheque / transfer reference")]
        public string? ReferenceNumber { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }
    }
}
