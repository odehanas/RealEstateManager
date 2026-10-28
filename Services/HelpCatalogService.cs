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
    /// Help is CONTENT, not code. Every file is cached in memory but re-read the moment
    /// its last-modified time changes, so editing a topic, adding a screenshot or
    /// re-ordering the catalog takes effect as soon as the file reaches the server - no
    /// rebuild and no restart. (An earlier version read the files once at start-up, which
    /// meant a text correction stayed invisible until the next full deployment.)
    /// </summary>
    public class HelpCatalogService
    {
        private const string CatalogPath = "help/catalog.json";
        private const string ShotsPath = "help/images/shots.json";
        private const string VideosPath = "help/videos/videos.json";

        private readonly IWebHostEnvironment _environment;
        private readonly Localization.UiText _text;
        private readonly ConcurrentDictionary<string, (DateTimeOffset TopicStamp, DateTimeOffset ShotsStamp, DateTimeOffset VideosStamp, string Html)> _contentCache =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly object _catalogLock = new();
        private HelpCatalog _catalog = new();
        private DateTimeOffset _catalogStamp = DateTimeOffset.MinValue;

        private readonly object _shotsLock = new();
        private Dictionary<string, HelpShot> _shots = new(StringComparer.OrdinalIgnoreCase);
        private DateTimeOffset _shotsStamp = DateTimeOffset.MinValue;

        private readonly object _videosLock = new();
        private Dictionary<string, HelpVideo> _videos = new(StringComparer.OrdinalIgnoreCase);
        private DateTimeOffset _videosStamp = DateTimeOffset.MinValue;

        public HelpCatalogService(IWebHostEnvironment environment, Localization.UiText text)
        {
            _environment = environment;
            _text = text;
        }

        /// <summary>
        /// Screenshot captions, call-outs and video titles come from the capture tools in
        /// English; in Arabic they are looked up in the dictionary like the screens' text.
        /// </summary>
        private string Tr(string? english) =>
            string.IsNullOrEmpty(english) || !Localization.UiLanguages.IsArabic ? english ?? string.Empty : _text[english];

        public IReadOnlyList<HelpGroup> Groups => Catalog.Groups;

        public IReadOnlyList<HelpTopic> AllTopics => Catalog.AllTopics;

        public HelpTopic? GetTopic(string? key) =>
            string.IsNullOrWhiteSpace(key) ? null : Catalog.ByKey.GetValueOrDefault(key);

        /// <summary>The catalog, reloaded whenever catalog.json changes on disk.</summary>
        private HelpCatalog Catalog
        {
            get
            {
                var stamp = _environment.WebRootFileProvider.GetFileInfo(CatalogPath).LastModified
                            + (_environment.WebRootFileProvider.GetFileInfo("help/catalog.ar.json").LastModified - DateTimeOffset.MinValue);
                if (stamp == _catalogStamp)
                    return _catalog;

                lock (_catalogLock)
                {
                    if (stamp != _catalogStamp)
                    {
                        _catalog = LoadCatalog();
                        _catalogStamp = stamp;
                    }

                    return _catalog;
                }
            }
        }

        /// <summary>
        /// The topic for a screen. An entry naming the action ("Reports.CashFlow") wins
        /// over one naming the whole controller ("Reports"), so one controller can serve
        /// several topics.
        /// </summary>
        public HelpTopic? ForScreen(string? controller, string? action)
        {
            if (string.IsNullOrWhiteSpace(controller))
                return null;

            var screens = Catalog.ByScreen;

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

            var html = RawTopicHtml(topic.Key);

            if (linkMode == HelpLinkMode.Anchor)
            {
                // The single-page manual is what the PDF is printed from. Chrome copies a
                // JPEG into a PDF unchanged but expands a WebP about fivefold, so the
                // manual uses each screenshot's JPEG twin.
                return ManualImagePattern.Replace(html, "$1.jpg?");
            }

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

        /// <summary>A topic's text without markup, for the AI assistant to read.</summary>
        public string GetPlainText(string key)
        {
            var topic = GetTopic(key);
            return topic == null ? string.Empty : PlainText(RawTopicHtml(topic.Key));
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
                var text = PlainText(RawTopicHtml(topic.Key));
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

            ApplyArabic(catalog);
            return catalog;
        }

        /// <summary>
        /// Arabic titles, menu paths and summaries from help/catalog.ar.json:
        /// { "groups": { key: title }, "topics": { key: { "title", "path", "summary" } } }.
        /// A topic without them keeps its English.
        /// </summary>
        private void ApplyArabic(HelpCatalog catalog)
        {
            var file = _environment.WebRootFileProvider.GetFileInfo("help/catalog.ar.json");
            if (!file.Exists)
                return;

            using var stream = file.CreateReadStream();
            var raw = JsonSerializer.Deserialize<RawArabicCatalog>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (raw == null)
                return;

            foreach (var group in catalog.Groups)
            {
                if (raw.Groups.TryGetValue(group.Key, out var title))
                    group.TitleAr = title;
                foreach (var topic in group.Topics)
                {
                    topic.GroupTitleAr = group.TitleAr;
                    if (!raw.Topics.TryGetValue(topic.Key, out var ar))
                        continue;
                    topic.TitleAr = ar.Title;
                    topic.PathAr = ar.Path;
                    topic.SummaryAr = ar.Summary;
                }
            }
        }

        private sealed class RawArabicCatalog
        {
            public Dictionary<string, string> Groups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, RawArabicTopic> Topics { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class RawArabicTopic
        {
            public string? Title { get; set; }
            public string? Path { get; set; }
            public string? Summary { get; set; }
        }

        /// <summary>
        /// A topic's HTML with its screenshots in place. Cached, but re-read whenever the
        /// topic file or the screenshot list changes. Only ever called with a key that
        /// exists in the catalog, so a request can never be steered to an arbitrary file.
        /// </summary>
        private string RawTopicHtml(string key)
        {
            // In Arabic, help/topics/ar/<key>.html when it has been translated.
            var arabic = Localization.UiLanguages.IsArabic
                ? _environment.WebRootFileProvider.GetFileInfo($"help/topics/ar/{key}.html")
                : null;
            var file = arabic is { Exists: true } ? arabic : _environment.WebRootFileProvider.GetFileInfo($"help/topics/{key}.html");
            // Keyed by language, because the screenshots' and videos' words follow it too.
            var cacheKey = Localization.UiLanguages.IsArabic ? "ar:" + key : key;
            if (!file.Exists)
                return "<p class=\"text-muted\">This help topic has not been written yet.</p>";

            // Both files are tracked separately: a re-captured screenshot must refresh every
            // topic that shows it even though the topic text did not change, and an edited
            // topic must refresh even if its file time is older than the screenshot list's.
            var shots = Shots;
            var shotsStamp = _shotsStamp;
            var videos = Videos;
            var videosStamp = _videosStamp;

            if (_contentCache.TryGetValue(cacheKey, out var cached)
                && cached.TopicStamp == file.LastModified
                && cached.ShotsStamp == shotsStamp
                && cached.VideosStamp == videosStamp)
            {
                return cached.Html;
            }

            string html;
            using (var reader = new StreamReader(file.CreateReadStream()))
                html = reader.ReadToEnd();

            html = ShotPattern.Replace(html, match => RenderShot(match.Groups[1].Value, shots));
            html = VideoPattern.Replace(html, match => RenderVideo(match.Groups[1].Value, videos));

            _contentCache[cacheKey] = (file.LastModified, shotsStamp, videosStamp, html);
            return html;
        }

        // =====================================================================
        //  SCREENSHOTS
        // =====================================================================

        /// <summary>
        /// A topic places a screenshot with one tag: &lt;figure data-shot="vendor-bills-edit"&gt;&lt;/figure&gt;.
        /// The picture, its size, caption and numbered call-outs all come from
        /// images/shots.json, which the capture tool writes - so re-capturing the screens
        /// after a design change refreshes every topic without touching its text.
        /// </summary>
        private static readonly Regex ShotPattern =
            new("<figure\\s+data-shot=\"([a-z0-9-]+)\"\\s*>\\s*</figure>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private Dictionary<string, HelpShot> Shots
        {
            get
            {
                var file = _environment.WebRootFileProvider.GetFileInfo(ShotsPath);
                var stamp = file.Exists ? file.LastModified : DateTimeOffset.MinValue;

                if (stamp == _shotsStamp)
                    return _shots;

                lock (_shotsLock)
                {
                    if (stamp == _shotsStamp)
                        return _shots;

                    var loaded = new Dictionary<string, HelpShot>(StringComparer.OrdinalIgnoreCase);

                    if (file.Exists)
                    {
                        using var stream = file.CreateReadStream();
                        var list = JsonSerializer.Deserialize<List<HelpShot>>(stream, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        }) ?? new List<HelpShot>();

                        foreach (var shot in list.Where(x => !string.IsNullOrWhiteSpace(x.Key)))
                            loaded[shot.Key] = shot;
                    }

                    _shots = loaded;
                    _shotsStamp = stamp;
                    return _shots;
                }
            }
        }

        /// <summary>
        /// Builds the framed screenshot. Desktop screens get a browser window, portal
        /// screens a phone. Call-outs are positioned in percentages of the picture, so
        /// they stay on target at any width - in the narrow help panel, on the full
        /// topic page and in the printed manual alike.
        /// </summary>
        private string RenderShot(string key, Dictionary<string, HelpShot> shots)
        {
            if (!shots.TryGetValue(key, out var shot))
                return string.Empty; // Not captured yet: show nothing rather than a broken image.

            var enc = (Func<string?, string>)(value => WebUtility.HtmlEncode(value ?? string.Empty));

            // The capture time is appended so a re-captured image is never served from a
            // browser's cache under the old name.
            var src = $"/help/images/{enc(shot.File)}?v={shot.Version}";
            var frame = shot.Frame == "phone" ? "shot-phone" : "shot-browser";

            var markers = string.Concat(shot.Markers.Select(m =>
                $"<span class=\"shot-marker\" style=\"left:{m.X.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}%;" +
                $"top:{m.Y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}%\" " +
                $"aria-hidden=\"true\">{m.Number}</span>"));

            // The address bar reads like a menu path; each part is a menu name.
            var addressText = string.Join(" ▸ ", (shot.Address ?? string.Empty).Split(" ▸ ").Select(Tr));

            var legend = shot.Markers.Count == 0
                ? string.Empty
                : "<ol class=\"shot-legend\">" +
                  string.Concat(shot.Markers.Select(m =>
                      $"<li value=\"{m.Number}\"><span class=\"shot-legend-no\">{m.Number}</span>{enc(Tr(m.Label))}</li>")) +
                  "</ol>";

            var address = shot.Frame == "phone"
                ? string.Empty
                : $"<div class=\"shot-bar\"><span class=\"shot-dots\"><i></i><i></i><i></i></span>" +
                  $"<span class=\"shot-address\">{enc(addressText)}</span></div>";

            return
                $"<figure class=\"shot {frame}\">" +
                $"<div class=\"shot-window\">{address}" +
                $"<div class=\"shot-canvas\">" +
                $"<img src=\"{src}\" width=\"{shot.Width}\" height=\"{shot.Height}\" loading=\"lazy\" " +
                $"alt=\"{enc(Tr(shot.Caption))}\" data-shot-zoom=\"{src}\" />{markers}</div></div>" +
                $"<figcaption>{enc(Tr(shot.Caption))}</figcaption>{legend}</figure>";
        }

        // =====================================================================
        //  VIDEOS
        // =====================================================================

        /// <summary>
        /// A topic places a training video with one tag: &lt;figure data-video="lease"&gt;&lt;/figure&gt;.
        /// The title, file, cover picture and length come from videos/videos.json, which
        /// the recording tool (Tools/VideoCapture) writes - so re-recording the videos after
        /// a screen changes refreshes every topic without touching its text.
        /// </summary>
        private static readonly Regex VideoPattern =
            new("<figure\\s+data-video=\"([a-z0-9-]+)\"\\s*>\\s*</figure>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Every recorded video, in the order they are meant to be watched.</summary>
        public IReadOnlyList<HelpVideo> AllVideos => Videos.Values.OrderBy(x => x.Number).ToList();

        private Dictionary<string, HelpVideo> Videos
        {
            get
            {
                var file = _environment.WebRootFileProvider.GetFileInfo(VideosPath);
                var stamp = file.Exists ? file.LastModified : DateTimeOffset.MinValue;

                if (stamp == _videosStamp)
                    return _videos;

                lock (_videosLock)
                {
                    if (stamp == _videosStamp)
                        return _videos;

                    var loaded = new Dictionary<string, HelpVideo>(StringComparer.OrdinalIgnoreCase);

                    if (file.Exists)
                    {
                        using var stream = file.CreateReadStream();
                        var list = JsonSerializer.Deserialize<List<HelpVideo>>(stream, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        }) ?? new List<HelpVideo>();

                        // A video that is listed but not on the server - not uploaded yet -
                        // is left out, so a topic never shows a player that cannot play.
                        foreach (var video in list.Where(x => !string.IsNullOrWhiteSpace(x.Key)))
                        {
                            if (_environment.WebRootFileProvider.GetFileInfo($"help/videos/{video.File}").Exists)
                                loaded[video.Key] = video;
                        }
                    }

                    _videos = loaded;
                    _videosStamp = stamp;
                    return _videos;
                }
            }
        }

        /// <summary>
        /// A player showing the video's cover picture. Nothing is downloaded until someone
        /// presses play. On paper - the printed manual and the PDF - a video cannot play,
        /// so a short note stands in for it there (see help.css).
        /// </summary>
        private string RenderVideo(string key, Dictionary<string, HelpVideo> videos)
        {
            if (!videos.TryGetValue(key, out var video))
                return string.Empty;

            var enc = (Func<string?, string>)(value => WebUtility.HtmlEncode(value ?? string.Empty));
            var src = $"/help/videos/{enc(video.File)}?v={video.Version}";
            var poster = string.IsNullOrWhiteSpace(video.Poster)
                ? string.Empty
                : $" poster=\"/help/videos/{enc(video.Poster)}?v={video.Version}\"";
            var length = $"{video.Seconds / 60}:{video.Seconds % 60:00}";
            var title = Tr(video.Title);

            return
                $"<figure class=\"help-video\">" +
                $"<video controls preload=\"none\" playsinline{poster} src=\"{src}\" " +
                $"aria-label=\"{enc(Tr("Video:"))} {enc(title)}\"></video>" +
                $"<figcaption><span class=\"help-video-badge\">{enc(Tr("Video"))}</span>{enc(title)} " +
                $"<span class=\"help-video-length\">{length}</span></figcaption>" +
                $"<p class=\"help-video-print\">{enc(Tr("Video:"))} <strong>{enc(title)}</strong> ({length}) - " +
                $"{enc(Tr("watch it in the Help on screen."))}</p>" +
                $"</figure>";
        }

        // =====================================================================
        //  TEXT HELPERS
        // =====================================================================

        private static readonly Regex TopicLinkPattern =
            new("href=\"#([a-z0-9-]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TagPattern = new("<[^>]+>", RegexOptions.Compiled);

        private static readonly Regex ManualImagePattern =
            new(@"(/help/images/[a-z0-9-]+)\.webp\?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
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
        private string _title = string.Empty;
        public string Key { get; set; } = string.Empty;

        /// <summary>In the reader's language: the Arabic title when there is one.</summary>
        public string Title { get => Localization.UiLanguages.IsArabic && TitleAr != null ? TitleAr : _title; set => _title = value; }
        public string? TitleAr { get; set; }
        public List<HelpTopic> Topics { get; } = new();
    }

    public class HelpTopic
    {
        private string _title = string.Empty, _path = string.Empty, _summary = string.Empty, _groupTitle = string.Empty;
        private static bool Ar => Localization.UiLanguages.IsArabic;

        public string Key { get; set; } = string.Empty;
        public string Title { get => Ar && TitleAr != null ? TitleAr : _title; set => _title = value; }

        /// <summary>Where the screen is in the menus, e.g. "Operations ▸ Vendor Bills".</summary>
        public string Path { get => Ar && PathAr != null ? PathAr : _path; set => _path = value; }

        public string Summary { get => Ar && SummaryAr != null ? SummaryAr : _summary; set => _summary = value; }
        public string GroupKey { get; set; } = string.Empty;
        public string GroupTitle { get => Ar && GroupTitleAr != null ? GroupTitleAr : _groupTitle; set => _groupTitle = value; }

        public string? TitleAr { get; set; }
        public string? PathAr { get; set; }
        public string? SummaryAr { get; set; }
        public string? GroupTitleAr { get; set; }

        /// <summary>Position in the manual, 1-based.</summary>
        public int Order { get; set; }
    }

    /// <summary>One screenshot, as described in wwwroot/help/images/shots.json.</summary>
    public class HelpShot
    {
        public string Key { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }

        /// <summary>"browser" for staff screens, "phone" for the tenant portal.</summary>
        public string Frame { get; set; } = "browser";

        /// <summary>Shown in the browser frame's address bar, e.g. "Operations ▸ Vendor Bills".</summary>
        public string Address { get; set; } = string.Empty;

        public string Caption { get; set; } = string.Empty;

        /// <summary>Capture time in ticks, used to bust browser caches.</summary>
        public long Version { get; set; }

        public List<HelpShotMarker> Markers { get; set; } = new();
    }

    /// <summary>One training video, as listed in wwwroot/help/videos/videos.json.</summary>
    public class HelpVideo
    {
        public string Key { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string? Poster { get; set; }
        public int Seconds { get; set; }

        /// <summary>When it was recorded; added to the address so browsers fetch a new recording.</summary>
        public long Version { get; set; }
    }

    public class HelpShotMarker
    {
        public int Number { get; set; }
        public string Label { get; set; } = string.Empty;

        /// <summary>Centre of the call-out as a percentage of the picture's width and height.</summary>
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class HelpSearchResult
    {
        public HelpTopic Topic { get; set; } = new();
        public int Score { get; set; }
        public string Excerpt { get; set; } = string.Empty;
    }
}
