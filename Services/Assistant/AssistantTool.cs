using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services.Assistant
{
    /// <summary>
    /// One thing the assistant can do. Reads return data. Writes never change anything:
    /// <see cref="Propose"/> checks and describes the change, and <see cref="Execute"/>
    /// runs only after the user clicks Confirm.
    /// </summary>
    public sealed class AssistantTool
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;

        /// <summary>JSON schema of the arguments.</summary>
        public JsonObject Parameters { get; init; } = Schema.Object();

        /// <summary>
        /// The screen whose permission this tool carries, resolved through
        /// PermissionCatalog exactly as the screen itself is. Null: Assistant.Use alone.
        /// </summary>
        public (string Controller, string Action)? Screen { get; init; }

        public bool IsWrite { get; init; }

        public Func<AssistantToolContext, JsonElement, Task<object?>>? Run { get; init; }
        public Func<AssistantToolContext, JsonElement, Task<AssistantProposal>>? Propose { get; init; }
        public Func<AssistantToolContext, JsonElement, Task<AssistantActionResult>>? Execute { get; init; }

        public IReadOnlyList<string> RequiredPermissions =>
            Screen.HasValue
                ? PermissionCatalog.ResolvePermissions(Screen.Value.Controller, Screen.Value.Action)
                : Array.Empty<string>();
    }

    /// <summary>Who is asking, in which company: always taken from the sign-in cookie, never from the browser or the model.</summary>
    public sealed class AssistantToolContext
    {
        public required ClaimsPrincipal User { get; init; }
        public required int CompanyId { get; init; }
        public required int UserId { get; init; }
        public required string UserName { get; init; }
        public required string Currency { get; init; }
        public required ApplicationDbContext Db { get; init; }
        public required IServiceProvider Services { get; init; }
        public AssistantSession? Session { get; init; }

        /// <summary>The screen the user is on, as the page reported it. Information only.</summary>
        public string? Screen { get; init; }

        public T Get<T>() where T : notnull => Services.GetRequiredService<T>();
    }

    /// <summary>A prepared change, shown to the user to confirm.</summary>
    public sealed class AssistantProposal
    {
        public bool Ok { get; init; }
        public string? Error { get; init; }
        public string Summary { get; init; } = string.Empty;
        public List<string> Warnings { get; init; } = new();

        /// <summary>The fully resolved arguments Execute will receive (ids, not names).</summary>
        public object? Normalized { get; init; }

        public static AssistantProposal Refused(string error) => new() { Ok = false, Error = error };
    }

    public sealed class AssistantActionResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = string.Empty;
        public string? Reference { get; init; }
        public string? Link { get; init; }
        public string? JournalEntryNumber { get; init; }

        public static AssistantActionResult Failed(string message) => new() { Ok = false, Message = message };
    }

    /// <summary>Raised for a bad argument; the message goes back to the model to correct itself.</summary>
    public sealed class AssistantArgumentException : Exception
    {
        public AssistantArgumentException(string message) : base(message) { }
    }

    /// <summary>Reading the model's arguments safely.</summary>
    public static class Args
    {
        public static string? Str(JsonElement args, string name)
        {
            if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
                return null;
            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        public static string Required(JsonElement args, string name) =>
            Str(args, name) ?? throw new AssistantArgumentException($"'{name}' is required.");

        public static int? Int(JsonElement args, string name)
        {
            var text = Str(args, name);
            if (text == null) return null;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new AssistantArgumentException($"'{name}' must be a whole number.");
        }

        public static decimal? Dec(JsonElement args, string name)
        {
            var text = Str(args, name)?.Replace(",", string.Empty);
            if (text == null) return null;
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new AssistantArgumentException($"'{name}' must be a number.");
        }

        public static DateTime? Date(JsonElement args, string name)
        {
            var text = Str(args, name);
            if (text == null) return null;
            if (string.Equals(text, "today", StringComparison.OrdinalIgnoreCase)) return DateTime.Today;
            return DateTime.TryParseExact(text, new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
                ? value.Date
                : throw new AssistantArgumentException($"'{name}' must be a date as YYYY-MM-DD.");
        }

        public static bool Bool(JsonElement args, string name, bool fallback = false) =>
            Str(args, name)?.ToLowerInvariant() switch
            {
                "true" or "yes" or "1" => true,
                "false" or "no" or "0" => false,
                _ => fallback
            };

        public static int Take(JsonElement args, int fallback = 25, int max = 50) =>
            Math.Clamp(Int(args, "take") ?? fallback, 1, max);
    }

    /// <summary>Small helpers for writing JSON schemas.</summary>
    public static class Schema
    {
        public static JsonObject Object(params (string Name, JsonObject Schema, bool Required)[] properties)
        {
            var props = new JsonObject();
            var required = new JsonArray();
            foreach (var (name, schema, isRequired) in properties)
            {
                props[name] = schema;
                if (isRequired) required.Add(name);
            }

            var result = new JsonObject { ["type"] = "object", ["properties"] = props };
            if (required.Count > 0) result["required"] = required;
            return result;
        }

        public static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };
        public static JsonObject Num(string description) => new() { ["type"] = "number", ["description"] = description };
        public static JsonObject Int(string description) => new() { ["type"] = "integer", ["description"] = description };
        public static JsonObject Bool(string description) => new() { ["type"] = "boolean", ["description"] = description };
        public static JsonObject Date(string description) => Str(description + " (YYYY-MM-DD)");

        public static JsonObject Enum(string description, params string[] values)
        {
            var list = new JsonArray();
            foreach (var value in values) list.Add(value);
            return new JsonObject { ["type"] = "string", ["description"] = description, ["enum"] = list };
        }

        public static JsonObject Array(string description, JsonObject items) =>
            new() { ["type"] = "array", ["description"] = description, ["items"] = items };

        public static (string, JsonObject, bool) Take => ("take", Int("How many rows, at most 50"), false);
    }
}
