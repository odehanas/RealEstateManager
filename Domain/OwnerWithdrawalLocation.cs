namespace RealEstateManager.Domain
{
    /// <summary>
    /// The second analysis dimension on the owner's withdrawals, beside the category:
    /// the category says what the money was spent on, the location says where (the
    /// house, the farm, the private office...). Any category can be used at any location,
    /// so "Utilities at the farm" and "Utilities at the house" need only one category.
    ///
    /// Like the category it is not an account and not a cost centre: these places are the
    /// owner's own, not the units whose revenue and costs the company tracks.
    /// </summary>
    public class OwnerWithdrawalLocation
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        public int SortOrder { get; set; } = 0;
        public bool IsActive { get; set; } = true;

        public ICollection<JournalEntryLine> JournalEntryLines { get; set; } = new List<JournalEntryLine>();
    }
}
