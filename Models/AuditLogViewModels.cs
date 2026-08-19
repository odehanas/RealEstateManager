namespace RealEstateManager.Models
{
    public class AuditLogFilterViewModel
    {
        public DateTime? FromDate { get; set; } = DateTime.Today.AddDays(-7);
        public DateTime? ToDate { get; set; } = DateTime.Today;
        public string? Username { get; set; }
        public string? ControllerName { get; set; }
        public string? HttpMethod { get; set; }
        public bool OnlyFailures { get; set; }
    }

    public class AuditLogListViewModel
    {
        public AuditLogFilterViewModel Filter { get; set; } = new();
        public List<string> AvailableControllers { get; set; } = new();
        public List<AuditLogListItemViewModel> Items { get; set; } = new();
    }

    public class AuditLogListItemViewModel
    {
        public int Id { get; set; }
        public string? Username { get; set; }
        public string? DisplayName { get; set; }
        public string? CompanyName { get; set; }
        public string HttpMethod { get; set; } = string.Empty;
        public string RequestPath { get; set; } = string.Empty;
        public string? ControllerName { get; set; }
        public string? ActionName { get; set; }
        public string? ActivityName { get; set; }
        public bool IsSuccess { get; set; }
        public int StatusCode { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime OccurredAt { get; set; }
        public string? IpAddress { get; set; }
    }
}
