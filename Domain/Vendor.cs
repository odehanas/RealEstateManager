namespace RealEstateManager.Domain
{
    public class Vendor
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? ServiceType { get; set; }
        public string? ContactPerson { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }

        // Supplier's TRN, required as evidence supporting an input tax claim.
        public string? TrnNumber { get; set; }

        // Commercial licence number. Unique per company when supplied, so the same
        // supplier cannot be created twice under two vendor codes.
        public string? TradeLicenseNumber { get; set; }

        // Opening balance brought forward from a legacy system / prior period.
        // Positive = payable to this vendor (credit nature in AP control account).
        // Negative = advance paid to this vendor (debit balance).
        public decimal OpeningBalance { get; set; } = 0m;

        public bool IsActive { get; set; } = true;
    }
}