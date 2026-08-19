namespace RealEstateManager.Domain
{
    /// <summary>
    /// An analysis dimension for amounts posted to an owner's equity account
    /// (owner's current account / owner's withdrawals).
    ///
    /// This is deliberately NOT part of the chart of accounts. The chart of accounts
    /// belongs to the company and drives the company's own Profit and Loss. Owner
    /// expenditure is not a company expense - it is a movement in equity - so it must
    /// post to a single equity GL and be analysed on a separate axis.
    ///
    /// It is also NOT a cost centre. Cost centres in this system are units, used to
    /// allocate rental revenue and property expenses to the space that generated them.
    /// Owner expenditure has nothing to do with a unit.
    /// </summary>
    public class OwnerWithdrawalCategory
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
