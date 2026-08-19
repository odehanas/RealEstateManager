namespace RealEstateManager.Models
{
    public class FixedAssetRegisterViewModel
    {
        public DateTime PostingDate { get; set; } = DateTime.Today;
        public decimal TotalCost { get; set; }
        public decimal TotalAccumulatedDepreciation { get; set; }
        public decimal TotalNetBookValue { get; set; }
        public int PendingDepreciationCount { get; set; }
        public decimal PendingDepreciationAmount { get; set; }
        public List<FixedAssetRegisterItemViewModel> Assets { get; set; } = new();
    }

    public class FixedAssetRegisterItemViewModel
    {
        public int FixedAssetId { get; set; }
        public string AssetCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AssetCategory { get; set; } = string.Empty;
        public string? PropertyDisplay { get; set; }
        public DateTime AcquisitionDate { get; set; }
        public DateTime InServiceDate { get; set; }
        public DateTime? CapitalizationDate { get; set; }
        public decimal Cost { get; set; }
        public decimal ResidualValue { get; set; }
        public int UsefulLifeMonths { get; set; }
        public decimal MonthlyDepreciation { get; set; }
        public decimal AccumulatedDepreciation { get; set; }
        public decimal NetBookValue { get; set; }
        public string Status { get; set; } = string.Empty;
        public int PendingDepreciationCount { get; set; }
        public decimal PendingDepreciationAmount { get; set; }
    }

    public class FixedAssetCreateViewModel
    {
        public int? PropertyId { get; set; }
        public string AssetCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AssetCategory { get; set; } = string.Empty;
        public DateTime AcquisitionDate { get; set; } = DateTime.Today;
        public DateTime InServiceDate { get; set; } = DateTime.Today;
        public decimal Cost { get; set; }
        public decimal ResidualValue { get; set; }
        public int UsefulLifeMonths { get; set; }
        public int? AssetAccountId { get; set; }
        public int? AccumulatedDepreciationAccountId { get; set; }
        public int? DepreciationExpenseAccountId { get; set; }
        public int? InitialCreditAccountId { get; set; }
        public int? InitialVendorId { get; set; }
        public string? Notes { get; set; }

        // Sub-ledger opening balance brought forward (no journal entries are
        // generated; these feed the GL control accounts as opening balances).
        public decimal OpeningCost { get; set; }
        public decimal OpeningAccumulatedDepreciation { get; set; }
    }

    public class FixedAssetAcquisitionViewModel
    {
        public int FixedAssetId { get; set; }
        public string AssetCode { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;
        public string AssetCategory { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? PropertyDisplay { get; set; }
        public DateTime AcquisitionDate { get; set; }
        public DateTime InServiceDate { get; set; }
        public DateTime? CapitalizationDate { get; set; }
        public decimal ResidualValue { get; set; }
        public int UsefulLifeMonths { get; set; }
        public decimal CapitalizableCost { get; set; }
        public decimal OpenCapitalizableCost { get; set; }
        public int? DefaultClearingAccountId { get; set; }
        public int? DefaultOffsetAccountId { get; set; }
        public int? AccountsPayableAccountId { get; set; }
        public int? DefaultExpenseAccountId { get; set; }
        public List<FixedAssetAcquisitionCostItemViewModel> Costs { get; set; } = new();
    }

    public class FixedAssetAcquisitionCostItemViewModel
    {
        public DateTime CostDate { get; set; }
        public string CostType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public bool IsCapitalizable { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? SourceDocumentNumber { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? OffsetAccountDisplay { get; set; }
        public string? VendorDisplay { get; set; }
        public string? VendorBillNumber { get; set; }
        public string? ExpenseAccountDisplay { get; set; }
        public string? JournalEntryNumber { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CapitalizedAt { get; set; }
    }

    public class FixedAssetAcquisitionCostEntryViewModel
    {
        public int FixedAssetId { get; set; }
        public DateTime CostDate { get; set; } = DateTime.Today;
        public string CostType { get; set; } = "Purchase";
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public bool IsCapitalizable { get; set; } = true;
        public int? CreditAccountId { get; set; }
        public int? VendorId { get; set; }
        public int? ExpenseAccountId { get; set; }
        public string? SourceDocumentNumber { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Notes { get; set; }
    }

    public class FixedAssetCapitalizeViewModel
    {
        public int FixedAssetId { get; set; }
        public DateTime CapitalizationDate { get; set; } = DateTime.Today;
        public DateTime InServiceDate { get; set; } = DateTime.Today;
        public int? CreditAccountId { get; set; }
        public string? Remarks { get; set; }
    }

    public class FixedAssetDepreciationViewModel
    {
        public int FixedAssetId { get; set; }
        public string AssetCode { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;
        public decimal Cost { get; set; }
        public decimal ResidualValue { get; set; }
        public decimal MonthlyDepreciation { get; set; }
        public decimal PostedDepreciation { get; set; }
        public decimal NetBookValue { get; set; }
        public List<FixedAssetDepreciationItemViewModel> Schedules { get; set; } = new();
    }

    public class FixedAssetDepreciationItemViewModel
    {
        public DateTime DepreciationDate { get; set; }
        public string PeriodName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? JournalEntryNumber { get; set; }
        public DateTime? PostedAt { get; set; }
    }

    public class FixedAssetDepreciationRunResult
    {
        public int PostedCount { get; set; }
        public decimal PostedAmount { get; set; }
    }
}
