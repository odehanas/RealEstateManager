using RealEstateManager.Services;

namespace RealEstateManager.Models
{
    public class DatabaseUpdatesViewModel
    {
        public SchemaUpdateStatus Status { get; set; } = null!;

        // Null when the database has no SchemaScriptsApplied table yet.
        public IReadOnlyList<SchemaScriptRunner.AppliedScript>? History { get; set; }
        public string? HistoryError { get; set; }

        public int IncludedCount { get; set; }
        public IReadOnlyList<string> NotApplied { get; set; } = Array.Empty<string>();
    }
}
