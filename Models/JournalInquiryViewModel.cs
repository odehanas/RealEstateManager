namespace RealEstateManager.Models
{
    public class JournalInquiryViewModel
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? SourceType { get; set; }
        public bool PostedOnly { get; set; } = true;

        public List<JournalInquiryItemViewModel> Entries { get; set; } = new();
    }

    public class JournalInquiryItemViewModel
    {
        public int JournalEntryId { get; set; }
        public string EntryNumber { get; set; } = string.Empty;
        public DateTime EntryDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? SourceType { get; set; }
        public int? SourceId { get; set; }
        public bool IsPosted { get; set; }

        /// <summary>
        /// Period context, so the workflow buttons can be shown or hidden without a
        /// per-entry query. The expensive dependency checks run on click.
        /// </summary>
        public string? PeriodName { get; set; }
        public bool PeriodClosed { get; set; }

        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }

        public List<JournalInquiryLineViewModel> Lines { get; set; } = new();
    }

    public class JournalInquiryLineViewModel
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }
}