namespace RealEstateManager.Domain
{
    public class Account
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        // Asset, Liability, Equity, Revenue, Expense
        public string AccountType { get; set; } = string.Empty;

        // Assets, Liabilities, Equity, Revenue, DirectExpense, GeneralExpense
        public string ReportingGroup { get; set; } = string.Empty;

        /// <summary>
        /// Income statement line of a revenue or expense account (see <see cref="StatementLines"/>).
        /// Null: the parent group's line, or the standard default.
        /// </summary>
        public string? StatementLine { get; set; }

        public bool IsControlAccount { get; set; } = false;

        // Marks an equity account that represents an owner's current account /
        // owner's withdrawals. Lines posted to such an account may carry an
        // OwnerWithdrawalCategory; lines posted to any other account may not.
        public bool IsOwnerAccount { get; set; } = false;

        // Marks an asset account that holds a petty cash float. Each flagged account
        // is a separate float with its own custodian and its own running balance.
        public bool IsPettyCashAccount { get; set; } = false;

        public bool IsGroup { get; set; } = false;
        public bool IsPostable { get; set; } = true;
        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; } = 0;

        public decimal OpeningBalance { get; set; } = 0m;

        public int? ParentAccountId { get; set; }
        public Account? ParentAccount { get; set; }

        public ICollection<Account> Children { get; set; } = new List<Account>();
        public ICollection<JournalEntryLine> JournalEntryLines { get; set; } = new List<JournalEntryLine>();
    }
}
