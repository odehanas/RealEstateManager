namespace RealEstateManager.Domain
{
    public class JournalEntryLine
    {
        public int Id { get; set; }

        public int JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public int AccountId { get; set; }
        public Account? Account { get; set; }

        public decimal Debit { get; set; } = 0m;
        public decimal Credit { get; set; } = 0m;

        public string? Description { get; set; }

        // Dimensions / optional subledger references
        public int? PropertyId { get; set; }
        public Property? Property { get; set; }

        public int? UnitId { get; set; }   // Cost center in your case
        public Unit? Unit { get; set; }

        public int? TenantId { get; set; }
        public Tenant? Tenant { get; set; }

        public int? VendorId { get; set; }
        public Vendor? Vendor { get; set; }

        public int? LeaseId { get; set; }
        public Lease? Lease { get; set; }

        public int? CustomerInvoiceId { get; set; }
        public CustomerInvoice? CustomerInvoice { get; set; }

        public int? VendorBillId { get; set; }
        public VendorBill? VendorBill { get; set; }

        public int? SecurityDepositId { get; set; }
        public SecurityDeposit? SecurityDeposit { get; set; }

        // Owner expenditure dimension. Only meaningful on lines posted to an account
        // flagged as an owner's current account (Account.IsOwnerAccount).
        public int? OwnerWithdrawalCategoryId { get; set; }

        /// <summary>The building under construction this cost belongs to.</summary>
        public int? DevelopmentProjectId { get; set; }
        public DevelopmentProject? DevelopmentProject { get; set; }

        /// <summary>What the development cost was for: land, construction, design...</summary>
        public string? DevelopmentCostCategory { get; set; }
        public OwnerWithdrawalCategory? OwnerWithdrawalCategory { get; set; }
    }
}