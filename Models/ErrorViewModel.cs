namespace RealEstateManager.Models
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        /// <summary>The page that failed.</summary>
        public string? Path { get; set; }

        /// <summary>The exception, filled only for the system administrator.</summary>
        public string? Detail { get; set; }
    }
}
