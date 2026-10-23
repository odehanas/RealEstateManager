namespace RealEstateManager.Domain
{
    /// <summary>
    /// One month of a fiscal year. A closed period is locked: no journal can be created,
    /// posted, reset, edited or deleted in it (ApplicationDbContext enforces this on every
    /// save). It is closed from the Closing Cockpit once its checklist is green, and only
    /// an approver can open it again, on a written request - see PeriodReopenRequest.
    /// </summary>
    public class AccountingPeriod
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int FiscalYear { get; set; }
        public int PeriodNumber { get; set; }

        public string Name { get; set; } = string.Empty;

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsClosed { get; set; } = false;
        public DateTime? ClosedAt { get; set; }
        public string? ClosedByName { get; set; }

        /// <summary>When an approved request last opened the period again; null if it never was.</summary>
        public DateTime? ReopenedAt { get; set; }
        public string? ReopenedByName { get; set; }

        /// <summary>How many times the period has been reopened. Shown so repeated reopening stands out.</summary>
        public int ReopenCount { get; set; }
    }
}
