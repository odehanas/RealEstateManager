using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace RealEstateManager.Services.Assistant
{
    /// <summary>
    /// Reads an Emirates ID or a passport (scan, photo or PDF) with OpenAI's vision model,
    /// the same key as the assistant, so the number and expiry date need not be typed.
    /// The result is only a suggestion: the user sees what was filled in.
    /// </summary>
    public class IdDocumentScanService
    {
        private readonly AssistantOptions _options;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<IdDocumentScanService> _logger;

        public IdDocumentScanService(IOptions<AssistantOptions> options, IHttpClientFactory httpClientFactory, ILogger<IdDocumentScanService> logger)
        {
            _options = options.Value;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public bool IsAvailable => _options.IsUsable;

        public sealed class IdDocument
        {
            public string? DocumentKind { get; set; }
            public string? IdNumber { get; set; }
            public string? PassportNumber { get; set; }
            public string? NameEnglish { get; set; }
            public string? Nationality { get; set; }
            public string? DateOfBirth { get; set; }
            public string? IssueDate { get; set; }
            public string? ExpiryDate { get; set; }

            public DateTime? Expiry => Date(ExpiryDate);
            public DateTime? Issued => Date(IssueDate);

            /// <summary>An Emirates ID number as 784-YYYY-NNNNNNN-N, when it reads as one.</summary>
            public string? FormattedIdNumber
            {
                get
                {
                    var digits = new string((IdNumber ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
                    return digits.Length == 15 ? $"{digits[..3]}-{digits[3..7]}-{digits[7..14]}-{digits[14]}" : IdNumber?.Trim();
                }
            }

            private static DateTime? Date(string? text) =>
                DateTime.TryParseExact(text?.Trim(), new[] { "yyyy-MM-dd", "dd/MM/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) &&
                d.Year > 1900 && d.Year < 2100 ? d.Date : null;
        }

        /// <summary>Reads the document, or returns null with the reason when it cannot.</summary>
        public async Task<(IdDocument? Document, string? Error)> ReadAsync(byte[] data, string mediaType, string fileName)
        {
            if (!IsAvailable)
                return (null, "Reading documents automatically needs the AI assistant's OpenAI key, which is not set up.");

            var dataUrl = $"data:{mediaType};base64,{Convert.ToBase64String(data)}";
            JsonNode document = mediaType == "application/pdf"
                ? new JsonObject { ["type"] = "input_file", ["filename"] = fileName, ["file_data"] = dataUrl }
                : new JsonObject { ["type"] = "input_image", ["image_url"] = dataUrl, ["detail"] = "high" };

            static JsonObject Nullable() => new() { ["type"] = new JsonArray("string", "null") };
            var schema = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["document_kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("emirates_id", "passport", "other") },
                    ["id_number"] = Nullable(),
                    ["passport_number"] = Nullable(),
                    ["name_english"] = Nullable(),
                    ["nationality"] = Nullable(),
                    ["date_of_birth"] = Nullable(),
                    ["issue_date"] = Nullable(),
                    ["expiry_date"] = Nullable()
                },
                ["required"] = new JsonArray("document_kind", "id_number", "passport_number", "name_english", "nationality",
                    "date_of_birth", "issue_date", "expiry_date")
            };

            var body = new JsonObject
            {
                ["model"] = _options.DocumentModel,
                ["input"] = new JsonArray(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray(
                        new JsonObject
                        {
                            ["type"] = "input_text",
                            ["text"] = "This is a UAE Emirates ID card (front and/or back) or a passport. Read it. id_number is the 15-digit " +
                                       "Emirates ID number (784-...), passport_number the passport number. Dates as YYYY-MM-DD. nationality in English. " +
                                       "Use null for anything not on the document or not readable; never guess."
                        },
                        document)
                }),
                ["text"] = new JsonObject
                {
                    ["format"] = new JsonObject { ["type"] = "json_schema", ["name"] = "id_document", ["strict"] = true, ["schema"] = schema }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenAIApiKey);

            try
            {
                using var response = await _httpClientFactory.CreateClient("openai-documents").SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("OpenAI refused to read an ID document: {Status} {Body}", (int)response.StatusCode, text.Length > 500 ? text[..500] : text);
                    return (null, "The document could not be read automatically; enter the number and expiry date by hand.");
                }

                using var doc = JsonDocument.Parse(text);
                var output = doc.RootElement.GetProperty("output").EnumerateArray()
                    .Where(x => x.TryGetProperty("content", out _))
                    .SelectMany(x => x.GetProperty("content").EnumerateArray())
                    .Where(x => x.TryGetProperty("type", out var t) && t.GetString() == "output_text")
                    .Select(x => x.GetProperty("text").GetString())
                    .FirstOrDefault();
                if (output == null)
                    return (null, "The document could not be read automatically.");

                var result = JsonSerializer.Deserialize<IdDocument>(output, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });
                return (result, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reading an ID document failed.");
                return (null, "The document could not be read automatically; enter the number and expiry date by hand.");
            }
        }
    }
}
