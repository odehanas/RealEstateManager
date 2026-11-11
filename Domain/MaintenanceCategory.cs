namespace RealEstateManager.Domain
{
    /// <summary>
    /// A kind of maintenance work a company sorts its jobs by: Plumbing, Electrical,
    /// Lifts... Each company keeps its own list. A job stores the NAME, not this row, so
    /// a job keeps its kind of work after the entry is switched off; renaming an entry
    /// renames it on the company's jobs and planned maintenance too.
    /// </summary>
    public class MaintenanceCategory
    {
        public const int MaxNameLength = 50;

        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Off: no longer offered for new jobs. Jobs that have it keep it.</summary>
        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByName { get; set; }
    }
}
