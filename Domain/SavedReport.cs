namespace RealEstateManager.Domain
{
    /// <summary>
    /// A report someone built in the Report Builder and saved: its definition (source,
    /// columns, filters, grouping, chart) as JSON. Shared reports are listed for everyone
    /// in the company; each person still sees only data their own permissions allow.
    /// </summary>
    public class SavedReport
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int AppUserId { get; set; }
        public string OwnerName { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
        public string DefinitionJson { get; set; } = "{}";
        public bool IsShared { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
