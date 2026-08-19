namespace RealEstateManager.Models
{
    public class ChartOfAccountsIndexViewModel
    {
        public List<AccountTreeViewModel> Accounts { get; set; } = new();
        public OpeningBalanceSummaryViewModel OpeningBalances { get; set; } = new();
    }

    /// <summary>
    /// Opening balance position shown at the top of the Chart of Accounts, so the
    /// person entering the balances sees immediately whether they still balance
    /// instead of discovering it later on the Trial Balance.
    /// </summary>
    public class OpeningBalanceSummaryViewModel
    {
        /// <summary>Debit-nature total, presented positive.</summary>
        public decimal TotalDebit { get; set; }

        /// <summary>Credit-nature total, presented positive.</summary>
        public decimal TotalCredit { get; set; }

        // Natural balances by type: assets debit-positive, the rest credit-positive.
        public decimal Assets { get; set; }
        public decimal Liabilities { get; set; }
        public decimal Equity { get; set; }
        public decimal Revenue { get; set; }
        public decimal Expenses { get; set; }

        public int AccountsWithOpeningBalance { get; set; }

        /// <summary>Whether account 39999 exists yet.</summary>
        public bool HasControlAccount { get; set; }

        /// <summary>
        /// Balance sitting on 39999, credit-positive. Zero means the opening position is
        /// complete. Non-zero is the amount still unaccounted for.
        /// </summary>
        public decimal ControlAccountBalance { get; set; }

        /// <summary>Equity excluding the control account - the real equity figure.</summary>
        public decimal EquityExcludingControl { get; set; }

        /// <summary>
        /// Debits less credits across everything. With the control account in place this
        /// is always zero by construction, which is the whole point.
        /// </summary>
        public decimal Difference => TotalDebit - TotalCredit;

        /// <summary>
        /// Complete when the control account is empty. Without a control account this
        /// falls back to the raw debit/credit test.
        /// </summary>
        public bool IsBalanced => HasControlAccount
            ? ControlAccountBalance == 0m
            : Difference == 0m;

        /// <summary>Liabilities + equity, excluding the suspense account.</summary>
        public decimal LiabilitiesAndEquity => Liabilities + EquityExcludingControl;

        /// <summary>
        /// Assets less liabilities and real equity. This is what the control account is
        /// holding, and what still has to be entered.
        /// </summary>
        public decimal EquationGap => Assets - LiabilitiesAndEquity;

        public bool HasProfitAndLossOpeningBalances => Revenue != 0m || Expenses != 0m;

        public bool HasAnyOpeningBalance => TotalDebit != 0m || TotalCredit != 0m;
    }
}
