namespace RealEstateManager.Models
{
    public class PrepaymentScheduleListViewModel
    {
        public DateTime AsOfDate { get; set; } = DateTime.Today;
        public bool ShowPosted { get; set; } = true;
        public decimal OpenPrepaidBalance { get; set; }
        public decimal DueRecognitionAmount { get; set; }
        public int DueLineCount { get; set; }
        public List<PrepaymentScheduleListItemViewModel> Schedules { get; set; } = new();
    }

    public class PrepaymentScheduleListItemViewModel
    {
        public int PrepaymentScheduleId { get; set; }
        public string ScheduleNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PrepaymentType { get; set; } = string.Empty;
        public DateTime PaymentDate { get; set; }
        public DateTime FirstRecognitionDate { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal RecognizedAmount { get; set; }
        public decimal RemainingAmount { get; set; }
        public int RecognitionMonths { get; set; }
        public string Status { get; set; } = string.Empty;
        public string PrepaidAccountDisplay { get; set; } = string.Empty;
        public string ExpenseAccountDisplay { get; set; } = string.Empty;
        public string OffsetAccountDisplay { get; set; } = string.Empty;
        public string? VendorDisplay { get; set; }
        public string? VendorBillNumber { get; set; }
        public string? InitialJournalEntryNumber { get; set; }
        public int DueLineCount { get; set; }
        public decimal DueLineAmount { get; set; }
    }

    public class PrepaymentCreateViewModel
    {
        public string ScheduleNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PrepaymentType { get; set; } = "PrepaidExpense";
        public DateTime PaymentDate { get; set; } = DateTime.Today;
        public DateTime FirstRecognitionDate { get; set; } = DateTime.Today;
        public decimal TotalAmount { get; set; }
        public int RecognitionMonths { get; set; } = 12;
        public int? PrepaidAccountId { get; set; }
        public int? ExpenseAccountId { get; set; }
        public int? OffsetAccountId { get; set; }
        public int? VendorId { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Notes { get; set; }
    }
}
