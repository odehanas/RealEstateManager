namespace RealEstateManager.Domain
{
    /// <summary>
    /// One conversation with the AI assistant: who, when, which model, and the usage the
    /// browser reported back from OpenAI, so cost can be followed per company.
    /// </summary>
    public class AssistantSession
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int AppUserId { get; set; }
        public string? UserName { get; set; }

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? EndedAt { get; set; }

        public string Model { get; set; } = string.Empty;

        /// <summary>"voice" or "text".</summary>
        public string Mode { get; set; } = "text";

        public int ToolCalls { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int InputAudioTokens { get; set; }
        public int OutputAudioTokens { get; set; }

        public string Status { get; set; } = AssistantSessionStatuses.Active;
    }

    public static class AssistantSessionStatuses
    {
        public const string Active = "Active";
        public const string Ended = "Ended";
    }

    /// <summary>A line of the conversation: what was said, or which tool ran with what.</summary>
    public class AssistantMessage
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int SessionId { get; set; }
        public AssistantSession? Session { get; set; }

        /// <summary>"user", "assistant" or "tool".</summary>
        public string Role { get; set; } = "user";
        public string? Text { get; set; }
        public string? ToolName { get; set; }
        public string? ToolArgsJson { get; set; }
        public string? ToolResultSummary { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Something the assistant prepared that changes the records. Nothing happens until
    /// the user clicks Confirm; then it runs through the same service the screen uses.
    /// </summary>
    public class AssistantPendingAction
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int AppUserId { get; set; }

        public int SessionId { get; set; }
        public AssistantSession? Session { get; set; }

        public string ToolName { get; set; } = string.Empty;
        public string ArgumentsJson { get; set; } = "{}";
        public string Summary { get; set; } = string.Empty;

        public string Status { get; set; } = AssistantActionStatuses.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        public DateTime? ExecutedAt { get; set; }
        public string? ResultJson { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public static class AssistantActionStatuses
    {
        public const string Pending = "Pending";
        public const string Executed = "Executed";
        public const string Cancelled = "Cancelled";
        public const string Expired = "Expired";
        public const string Failed = "Failed";
    }
}
