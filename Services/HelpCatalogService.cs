using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The in-app help: which topic belongs to which screen, and the topic text.
    ///
    /// Everything comes from two places under wwwroot/help - catalog.json, which lists
    /// the topics and the screens each one covers, and topics/&lt;key&gt;.html, one HTML
    /// fragment per topic. The help panel on every screen, the Help Center, the full
    /// manual and the PDF are all built from those, so editing one topic file updates
    /// every place that topic appears and they can never disagree.
    ///
    /// Registered as a singleton: the files only change on deployment, which restarts
    /// the application, so they are read once and cached.
    /// </summary>
    public class HelpCatalogService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ConcurrentDictionary<string, string> _contentCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lazy<HelpCatalog> _catalog;

        public HelpCatalogService(IWebHostEnvironment environment)
        {
            _environment = environment;
            _catalog = new Lazy<HelpCatalog>(LoadCatalog);
        }

        public IReadOnlyList<HelpGroup> Groups => _catalog.Value.Groups;

        public IReadOnlyList<HelpTopic> AllTopics => _catalog.Value.AllTopics;

        public HelpTopic? GetTopic(string? key) =>
            string.IsNullOrWhiteSpace(key) ? null : _catalog.Value.ByKey.GetValueOrDefault(key);

        /// <summary>
        /// The topic for a screen. An entry naming the action ("Reports.CashFlow") wins
        /// over one naming the whole controller ("Reports"), so one controller can serve
        /// several topics.
        /// </summary>
        public HelpTopic? ForScreen(string? controller, string? action)
        {
            if (string.IsNullOrWhiteSpace(controller))
                return null;

            var screens = _catalog.Value.ByScreen;

            if (!string.IsNullOrWhiteSpace(action) &&
                screens.TryGetValue($"{controller}.{action}", out var exact))
            {
                return exact;
            }

            return screens.GetValueOrDefault(controller);
        }

        /// <summary>The topic before and after this one, in manual order.</summary>
        public (HelpTopic? Previous, HelpTopic? Next) Neighbours(string key)
        {
            var all = AllTopics;
            var index = all.ToList().FindIndex(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
                return (null, null);

            return (index > 0 ? all[index - 1] : null,
                    index < all.Count - 1 ? all[index + 1] : null);
        }

        /// <summary>
        /// A topic's HTML with its cross-references rewritten for where it is shown.
        ///
        /// Topic files link to each other as href="#key", which works as-is inside the
        /// single-page manual. Anywhere else the link has to become a real address.
        /// </summary>
        public string GetContent(string key, HelpLinkMode linkMode)
        {
            var topic = GetTopic(key);
            if (topic == null)
                return string.Empty;

            var html = _contentCache.GetOrAdd(topic.Key, ReadTopicFile);

            if (linkMode == HelpLinkMode.Anchor)
                return html;

            return TopicLinkPattern.Replace(html, match =>
            {
                var target = GetTopic(match.Groups[1].Value);

                // Not a topic - an in-page reference such as the workflow icons. Leave it.
                if (target == null)
                    return match.Value;

                return linkMode == HelpLinkMode.Plain
                    ? "href=\"#\""
                    : $"href=\"/Help/Topic/{target.Key}\" data-help-topic=\"{target.Key}\"";
            });
        }

        /// <summary>
        /// Topics matching every word of the query, in the title, the summary or the text,
        /// best matches first, each with a short excerpt around the first hit.
        /// </summary>
        public List<HelpSearchResult> Search(string? query)
        {
            var words = (query ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => x.Length > 1)
                .Select(x => x.ToLowerInvariant())
                .Distinct()
                .ToList();

            if (words.Count == 0)
                return new List<HelpSearchResult>();

            var results = new List<HelpSearchResult>();

            foreach (var topic in AllTopics)
            {
                var title = topic.Title.ToLowerInvariant();
                var summary = topic.Summary.ToLowerInvariant();
                var text = PlainText(_contentCache.GetOrAdd(topic.Key, ReadTopicFile));
                var lowerText = text.ToLowerInvariant();

                if (!words.All(w => title.Contains(w) || summary.Contains(w) || lowerText.Contains(w)))
                    continue;

                // A word in the title says far more about relevance than one in the body.
                var score = words.Sum(w =>
                    (title.Contains(w) ? 10 : 0) +
                    (summary.Contains(w) ? 4 : 0) +
                    Math.Min(5, CountOccurrences(lowerText, w)));

                results.Add(new HelpSearchResult
                {
                    Topic = topic,
                    Score = score,
                    Excerpt = Excerpt(text, lowerText, words)
                });
            }

            return results.OrderByDescending(x => x.Score).ThenBy(x => x.Topic.Order).ToList();
        }

        // =====================================================================
        //  LOADING
        // =====================================================================

        private HelpCatalog LoadCatalog()
        {
            var file = _environment.WebRootFileProvider.GetFileInfo("help/catalog.json");
            if (!file.Exists)
                return new HelpCatalog();

            using var stream = file.CreateReadStream();
            var raw = JsonSerializer.Deserialize<RawCatalog>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new RawCatalog();

            var catalog = new HelpCatalog();
            var order = 0;

            foreach (var rawGroup in raw.Groups)
            {
                var group = new HelpGroup { Key = rawGroup.Key, Title = rawGroup.Title };

                foreach (var rawTopic in rawGroup.Topics)
                {
                    var topic = new HelpTopic
                    {
                        Key = rawTopic.Key,
                        Title = rawTopic.Title,
                        Path = rawTopic.Path ?? string.Empty,
                        Summary = rawTopic.Summary ?? string.Empty,
                        GroupKey = group.Key,
                        GroupTitle = group.Title,
                        Order = ++order
                    };

                    group.Topics.Add(topic);
                    catalog.AllTopics.Add(topic);
                    catalog.ByKey[topic.Key] = topic;

                    foreach (var screen in rawTopic.Screens ?? new List<string>())
                        catalog.ByScreen[screen] = topic;
                }

                catalog.Groups.Add(group);
            }

            return catalog;
        }

        /// <summary>
        /// Reads a topic file. Only ever called with a key that exists in the catalog,
        /// so a request can never be steered to an arbitrary file on disk.
        /// </summary>
        private string ReadTopicFile(string key)
        {
            var file = _environment.WebRootFileProvider.GetFileInfo($"help/topics/{key}.html");
            if (!file.Exists)
                return $"<p class=\"text-muted\">This help topic has not been written yet.</p>";

            using var reader = new StreamReader(file.CreateReadStream());
            return reader.ReadToEnd();
        }

        // =====================================================================
        //  TEXT HELPERS
        // =====================================================================

        private static readonly Regex TopicLinkPattern =
            new("href=\"#([a-z0-9-]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TagPattern = new("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex SpacePattern = new(@"\s+", RegexOptions.Compiled);

        private static string PlainText(string html)
        {
            // The workflow topic carries an SVG icon sheet; none of it is searchable text.
            var withoutSvg = Regex.Replace(html, "<svg.*?</svg>", " ", RegexOptions.Singleline);
            var text = TagPattern.Replace(withoutSvg, " ");
            return SpacePattern.Replace(WebUtility.HtmlDecode(text), " ").Trim();
        }

        private static int CountOccurrences(string text, string word)
        {
            var count = 0;
            var index = 0;

            while ((index = text.IndexOf(word, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += word.Length;
            }

            return count;
        }

        private static string Excerpt(string text, string lowerText, List<string> words)
        {
            var first = words
                .Select(w => lowerText.IndexOf(w, StringComparison.Ordinal))
                .Where(i => i >= 0)
                .DefaultIfEmpty(0)
                .Min();

            var start = Math.Max(0, first - 70);
            var length = Math.Min(220, text.Length - start);
            var excerpt = text.Substring(start, length).Trim();

            return (start > 0 ? "… " : string.Empty) + excerpt + (start + length < text.Length ? " …" : string.Empty);
        }

        // =====================================================================
        //  SHAPES
        // =====================================================================

        private sealed class HelpCatalog
        {
            public List<HelpGroup> Groups { get; } = new();
            public List<HelpTopic> AllTopics { get; } = new();
            public Dictionary<string, HelpTopic> ByKey { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, HelpTopic> ByScreen { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class RawCatalog
        {
            public List<RawGroup> Groups { get; set; } = new();
        }

        private sealed class RawGroup
        {
            public string Key { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public List<RawTopic> Topics { get; set; } = new();
        }

        private sealed class RawTopic
        {
            public string Key { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string? Path { get; set; }
            public string? Summary { get; set; }
            public List<string>? Screens { get; set; }
        }
    }

    /// <summary>How a topic's cross-references are written out.</summary>
    public enum HelpLinkMode
    {
        /// <summary>Links to /Help/Topic/key - the Help Center and the help panel.</summary>
        Page,

        /// <summary>Links stay as #key - the single-page manual, where every topic is an anchor.</summary>
        Anchor,

        /// <summary>Links disabled - the tenant portal, which has no access to staff help.</summary>
        Plain
    }

    public class HelpGroup
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public List<HelpTopic> Topics { get; } = new();
    }

    public class HelpTopic
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;

        /// <summary>Where the screen is in the menus, e.g. "Operations ▸ Vendor Bills".</summary>
        public string Path { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;
        public string GroupKey { get; set; } = string.Empty;
        public string GroupTitle { get; set; } = string.Empty;

        /// <summary>Position in the manual, 1-based.</summary>
        public int Order { get; set; }
    }

    public class HelpSearchResult
    {
        public HelpTopic Topic { get; set; } = new();
        public int Score { get; set; }
        public string Excerpt { get; set; } = string.Empty;
    }
}
