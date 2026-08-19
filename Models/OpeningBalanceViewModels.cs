namespace RealEstateManager.Models
{
    public class OpeningBalanceReconciliationViewModel
    {
        public List<OpeningBalanceAccountRow> Rows { get; set; } = new();

        public List<OpeningBalanceSubLedgerRow> TenantOpeningBalances { get; set; } = new();
        public List<OpeningBalanceSubLedgerRow> VendorOpeningBalances { get; set; } = new();
        public List<OpeningBalanceFixedAssetRow> FixedAssetOpeningBalances { get; set; } = new();

        public decimal TotalDebitOpeningBalance { get; set; }
        public decimal TotalCreditOpeningBalance { get; set; }
        public decimal NetImbalance => TotalDebitOpeningBalance - TotalCreditOpeningBalance;

        public decimal TenantOpeningTotal { get; set; }
        public decimal VendorOpeningTotal { get; set; }
        public decimal FixedAssetOpeningCostTotal { get; set; }
        public decimal FixedAssetOpeningAccumDepTotal { get; set; }
        public decimal FixedAssetOpeningNetBookValueTotal => FixedAssetOpeningCostTotal - FixedAssetOpeningAccumDepTotal;
    }

    public class OpeningBalanceAccountRow
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public string ReportingGroup { get; set; } = string.Empty;

        // Manual / AccountsReceivable / AccountsPayable / FixedAssetCost / FixedAssetAccumDepreciation
        public string Source { get; set; } = "Manual";

        public bool IsControlAccount { get; set; }

        public decimal ManualOpeningBalance { get; set; }
        public decimal SubLedgerTotal { get; set; }
        public decimal EffectiveOpeningBalance { get; set; }

        public decimal DebitOpeningBalance { get; set; }
        public decimal CreditOpeningBalance { get; set; }
    }

    public class OpeningBalanceSubLedgerRow
    {
        public string EntityCode { get; set; } = string.Empty;
        public string EntityName { get; set; } = string.Empty;
        public decimal OpeningBalance { get; set; }
    }

    public class OpeningBalanceFixedAssetRow
    {
        public string AssetCode { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;
        public decimal OpeningCost { get; set; }
        public decimal OpeningAccumulatedDepreciation { get; set; }
        public decimal OpeningNetBookValue { get; set; }
        public string AssetAccountDisplay { get; set; } = string.Empty;
        public string AccumulatedDepreciationAccountDisplay { get; set; } = string.Empty;
    }
}
