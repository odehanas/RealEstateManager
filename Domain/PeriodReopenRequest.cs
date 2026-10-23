namespace RealEstateManager.Domain
{
    /// <summary>
    /// A request to open a closed accounting period again. The person who needs to post
    /// into it explains why; an approver (permission Closing.ApproveReopen) opens the
    /// period or turns the request down. Once the correction is made the period is closed
    /// again from the Closing Cockpit, which marks the request Reclosed. Every request is
    /// kept, so the history of why a period was reopened is never lost.
    /// </summary>
    public class PeriodReopenRequest
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int AccountingPeriodId { get; set; }
        public AccountingPeriod? AccountingPeriod { get; set; }

        public string Reason { get; set; } = string.Empty;

        public string Status { get; set; } = PeriodReopenStatuses.Pending;

        public int RequestedByAppUserId { get; set; }
        public string RequestedByName { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        public int? DecidedByAppUserId { get; set; }
        public string? DecidedByName { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? DecisionComment { get; set; }

        public DateTime? ReclosedAt { get; set; }
        public string? ReclosedByName { get; set; }
    }

    public static class PeriodReopenStatuses
    {
        public const string Pending = "Pending";

        /// <summary>Approved: the period is open again, waiting for the correction and to be closed again.</summary>
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Withdrawn = "Withdrawn";

        /// <summary>The period was closed again after the correction.</summary>
        public const string Reclosed = "Reclosed";
    }

    /// <summary>
    /// What happened to a period and when: closed, reopened, closed again, year closed.
    /// The cockpit shows it as the period's history.
    /// </summary>
    public class AccountingPeriodEvent
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int AccountingPeriodId { get; set; }
        public AccountingPeriod? AccountingPeriod { get; set; }

        public string Action { get; set; } = string.Empty;
        public string? Note { get; set; }

        public string ByName { get; set; } = string.Empty;
        public DateTime At { get; set; } = DateTime.UtcNow;
    }

    public static class AccountingPeriodActions
    {
        public const string Closed = "Closed";
        public const string ReopenRequested = "Reopen requested";
        public const string Reopened = "Reopened";
        public const string ReopenRejected = "Reopen rejected";
        public const string ReopenWithdrawn = "Reopen withdrawn";
        public const string Reclosed = "Closed again";
        public const string YearClosed = "Fiscal year closed";
        public const string YearReopened = "Fiscal year close undone";
    }
}
