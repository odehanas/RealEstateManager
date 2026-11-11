namespace RealEstateManager.Models
{
    /// <summary>The user's own two-step sign-in page: its state, or the key to set it up with.</summary>
    public class TwoStepPageViewModel
    {
        public bool Enabled { get; set; }
        public DateTime? EnabledAt { get; set; }
        public int RecoveryCodesLeft { get; set; }

        /// <summary>A company the user works in requires two-step sign-in.</summary>
        public bool Required { get; set; }

        /// <summary>While setting up: the key as text, and as a QR code for the app to scan.</summary>
        public string Key { get; set; } = string.Empty;
        public string QrDataUri { get; set; } = string.Empty;

        /// <summary>Recovery codes just issued, shown once.</summary>
        public List<string> NewRecoveryCodes { get; set; } = new();
    }
}
