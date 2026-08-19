namespace RealEstateManager.Domain
{
    public class Tenant
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Code { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Nationality { get; set; }
        public string? IdNumber { get; set; }
        public string? PassportNumber { get; set; }
        public string? Address { get; set; }

        // Commercial licence number for a corporate tenant. Together with the Emirates
        // ID, passport and TRN this is one of the identifiers the system refuses to
        // duplicate, so the same party cannot be set up twice under two codes.
        public string? TradeLicenseNumber { get; set; }

        // Tax Registration Number. When present the tenant gets a FULL tax invoice
        // showing their TRN; when absent, a simplified tax invoice.
        public string? TrnNumber { get; set; }

        // Opening balance brought forward from a legacy system / prior period.
        // Positive = receivable from this tenant (debit nature in AR control account).
        // Negative = advance held for this tenant (credit balance).
        public decimal OpeningBalance { get; set; } = 0m;

        public bool IsActive { get; set; } = true;

        public ICollection<Lease> Leases { get; set; } = new List<Lease>();
    }
}