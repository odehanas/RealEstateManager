namespace RealEstateManager.Models
{
    public class ManualJournalEntryViewModel
    {
        public DateTime EntryDate { get; set; } = DateTime.Today;
        public string Description { get; set; } = string.Empty;
        public string? DocumentNumber { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Remarks { get; set; }

        public List<ManualJournalLineViewModel> Lines { get; set; } = new();
    }

    public class ManualJournalLineViewModel
    {
        public int? AccountId { get; set; }
        public string? LineDescription { get; set; }

        public decimal Debit { get; set; }
        public decimal Credit { get; set; }

        public int? UnitId { get; set; }      // Cost center
        public int? TenantId { get; set; }    // Optional AR ref
        public int? VendorId { get; set; }    // Optional AP ref
        public int? LeaseId { get; set; }     // Optional support ref

        // Owner expenditure dimension. Valid only on lines posted to an account
        // flagged as an owner's current account.
        public int? OwnerWithdrawalCategoryId { get; set; }

        // "projectId|category" for a cost capitalised into a development project.
        public string? DevelopmentCost { get; set; }
    }
}