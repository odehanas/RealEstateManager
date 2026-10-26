namespace RealEstateManager.Services.Assistant
{
    /// <summary>
    /// The "Assistant" configuration section. The API key is never put in
    /// appsettings.json: it comes from the hosting environment variable
    /// Assistant__OpenAIApiKey (and locally from dotnet user-secrets).
    /// </summary>
    public class AssistantOptions
    {
        public bool Enabled { get; set; }

        public string? OpenAIApiKey { get; set; }

        /// <summary>OpenAI realtime model. Checked against OpenAI's model list in October 2026.</summary>
        public string Model { get; set; } = "gpt-realtime-2.1-mini";

        public string Voice { get; set; } = "marin";

        /// <summary>Turns the user's speech into text for the on-screen transcript.</summary>
        public string TranscriptionModel { get; set; } = "gpt-4o-mini-transcribe";

        /// <summary>
        /// Vision model that reads scanned supplier invoices (the OCR). Uses the same key.
        /// GPT-5.4 mini, released March 2026, reads PDFs and images through the Responses API.
        /// </summary>
        public string DocumentModel { get; set; } = "gpt-5.4-mini";

        public int MaxSessionMinutes { get; set; } = 15;

        public int MaxToolCallsPerSession { get; set; } = 60;

        public int DailySessionLimitPerCompany { get; set; } = 100;

        /// <summary>Minutes a prepared entry waits for the user's Confirm click.</summary>
        public int PendingActionMinutes { get; set; } = 10;

        public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(OpenAIApiKey);
    }
}
