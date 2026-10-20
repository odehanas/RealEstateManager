using System.ComponentModel.DataAnnotations;
using RealEstateManager.Domain;

namespace RealEstateManager.Models
{
    public class DevelopmentProjectFormViewModel
    {
        public int Id { get; set; }
        public string? Code { get; set; }

        [Required, StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Property (plot)")]
        public int? PropertyId { get; set; }

        [Display(Name = "Intended use")]
        public string IntendedUse { get; set; } = DevelopmentIntendedUses.Sale;

        [Display(Name = "Cost collected in")]
        public int WipAccountId { get; set; }

        [Display(Name = "Start date"), DataType(DataType.Date)]
        public DateTime? StartDate { get; set; }

        [Display(Name = "Expected completion"), DataType(DataType.Date)]
        public DateTime? ExpectedCompletionDate { get; set; }

        [Display(Name = "Main contractor")]
        public int? MainContractorVendorId { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        public List<DevelopmentBudgetInput> Budget { get; set; } = new();

        /// <summary>True once costs are posted: the account can no longer be changed.</summary>
        public bool HasCosts { get; set; }
    }

    public class DevelopmentBudgetInput
    {
        public string Category { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class DevelopmentProjectListRow
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? PropertyName { get; set; }
        public string IntendedUse { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string WipAccount { get; set; } = string.Empty;
        public decimal Budget { get; set; }
        public decimal CostToDate { get; set; }
        public decimal Transferred { get; set; }
        public decimal InWorkInProgress => CostToDate - Transferred;
        public decimal PercentOfBudget => Budget == 0m ? 0m : Math.Round(CostToDate / Budget * 100m, 1);
        public DateTime? ExpectedCompletionDate { get; set; }
        public DateTime? CompletionDate { get; set; }
    }

    public class DevelopmentProjectIndexViewModel
    {
        public List<DevelopmentProjectListRow> Projects { get; set; } = new();

        /// <summary>
        /// Amounts posted to a development account without a project. They are on the
        /// balance sheet but in no project's cost, so they need to be tagged or moved.
        /// </summary>
        public List<UntaggedDevelopmentBalance> Untagged { get; set; } = new();
    }

    public class UntaggedDevelopmentBalance
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class DevelopmentProjectDetailsViewModel
    {
        public DevelopmentProject Project { get; set; } = null!;

        public List<DevelopmentCategoryRow> Categories { get; set; } = new();
        public List<DevelopmentCostLine> Ledger { get; set; } = new();

        /// <summary>Cost moved out of work in progress by completion transfers.</summary>
        public decimal Transferred { get; set; }
        public decimal InWorkInProgress => CostToDate - Transferred;

        public List<DevelopmentTransfer> Transfers { get; set; } = new();
        public List<Unit> Units { get; set; } = new();

        /// <summary>Tagged lines saved but not posted yet - not in the cost until posted.</summary>
        public decimal DraftCosts { get; set; }
        public int DraftCount { get; set; }

        public decimal Budget => Categories.Sum(x => x.Budget);
        public decimal CostToDate => Categories.Sum(x => x.Actual);
        public decimal Remaining => Budget - CostToDate;
        public decimal PercentOfBudget => Budget == 0m ? 0m : Math.Round(CostToDate / Budget * 100m, 1);
    }

    public class DevelopmentCategoryRow
    {
        public string Category { get; set; } = string.Empty;
        public string Label => DevelopmentCostCategories.Describe(Category);
        public decimal Budget { get; set; }
        public decimal Actual { get; set; }
        public decimal Variance => Budget - Actual;
    }

    public class DevelopmentCostLine
    {
        public DateTime Date { get; set; }
        public int JournalEntryId { get; set; }
        public string EntryNumber { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string? DocumentNumber { get; set; }
        public string? VendorName { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>One choice in the "development cost" list on bill and journal lines.</summary>
    public class DevelopmentCostOption
    {
        public int ProjectId { get; set; }
        public string ProjectLabel { get; set; } = string.Empty;
        public int WipAccountId { get; set; }
        public int? PropertyId { get; set; }
    }
}
