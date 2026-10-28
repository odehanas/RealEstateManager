namespace RealEstateManager.Domain
{
    /// <summary>Who a notification subscription belongs to: each side of the application signs in separately.</summary>
    public static class PushAudiences
    {
        public const string Staff = "Staff";
        public const string Tenant = "Tenant";
        public const string Employee = "Employee";
    }

    /// <summary>
    /// One device that agreed to receive notifications, as the browser described it when
    /// the person turned notifications on. Exactly one of the three user ids is set.
    /// </summary>
    public class PushSubscription
    {
        public int Id { get; set; }

        public string Audience { get; set; } = PushAudiences.Staff;

        public int? AppUserId { get; set; }
        public int? TenantPortalUserId { get; set; }
        public int? EmployeePortalUserId { get; set; }

        /// <summary>The company the person was in when they subscribed; for tenants and employees, theirs.</summary>
        public int? CompanyId { get; set; }

        /// <summary>The push service's address for this device (Google, Apple or Mozilla).</summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>SHA-256 of the endpoint, so a device is registered once whatever its address length.</summary>
        public string EndpointHash { get; set; } = string.Empty;

        /// <summary>The device's public key and secret, used to encrypt what is sent to it.</summary>
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;

        /// <summary>"en" or "ar": the language the notifications are written in.</summary>
        public string Language { get; set; } = "en";

        public string? UserAgent { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastSentAt { get; set; }

        /// <summary>Consecutive failed sends; a device that keeps failing is dropped.</summary>
        public int FailureCount { get; set; }
    }
}
