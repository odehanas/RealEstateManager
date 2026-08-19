namespace RealEstateManager.Domain
{
    public class FixedAssetAcquisitionCost
    {
        public int Id { get; set; }

        public int FixedAssetId { get; set; }
        public FixedAsset? FixedAsset { get; set; }

        public DateTime CostDate { get; set; }
        public string CostType { get; set; } = "Purchase";
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; } = 0m;
        public bool IsCapitalizable { get; set; } = true;

        // Open, Capitalized, Cancelled
        public string Status { get; set; } = "Open";

        public string? SourceDocumentNumber { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Notes { get; set; }

        public int? OffsetAccountId { get; set; }
        public Account? OffsetAccount { get; set; }

        public int? VendorId { get; set; }
        public Vendor? Vendor { get; set; }

        public int? VendorBillId { get; set; }
        public VendorBill? VendorBill { get; set; }

        public int? ExpenseAccountId { get; set; }
        public Account? ExpenseAccount { get; set; }

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CapitalizedAt { get; set; }
    }
}
