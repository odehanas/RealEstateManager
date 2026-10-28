namespace RealEstateManager.Models
{
    public class AccountMappingViewModel
    {
        public int? DefaultCashAccountId { get; set; }
        public int? AccountsReceivableAccountId { get; set; }
        public int? AccountsPayableAccountId { get; set; }
        public int? AdvancesFromCustomersAccountId { get; set; }
        public int? UnearnedRevenueAccountId { get; set; }
        public int? TenantSecurityDepositLiabilityAccountId { get; set; }
        public int? VatRecoverableAccountId { get; set; }
        public int? VatPayableAccountId { get; set; }
        public int? DueToLandlordsAccountId { get; set; }
        public int? RentalRevenueOwnedAccountId { get; set; }
        public int? ManagementFeeRevenueAccountId { get; set; }
        public int? OtherIncomeAccountId { get; set; }
        public int? DirectExpenseDefaultAccountId { get; set; }
        public int? GeneralExpenseDefaultAccountId { get; set; }
        public int? DepreciationExpenseAccountId { get; set; }
        public int? FinanceCostAccountId { get; set; }
        public int? RetainedEarningsPriorYearsAccountId { get; set; }
        public int? CurrentYearProfitOrLossAccountId { get; set; }
        public int? InvestmentPropertyAccountId { get; set; }
        public int? PdcOnHandAccountId { get; set; }
        public int? PrepaidExpenseDefaultAccountId { get; set; }
        public int? PdcContraAccountId { get; set; }
        public int? ChequesUnderCollectionAccountId { get; set; }
        public int? PdcChargeIncomeAccountId { get; set; }

        /// <summary>Default charge for a cheque withdrawn at the tenant's request, before VAT.</summary>
        public decimal PdcWithdrawalCharge { get; set; } = 500m;

        /// <summary>Default charge for a returned cheque, before VAT; 0 for none.</summary>
        public decimal PdcBounceCharge { get; set; }
        public int? FixedAssetDefaultAccountId { get; set; }
        public int? FixedAssetClearingAccountId { get; set; }
        public int? AccumulatedDepreciationDefaultAccountId { get; set; }
    }
}
