using System.ComponentModel.DataAnnotations;
using RealEstateManager.Services.WhatsApp;

namespace RealEstateManager.Models
{
    public class WhatsAppSettingsViewModel
    {
        public bool Exists { get; set; }

        public string? PublicKey { get; set; }

        [Display(Name = "Phone Number ID")]
        public string? PhoneNumberId { get; set; }

        [Display(Name = "WhatsApp number")]
        public string? DisplayPhoneNumber { get; set; }

        [Display(Name = "Country calling code")]
        public string? DefaultCallingCode { get; set; } = "971";

        [Display(Name = "Receive messages")]
        public bool IsEnabled { get; set; } = true;

        /// <summary>Goes in only: the stored one is never put back on the page.</summary>
        [Display(Name = "App Secret")]
        public string? AppSecret { get; set; }

        /// <summary>Generated here, and needed to register the webhook address with Meta.</summary>
        public string? VerifyToken { get; set; }
    }

    public class WhatsAppTenantOption
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Phone { get; set; }
    }

    public class WhatsAppInboxItemViewModel
    {
        public int Id { get; set; }
        public string Sender { get; set; } = string.Empty;
        public string? ProfileName { get; set; }
        public string MessageType { get; set; } = string.Empty;
        public string? Body { get; set; }
        public DateTime SentAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ReviewReason { get; set; }
        public string? TenantName { get; set; }
        public int? RequestId { get; set; }
        public string? RequestNumber { get; set; }

        /// <summary>Tenants whose record carries this number: a suggestion for the person deciding.</summary>
        public List<WhatsAppTenantOption> Candidates { get; set; } = new();

        public string SenderDisplay => WhatsAppPhoneNumbers.Display(Sender);
    }

    public class WhatsAppInboxViewModel
    {
        public bool ShowHandled { get; set; }
        public bool IsConnected { get; set; }
        public int WaitingCount { get; set; }
        public List<WhatsAppInboxItemViewModel> Items { get; set; } = new();
    }

    /// <summary>What staff choose when sorting a request that came in by itself from WhatsApp.</summary>
    public class ServiceRequestClassifyOptions
    {
        public string RequestType { get; set; } = string.Empty;
        public string? Category { get; set; }
        public int? LeaseId { get; set; }
        public List<ServiceRequestLeaseOption> Leases { get; set; } = new();

        /// <summary>Why this tenant could not raise maintenance on the portal, if so.</summary>
        public string? MaintenanceHold { get; set; }
    }
}
