namespace RealEstateManager.Domain
{
    public class PrepaymentSchedule
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string ScheduleNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PrepaymentType { get; set; } = "PrepaidExpense";

        public DateTime PaymentDate { get; set; }
        public DateTime FirstRecognitionDate { get; set; }

        public decimal TotalAmount { get; set; } = 0m;
        public int RecognitionMonths { get; set; }

        public int PrepaidAccountId { get; set; }
        public Account? PrepaidAccount { get; set; }

        public int ExpenseAccountId { get; set; }
        public Account? ExpenseAccount { get; set; }

        public int OffsetAccountId { get; set; }
        public Account? OffsetAccount { get; set; }

        public int? VendorId { get; set; }
        public Vendor? Vendor { get; set; }

        public int? VendorBillId { get; set; }
        public VendorBill? VendorBill { get; set; }

        public int? InitialJournalEntryId { get; set; }
        public JournalEntry? InitialJournalEntry { get; set; }

        // Active, Completed, Cancelled
        public string Status { get; set; } = "Active";
        public string? ReferenceNumber { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<PrepaymentScheduleLine> Lines { get; set; } = new List<PrepaymentScheduleLine>();
    }
}
