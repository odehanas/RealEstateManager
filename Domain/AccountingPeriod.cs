namespace RealEstateManager.Domain
{
    public class AccountingPeriod
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int FiscalYear { get; set; }
        public int PeriodNumber { get; set; }

        public string Name { get; set; } = string.Empty;

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsClosed { get; set; } = false;
        public DateTime? ClosedAt { get; set; }
    }
}