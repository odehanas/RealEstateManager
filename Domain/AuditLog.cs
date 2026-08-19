namespace RealEstateManager.Domain
{
    public class AuditLog
    {
        public int Id { get; set; }

        public int? AppUserId { get; set; }
        public AppUser? AppUser { get; set; }

        public int? CompanyId { get; set; }
        public Company? Company { get; set; }

        public string? Username { get; set; }
        public string? DisplayName { get; set; }
        public string? CompanyName { get; set; }
        public string HttpMethod { get; set; } = string.Empty;
        public string RequestPath { get; set; } = string.Empty;
        public string? QueryString { get; set; }
        public string? ControllerName { get; set; }
        public string? ActionName { get; set; }
        public string? ActivityName { get; set; }
        public bool IsSuccess { get; set; } = true;
        public int StatusCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? IpAddress { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }
}
