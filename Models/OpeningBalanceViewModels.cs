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

        /// <summary>
        /// Typed in on this screen. Sub-ledger control accounts take their figure from
        /// the tenants, vendors or assets, and the opening balance control account is
        /// worked out from every other account, so neither is typed.
        /// </summary>
        public bool IsEditable { get; set; }
    }

    /// <summary>One account's figure as posted back from the Opening Balances screen.</summary>
    public class OpeningBalanceEntry
    {
        public int AccountId { get; set; }
        public decimal OpeningBalance { get; set; }
    }

    public class OpeningBalanceSubLedgerRow
    {
        public int EntityId { get; set; }
        public string EntityCode { get; set; } = string.Empty;
        public string EntityName { get; set; } = string.Empty;
        public decimal OpeningBalance { get; set; }
    }

    public class OpeningBalanceFixedAssetRow
    {
        public int AssetId { get; set; }
        public string AssetCode { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;
        public decimal OpeningCost { get; set; }
        public decimal OpeningAccumulatedDepreciation { get; set; }
        public decimal OpeningNetBookValue { get; set; }
        public string AssetAccountDisplay { get; set; } = string.Empty;
        public string AccumulatedDepreciationAccountDisplay { get; set; } = string.Empty;
    }
}
