namespace RealEstateManager.Domain
{
    /// <summary>
    /// A company's link to its own WhatsApp Business number on Meta's Cloud API.
    ///
    /// The two credentials are never stored as typed: they are encrypted with the
    /// application's data protection keys (see WhatsAppConnectionService) and the App
    /// Secret is never shown again once saved. Only what receiving messages needs is kept:
    /// nothing here can send a message.
    /// </summary>
    public class WhatsAppConnection
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>
        /// The random part of this company's webhook address. It says which company's secret
        /// to check a delivery against, before anything in the delivery can be trusted.
        /// </summary>
        public string PublicKey { get; set; } = string.Empty;

        /// <summary>Meta's id for the number (not the number itself). A delivery for any other id is ignored.</summary>
        public string PhoneNumberId { get; set; } = string.Empty;

        /// <summary>The number as people know it, for the screens only.</summary>
        public string? DisplayPhoneNumber { get; set; }

        /// <summary>The Meta App Secret, encrypted. It signs every delivery.</summary>
        public string ProtectedAppSecret { get; set; } = string.Empty;

        /// <summary>The token Meta must quote when the webhook address is registered, encrypted.</summary>
        public string ProtectedVerifyToken { get; set; } = string.Empty;

        /// <summary>The country a phone number on a tenant's record belongs to when it is written without one, e.g. 971.</summary>
        public string DefaultCallingCode { get; set; } = "971";

        public bool IsEnabled { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
        public int? ChangedByAppUserId { get; set; }
    }

    /// <summary>
    /// One message that arrived from WhatsApp, kept whatever became of it. The unique
    /// message id is what makes a repeated delivery harmless. A message that could not safely
    /// be put on one tenant's record waits here for a member of staff.
    /// </summary>
    public class WhatsAppInboundMessage
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>Meta's id for the message ("wamid...").</summary>
        public string MessageId { get; set; } = string.Empty;

        /// <summary>
        /// Who sent it, as Meta identified them: normally the phone number in international
        /// digits, but an opaque id for a person who uses a WhatsApp username.
        /// </summary>
        public string Sender { get; set; } = string.Empty;

        /// <summary>The name the sender gave themselves on WhatsApp. Anyone can type anything, so it never decides a match.</summary>
        public string? ProfileName { get; set; }

        /// <summary>text, image, audio, location...</summary>
        public string MessageType { get; set; } = "text";

        /// <summary>The text or caption; empty for, say, a voice note.</summary>
        public string? Body { get; set; }

        /// <summary>When the person sent it, by Meta's clock (UTC).</summary>
        public DateTime SentAt { get; set; }

        /// <summary>When it reached us (UTC).</summary>
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

        /// <summary>See <see cref="WhatsAppMessageStatuses"/>.</summary>
        public string Status { get; set; } = WhatsAppMessageStatuses.Received;

        /// <summary>Why it needs a person. See <see cref="WhatsAppReviewReasons"/>.</summary>
        public string? ReviewReason { get; set; }

        /// <summary>The tenant it went to. Empty until it is on exactly one tenant's record.</summary>
        public int? TenantId { get; set; }
        public Tenant? Tenant { get; set; }

        public int? TenantServiceRequestId { get; set; }
        public TenantServiceRequest? TenantServiceRequest { get; set; }

        public int? HandledByAppUserId { get; set; }
        public DateTime? HandledAt { get; set; }
    }

    public static class WhatsAppMessageStatuses
    {
        /// <summary>Saved and being worked out. Only stays this way if the work was interrupted.</summary>
        public const string Received = "Received";

        /// <summary>Became a new request on the one tenant it matched.</summary>
        public const string RequestCreated = "RequestCreated";

        /// <summary>A further message from the same person, added to the request they had just opened.</summary>
        public const string AddedToRequest = "AddedToRequest";

        /// <summary>Waiting in the review list.</summary>
        public const string NeedsReview = "NeedsReview";

        /// <summary>A member of staff logged it for a tenant they chose.</summary>
        public const string LoggedByStaff = "LoggedByStaff";

        /// <summary>A member of staff decided it was not a request.</summary>
        public const string Dismissed = "Dismissed";

        public static string Describe(string status) => status switch
        {
            Received or NeedsReview => "Needs review",
            RequestCreated => "Request created",
            AddedToRequest => "Added to request",
            LoggedByStaff => "Logged by staff",
            Dismissed => "Dismissed",
            _ => status
        };
    }

    public static class WhatsAppReviewReasons
    {
        public const string NoMatch = "NoMatch";
        public const string SeveralTenants = "SeveralTenants";
        public const string NumberHidden = "NumberHidden";
        public const string NoText = "NoText";
        public const string SubscriptionInactive = "SubscriptionInactive";
        public const string CouldNotCreate = "CouldNotCreate";

        public static string Describe(string? reason) => reason switch
        {
            NoMatch => "No tenant has this phone number",
            SeveralTenants => "Several tenants have this phone number",
            NumberHidden => "WhatsApp did not give the phone number",
            NoText => "No text to make a request from",
            SubscriptionInactive => "Arrived while the subscription was inactive",
            _ => "The request could not be created automatically"
        };
    }
}
