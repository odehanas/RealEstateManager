namespace RealEstateManager.Models
{
    public class AccountFormViewModel
    {
        public int? Id { get; set; }

        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public string AccountType { get; set; } = string.Empty;
        public string ReportingGroup { get; set; } = string.Empty;

        /// <summary>Income statement line; empty = as its group / the default.</summary>
        public string? StatementLine { get; set; }

        /// <summary>What the line is when left empty, shown on the form.</summary>
        public string? InheritedStatementLine { get; set; }

        public int? ParentAccountId { get; set; }

        public bool IsGroup { get; set; } = false;
        public bool IsPostable { get; set; } = true;
        public bool IsActive { get; set; } = true;

        // Marks an equity account as an owner's current account, which enables the
        // owner withdrawal category dimension on journal lines posted to it.
        public bool IsOwnerAccount { get; set; } = false;

        // Marks an asset account as a petty cash float, which makes it available in
        // the Petty Cash journal and gives it its own running balance there.
        public bool IsPettyCashAccount { get; set; } = false;

        public int SortOrder { get; set; } = 0;

        // Opening balance entered as a NATURAL value (debit-positive for Asset /
        // Expense, credit-positive for Liability / Equity / Revenue).
        // For sub-ledger control accounts (AR / AP / Fixed Asset) this field is
        // read-only and the displayed value is computed from sub-ledger detail.
        public decimal OpeningBalance { get; set; } = 0m;

        // Display-only context filled by the controller for the form view.
        public bool IsOpeningBalanceReadOnly { get; set; } = false;
        public string? OpeningBalanceSource { get; set; }
        public decimal OpeningBalanceFromSubLedger { get; set; } = 0m;
    }
}