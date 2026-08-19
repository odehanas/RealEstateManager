namespace RealEstateManager.Models
{
    /// <summary>
    /// Input for the _JournalStateButtons partial, so any document list can offer
    /// Post / Reset to Draft without knowing how the workflow is implemented.
    /// </summary>
    public class JournalStateButtonsViewModel
    {
        public int? JournalEntryId { get; set; }
        public bool IsPosted { get; set; }
        public bool PeriodClosed { get; set; }
        public string? PeriodName { get; set; }

        public static JournalStateButtonsViewModel For(Domain.JournalEntry? entry)
        {
            return new JournalStateButtonsViewModel
            {
                JournalEntryId = entry?.Id,
                IsPosted = entry?.IsPosted ?? false,
                PeriodClosed = entry?.AccountingPeriod?.IsClosed ?? false,
                PeriodName = entry?.AccountingPeriod?.Name
            };
        }
    }
}
