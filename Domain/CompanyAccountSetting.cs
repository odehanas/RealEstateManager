namespace RealEstateManager.Domain
{
    public class CompanyAccountSetting
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int? DefaultCashAccountId { get; set; }
        public Account? DefaultCashAccount { get; set; }

        public int? AccountsReceivableAccountId { get; set; }
        public Account? AccountsReceivableAccount { get; set; }

        public int? AccountsPayableAccountId { get; set; }
        public Account? AccountsPayableAccount { get; set; }

        public int? AdvancesFromCustomersAccountId { get; set; }
        public Account? AdvancesFromCustomersAccount { get; set; }

        public int? UnearnedRevenueAccountId { get; set; }
        public Account? UnearnedRevenueAccount { get; set; }

        public int? TenantSecurityDepositLiabilityAccountId { get; set; }
        public Account? TenantSecurityDepositLiabilityAccount { get; set; }

        public int? VatRecoverableAccountId { get; set; }
        public Account? VatRecoverableAccount { get; set; }

        public int? VatPayableAccountId { get; set; }
        public Account? VatPayableAccount { get; set; }

        public int? DueToLandlordsAccountId { get; set; }
        public Account? DueToLandlordsAccount { get; set; }

        public int? RentalRevenueOwnedAccountId { get; set; }
        public Account? RentalRevenueOwnedAccount { get; set; }

        public int? ManagementFeeRevenueAccountId { get; set; }
        public Account? ManagementFeeRevenueAccount { get; set; }

        public int? OtherIncomeAccountId { get; set; }
        public Account? OtherIncomeAccount { get; set; }

        public int? DirectExpenseDefaultAccountId { get; set; }
        public Account? DirectExpenseDefaultAccount { get; set; }

        public int? GeneralExpenseDefaultAccountId { get; set; }
        public Account? GeneralExpenseDefaultAccount { get; set; }

        public int? DepreciationExpenseAccountId { get; set; }
        public Account? DepreciationExpenseAccount { get; set; }

        public int? FinanceCostAccountId { get; set; }
        public Account? FinanceCostAccount { get; set; }

        public int? RetainedEarningsPriorYearsAccountId { get; set; }
        public Account? RetainedEarningsPriorYearsAccount { get; set; }

        public int? CurrentYearProfitOrLossAccountId { get; set; }
        public Account? CurrentYearProfitOrLossAccount { get; set; }

        public int? InvestmentPropertyAccountId { get; set; }
        public Account? InvestmentPropertyAccount { get; set; }

        public int? PdcOnHandAccountId { get; set; }
        public Account? PdcOnHandAccount { get; set; }

        public int? PrepaidExpenseDefaultAccountId { get; set; }
        public Account? PrepaidExpenseDefaultAccount { get; set; }

        public int? PdcContraAccountId { get; set; }
        public Account? PdcContraAccount { get; set; }

        public int? FixedAssetDefaultAccountId { get; set; }
        public Account? FixedAssetDefaultAccount { get; set; }

        public int? FixedAssetClearingAccountId { get; set; }
        public Account? FixedAssetClearingAccount { get; set; }

        public int? AccumulatedDepreciationDefaultAccountId { get; set; }
        public Account? AccumulatedDepreciationDefaultAccount { get; set; }
    }
}
