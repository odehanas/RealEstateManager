using System.ComponentModel.DataAnnotations;

namespace RealEstateManager.Models
{
    public class PettyCashDashboardViewModel
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool HasFloats { get; set; }
        public int? SelectedPettyCashAccountId { get; set; }

        public List<PettyCashFloatViewModel> Floats { get; set; } = new();
        public List<PettyCashVoucherViewModel> Vouchers { get; set; } = new();
        public List<PettyCashExpenseSummaryViewModel> ExpenseSummary { get; set; } = new();

        public decimal TotalOpeningBalance => Floats.Sum(x => x.OpeningBalance);
        public decimal TotalReplenishments => Floats.Sum(x => x.Replenishments);
        public decimal TotalExpenses => Floats.Sum(x => x.Expenses);
        public decimal TotalClosingBalance => Floats.Sum(x => x.ClosingBalance);

        public int VoucherCount => Vouchers.Count;

        public decimal ShareOfExpenses(decimal amount)
        {
            var total = ExpenseSummary.Sum(x => Math.Max(0m, x.Amount));
            return total <= 0m ? 0m : Math.Round(Math.Max(0m, amount) / total * 100m, 1);
        }
    }

    public class PettyCashFloatViewModel
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public bool IsActive { get; set; }

        public decimal OpeningBalance { get; set; }
        public decimal Replenishments { get; set; }
        public decimal Expenses { get; set; }
        public decimal ClosingBalance { get; set; }

        public string Display => $"{AccountCode} - {AccountName}";
    }

    public class PettyCashVoucherViewModel
    {
        public int JournalEntryId { get; set; }
        public DateTime EntryDate { get; set; }
        public string EntryNumber { get; set; } = string.Empty;
        public string? VoucherNumber { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Description { get; set; }
        public string PettyCashAccountDisplay { get; set; } = string.Empty;
        public string CounterpartDisplay { get; set; } = string.Empty;

        public decimal ExpenseAmount { get; set; }
        public decimal ReplenishmentAmount { get; set; }

        public bool IsReplenishment => ReplenishmentAmount > 0m;
    }

    public class PettyCashExpenseSummaryViewModel
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public int VoucherCount { get; set; }
    }

    public class PettyCashExpenseViewModel
    {
        [Display(Name = "Petty Cash Float")]
        public int PettyCashAccountId { get; set; }

        [Display(Name = "Expense Account")]
        public int ExpenseAccountId { get; set; }

        [Display(Name = "Voucher Date")]
        [DataType(DataType.Date)]
        public DateTime VoucherDate { get; set; } = DateTime.Today;

        [Required]
        [Display(Name = "Description")]
        [StringLength(300)]
        public string Description { get; set; } = string.Empty;

        [Display(Name = "Paid To")]
        [StringLength(200)]
        public string? PaidTo { get; set; }

        [Display(Name = "Receipt / Reference")]
        [StringLength(100)]
        public string? ReferenceNumber { get; set; }

        [Display(Name = "Amount")]
        public decimal Amount { get; set; }

        [Display(Name = "Property")]
        public int? PropertyId { get; set; }

        [Display(Name = "Unit Cost Center")]
        public int? UnitId { get; set; }

        /// <summary>Charge the voucher on to the owner of the (managed) property, with 5% VAT.</summary>
        [Display(Name = "Charge to the owner (+5% VAT)")]
        public bool RechargeToOwner { get; set; }

        /// <summary>Live balance of the selected float, shown on the form.</summary>
        public decimal CurrentFloatBalance { get; set; }
    }

    public class PettyCashReplenishmentViewModel
    {
        [Display(Name = "Petty Cash Float")]
        public int PettyCashAccountId { get; set; }

        [Display(Name = "Funded From")]
        public int FundingAccountId { get; set; }

        [Display(Name = "Date")]
        [DataType(DataType.Date)]
        public DateTime ReplenishmentDate { get; set; } = DateTime.Today;

        [Required]
        [Display(Name = "Description")]
        [StringLength(300)]
        public string Description { get; set; } = "Petty cash top-up";

        [Display(Name = "Reference")]
        [StringLength(100)]
        public string? ReferenceNumber { get; set; }

        [Display(Name = "Amount")]
        public decimal Amount { get; set; }
    }
}
