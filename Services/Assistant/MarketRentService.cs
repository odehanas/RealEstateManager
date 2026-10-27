using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace RealEstateManager.Services.Assistant
{
    /// <summary>
    /// Looks up published market rents on the web (OpenAI's web search, with the
    /// assistant's key) for a city or area and a kind of unit. Only the place and the
    /// kind of unit are sent out: never a tenant, a contract or the company's figures.
    /// Results are kept for a day, so the same question does not search twice.
    /// </summary>
    public class MarketRentService
    {
        private static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);

        private readonly AssistantOptions _options;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<MarketRentService> _logger;

        public MarketRentService(
            IOptions<AssistantOptions> options,
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            ILogger<MarketRentService> logger)
        {
            _options = options.Value;
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _logger = logger;
        }

        public sealed class MarketSource
        {
            public string? Title { get; set; }
            public string? Url { get; set; }
            public string? Published { get; set; }
        }

        public sealed class MarketRent
        {
            public bool Found { get; set; }
            public decimal? TypicalAnnualRent { get; set; }
            public decimal? LowAnnualRent { get; set; }
            public decimal? HighAnnualRent { get; set; }
            public decimal? PerSqftLow { get; set; }
            public decimal? PerSqftHigh { get; set; }
            public string? RentBasis { get; set; }
            public string? AsOf { get; set; }
            public string? Notes { get; set; }
            public List<MarketSource> Sources { get; set; } = new();

            /// <summary>When this was searched, so the reader knows how fresh it is.</summary>
            public DateTime SearchedAt { get; set; }
        }

        public sealed class MarketRentException : Exception
        {
            public MarketRentException(string message) : base(message) { }
        }

        public async Task<MarketRent> LookUpAsync(string city, string? area, string unitType)
        {
            var key = $"market-rent|{city}|{area}|{unitType}".ToLowerInvariant();
            if (_cache.TryGetValue(key, out MarketRent? cached) && cached != null)
                return cached;

            var result = await SearchAsync(city, area, unitType);
            _cache.Set(key, result, CacheFor);
            return result;
        }

        private async Task<MarketRent> SearchAsync(string city, string? area, string unitType)
        {
            var place = area == null ? $"{city}, United Arab Emirates" : $"{area}, {city}, United Arab Emirates";
            var prompt = $"""
                Today is {DateTime.Today:d MMMM yyyy}. Find the current market rent for: {unitType} in {place}.
                - Search the web. Prefer the last 12 months and these kinds of source: government or regulator data
                  (land departments, rent indices, Ejari), recognised market reports (CBRE, JLL, Cushman & Wakefield,
                  Knight Frank, Savills, Asteco, ValuStrat, Property Monitor) and portal market reports or listings
                  (Bayut, Property Finder, Dubizzle).
                - Give rents per YEAR in AED. Convert monthly figures to annual. Give AED per square foot per year when published.
                - typical_annual_rent is the usual middle figure; low and high the range. rent_basis: "contracted" when the
                  figures are signed contracts (e.g. Ejari or a rent index), "asking" when they are adverts, "mixed" or "unknown".
                - as_of: the month and year the figures describe. sources: every page you used, with its title, URL and
                  publication date when shown.
                - If the area has no published figures, use the nearest comparable area or the city and say so in notes.
                  If nothing reliable is found, set found to false. Never invent a figure or a source.
                """;

            var body = new JsonObject
            {
                ["model"] = _options.ResearchModel,
                ["tools"] = new JsonArray(new JsonObject
                {
                    ["type"] = "web_search",
                    ["user_location"] = new JsonObject { ["type"] = "approximate", ["country"] = "AE" }
                }),
                ["input"] = prompt,
                ["text"] = new JsonObject
                {
                    ["format"] = new JsonObject
                    {
                        ["type"] = "json_schema",
                        ["name"] = "market_rent",
                        ["strict"] = true,
                        ["schema"] = Schema()
                    }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenAIApiKey);

            HttpResponseMessage response;
            try
            {
                response = await _httpClientFactory.CreateClient("openai-documents").SendAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not reach OpenAI for a market rent search.");
                throw new MarketRentException("The market search could not be reached. Try again in a moment.");
            }

            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenAI refused a market rent search: {Status} {Body}", (int)response.StatusCode, text.Length > 500 ? text[..500] : text);
                throw new MarketRentException("The market search was refused. The research model name may need checking.");
            }

            var (output, citations) = Read(text);
            if (output == null)
                throw new MarketRentException("The market search returned nothing.");

            MarketRent result;
            try
            {
                result = JsonSerializer.Deserialize<MarketRent>(output, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                }) ?? new MarketRent();
            }
            catch (JsonException)
            {
                throw new MarketRentException("The market search's answer could not be understood. Try again.");
            }

            // The pages the search actually opened are the trustworthy list; the model's
            // own list only adds their dates.
            var byUrl = result.Sources.Where(x => x.Url != null).GroupBy(x => Trim(x.Url!)).ToDictionary(g => g.Key, g => g.First());
            var cited = citations
                .GroupBy(x => Trim(x.Url!))
                .Select(g => new MarketSource
                {
                    Url = g.First().Url,
                    Title = g.First().Title,
                    Published = byUrl.TryGetValue(g.Key, out var listed) ? listed.Published : null
                })
                .ToList();
            if (cited.Count > 0)
                result.Sources = cited;
            else
                result.Sources = result.Sources.Where(x => x.Url != null && x.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)).ToList();

            if (result.Sources.Count == 0)
                result.Found = false;
            result.SearchedAt = DateTime.Now;
            return result;
        }

        /// <summary>The answer text and the web pages it cites, from a Responses API reply.</summary>
        private static (string? Output, List<MarketSource> Citations) Read(string responseJson)
        {
            string? output = null;
            var citations = new List<MarketSource>();
            using var doc = JsonDocument.Parse(responseJson);
            if (!doc.RootElement.TryGetProperty("output", out var items) || items.ValueKind != JsonValueKind.Array)
                return (null, citations);

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var part in content.EnumerateArray())
                {
                    if (!part.TryGetProperty("type", out var type) || type.GetString() != "output_text")
                        continue;
                    output = part.GetProperty("text").GetString();
                    if (!part.TryGetProperty("annotations", out var annotations) || annotations.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var note in annotations.EnumerateArray())
                    {
                        if (note.TryGetProperty("type", out var noteType) && noteType.GetString() == "url_citation" &&
                            note.TryGetProperty("url", out var url) && url.GetString() is { } link)
                        {
                            citations.Add(new MarketSource
                            {
                                Url = link,
                                Title = note.TryGetProperty("title", out var title) ? title.GetString() : null
                            });
                        }
                    }
                }
            }

            return (output, citations);
        }

        private static string Trim(string url) =>
            url.Split('?', '#')[0].TrimEnd('/').ToLowerInvariant();

        private static JsonObject Schema()
        {
            static JsonObject Nullable(string type) => new() { ["type"] = new JsonArray(type, "null") };

            var source = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["title"] = Nullable("string"),
                    ["url"] = Nullable("string"),
                    ["published"] = Nullable("string")
                },
                ["required"] = new JsonArray("title", "url", "published")
            };

            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["found"] = new JsonObject { ["type"] = "boolean" },
                    ["typical_annual_rent"] = Nullable("number"),
                    ["low_annual_rent"] = Nullable("number"),
                    ["high_annual_rent"] = Nullable("number"),
                    ["per_sqft_low"] = Nullable("number"),
                    ["per_sqft_high"] = Nullable("number"),
                    ["rent_basis"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("contracted", "asking", "mixed", "unknown") },
                    ["as_of"] = Nullable("string"),
                    ["notes"] = Nullable("string"),
                    ["sources"] = new JsonObject { ["type"] = "array", ["items"] = source }
                },
                ["required"] = new JsonArray("found", "typical_annual_rent", "low_annual_rent", "high_annual_rent",
                    "per_sqft_low", "per_sqft_high", "rent_basis", "as_of", "notes", "sources")
            };
        }
    }
}
