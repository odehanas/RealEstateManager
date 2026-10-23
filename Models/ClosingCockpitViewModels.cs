using RealEstateManager.Domain;

namespace RealEstateManager.Models
{
    /// <summary>Traffic light of one closing check, or of a whole period.</summary>
    public enum ClosingLight
    {
        /// <summary>Nothing to do here this period (no assets, no prepayments, ...).</summary>
        NotApplicable = 0,
        Green = 1,

        /// <summary>Worth a look, but does not stop the close once acknowledged.</summary>
        Amber = 2,

        /// <summary>Must be resolved: the period cannot be closed while any check is red.</summary>
        Red = 3
    }

    /// <summary>One line of the closing checklist.</summary>
    public class ClosingCheckItem
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;

        /// <summary>What the check proves, in one sentence, shown under the title.</summary>
        public string Rule { get; set; } = string.Empty;

        public ClosingLight Light { get; set; }
        public string Detail { get; set; } = string.Empty;

        /// <summary>What the schedules say should be in the ledger this period.</summary>
        public decimal? Expected { get; set; }

        /// <summary>What the general ledger actually holds for it.</summary>
        public decimal? Recorded { get; set; }

        /// <summary>A cockpit run that fixes it (see ClosingRunSteps), when there is one.</summary>
        public string? RunStep { get; set; }
        public string? RunLabel { get; set; }

        /// <summary>The screen where the user deals with it by hand.</summary>
        public string? LinkUrl { get; set; }
        public string? LinkLabel { get; set; }
    }

    public static class ClosingRunSteps
    {
        public const string Depreciation = "depreciation";
        public const string RevenueRecognition = "revenue";
        public const string Prepayments = "prepayments";
        public const string Invoices = "invoices";
        public const string Drafts = "drafts";
    }

    public class ClosingPeriodTile
    {
        public int Id { get; set; }
        public int FiscalYear { get; set; }
        public int PeriodNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsClosed { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? ClosedByName { get; set; }
        public DateTime? ReopenedAt { get; set; }
        public string? ReopenedByName { get; set; }
        public int ReopenCount { get; set; }

        /// <summary>Starts after today: nothing to close yet.</summary>
        public bool IsFuture { get; set; }

        /// <summary>Open again on an approved request, waiting to be closed again.</summary>
        public bool IsReopened { get; set; }
        public bool HasPendingRequest { get; set; }

        /// <summary>An earlier period is still open, so this one cannot be closed yet.</summary>
        public bool WaitsForEarlier { get; set; }

        public ClosingLight Light { get; set; }
        public int RedCount { get; set; }
        public int AmberCount { get; set; }
    }

    public class ClosingCockpitViewModel
    {
        public int FiscalYear { get; set; }
        public List<int> FiscalYears { get; set; } = new();
        public List<ClosingPeriodTile> Periods { get; set; } = new();

        public ClosingPeriodTile? Selected { get; set; }
        public List<ClosingCheckItem> Checks { get; set; } = new();
        public int RedCount => Checks.Count(x => x.Light == ClosingLight.Red);
        public int AmberCount => Checks.Count(x => x.Light == ClosingLight.Amber);
        public int GreenCount => Checks.Count(x => x.Light == ClosingLight.Green);

        /// <summary>Selected period is open, has started and its checklist has no red.</summary>
        public bool CanClose { get; set; }

        public PeriodReopenRequest? PendingRequest { get; set; }
        public List<PeriodReopenRequest> RequestHistory { get; set; } = new();
        public List<AccountingPeriodEvent> Events { get; set; } = new();

        /// <summary>Why a reopen request cannot be raised for the selected period, or null.</summary>
        public string? ReopenBlockReason { get; set; }

        public bool CanApproveReopen { get; set; }

        /// <summary>The company has one approver, who may then decide their own request.</summary>
        public bool OnlyApprover { get; set; }
        public List<PeriodReopenRequest> PendingRequests { get; set; } = new();
        public int CurrentUserId { get; set; }

        public FiscalYearClosePreview YearEnd { get; set; } = new();
        public List<FiscalYearClosingItemViewModel> FiscalYearClosings { get; set; } = new();
    }

    public class FiscalYearClosePreview
    {
        public int FiscalYear { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public bool IsClosed { get; set; }
        public bool WasReopened { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? ClosedByName { get; set; }
        public string? JournalEntryNumber { get; set; }
        public int? JournalEntryId { get; set; }

        /// <summary>Why the year cannot be closed yet. Empty when it can.</summary>
        public List<string> Blockers { get; set; } = new();
        public bool CanClose => !IsClosed && Blockers.Count == 0;

        public string? RetainedEarningsAccount { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetProfitOrLoss => TotalRevenue - TotalExpenses;
        public int RevenueAccounts { get; set; }
        public int ExpenseAccounts { get; set; }

        /// <summary>The balance sheet carried into the next year, once the year is closed.</summary>
        public List<FiscalYearCarryForward> CarryForwards { get; set; } = new();
    }

    public class FiscalYearClosingItemViewModel
    {
        public int FiscalYear { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal TotalRevenue { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal ProfitOrLossAmount { get; set; }
        public DateTime ClosedAt { get; set; }
        public string? ClosedByName { get; set; }
        public string? JournalEntryNumber { get; set; }
    }

    /// <summary>The approver's desk: every reopen request waiting, and the ones decided lately.</summary>
    public class ReopenRequestsViewModel
    {
        public List<PeriodReopenRequest> Pending { get; set; } = new();
        public List<PeriodReopenRequest> Recent { get; set; } = new();
        public int CurrentUserId { get; set; }
        public bool OnlyApprover { get; set; }
    }

    /// <summary>The dashboard's view of periods that are not closed yet.</summary>
    public class UnclosedPeriodsSummary
    {
        public List<UnclosedPeriodItem> Periods { get; set; } = new();
        public List<int> UnclosedFiscalYears { get; set; } = new();
        public int PendingReopenRequests { get; set; }
        public int OverdueCount => Periods.Count(x => x.DaysOverdue > 0);
    }

    public class UnclosedPeriodItem
    {
        public int FiscalYear { get; set; }
        public int PeriodNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        /// <summary>Days since the period ended; 0 for the current period.</summary>
        public int DaysOverdue { get; set; }
        public bool IsReopened { get; set; }
        public bool HasPendingRequest { get; set; }
        public int DraftJournals { get; set; }
    }
}
