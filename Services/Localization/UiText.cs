using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Html;

namespace RealEstateManager.Services.Localization
{
    /// <summary>
    /// The screen languages. English is the source language: every screen is written in
    /// English, and that English text is the key into the Arabic dictionary
    /// (Resources/Translations/ar.json). A phrase with no Arabic yet shows in English.
    /// </summary>
    public static class UiLanguages
    {
        public const string English = "en";
        public const string Arabic = "ar";
        public const string CookieName = ".REM.Lang";

        public static string Clean(string? language) => language == Arabic ? Arabic : English;

        /// <summary>The language of the current request (set by UiLanguageMiddleware).</summary>
        public static bool IsArabic => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Arabic;

        public static string Current => IsArabic ? Arabic : English;
    }

    /// <summary>
    /// Translates screen text. In a view: <c>@T["Save"]</c> for plain text, <c>@T.Html("Use &lt;strong&gt;Advance&lt;/strong&gt; ...")</c>
    /// for a sentence with inline markup, <c>T.Format("Cheque {0} is deposited.", number)</c> for one with values.
    /// </summary>
    public class UiText
    {
        private readonly IReadOnlyDictionary<string, string> _arabic;
        private readonly ConcurrentDictionary<string, byte> _missing = new();

        public UiText(IWebHostEnvironment environment)
        {
            var path = Path.Combine(environment.ContentRootPath, "Resources", "Translations", "ar.json");
            _arabic = File.Exists(path)
                ? Normalise(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new())
                : new Dictionary<string, string>();
            _templates = new(BuildTemplates);
        }

        public string this[string english] => Translate(english);

        /// <summary>The Arabic of a text whatever the current language, or the English when there is none.</summary>
        public string ArabicFor(string english) =>
            !string.IsNullOrEmpty(english) && _arabic.TryGetValue(Key(english), out var arabic) && !string.IsNullOrWhiteSpace(arabic)
                ? arabic
                : english;

        public IHtmlContent Html(string englishHtml) => new HtmlString(Translate(englishHtml));

        public string Format(string englishTemplate, params object?[] values) => SafeFormat(englishTemplate, values);

        /// <summary>A sentence with inline markup and values; the values are HTML-encoded.</summary>
        public IHtmlContent HtmlFormat(string englishHtmlTemplate, params object?[] values) =>
            new HtmlString(SafeFormat(englishHtmlTemplate, values.Select(v => v is IHtmlContent ? (object?)Render(v) : System.Net.WebUtility.HtmlEncode(Convert.ToString(v, CultureInfo.CurrentCulture))).ToArray()));

        /// <summary>
        /// A message built in code ("Cheque 104207 is deposited..."): translated whole when the
        /// dictionary has it, otherwise matched against the dictionary's templates
        /// ("Cheque {0} is deposited...") and its values carried into the Arabic.
        /// </summary>
        public string Message(string? english)
        {
            if (string.IsNullOrWhiteSpace(english) || !UiLanguages.IsArabic)
                return english ?? string.Empty;

            // Already Arabic: a note kept in TempData is drawn on more than one page.
            if (english.Any(c => c >= '؀' && c <= 'ۿ'))
                return english;

            var key = Key(english);
            if (_arabic.TryGetValue(key, out var exact) && !string.IsNullOrWhiteSpace(exact))
                return exact;

            // A message can be several sentences joined; translate each when the whole is unknown.
            foreach (var (pattern, arabic) in _templates.Value)
            {
                var match = pattern.Match(key);
                if (!match.Success)
                    continue;
                var values = match.Groups.Cast<System.Text.RegularExpressions.Group>().Skip(1)
                    .Select(g => (object?)(_arabic.TryGetValue(g.Value, out var word) ? word : g.Value)).ToArray();
                try { return string.Format(CultureInfo.CurrentCulture, arabic, values); }
                catch (FormatException) { break; }
            }

            var sentences = System.Text.RegularExpressions.Regex.Split(key, @"(?<=[.!?])\s+(?=[A-Z])");
            if (sentences.Length > 1)
            {
                var parts = sentences.Select(Message).ToList();
                if (parts.Where((p, i) => p != sentences[i]).Any())
                    return string.Join(' ', parts);
            }

            _missing.TryAdd(english, 0);
            return english;
        }

        private readonly Lazy<List<(System.Text.RegularExpressions.Regex Pattern, string Arabic)>> _templates;

        private List<(System.Text.RegularExpressions.Regex, string)> BuildTemplates() =>
            _arabic.Where(x => x.Key.Contains("{0}"))
                .OrderByDescending(x => System.Text.RegularExpressions.Regex.Replace(x.Key, @"\{\d+\}", string.Empty).Length)
                .Select(x => (new System.Text.RegularExpressions.Regex(
                    "^" + System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Escape(x.Key), @"\\\{\d+}", "(.+?)") + "$",
                    System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.CultureInvariant), x.Value))
                .ToList();

        private string SafeFormat(string template, object?[] values)
        {
            var translated = Translate(template);
            try { return string.Format(CultureInfo.CurrentCulture, translated, values); }
            catch (FormatException) { return string.Format(CultureInfo.CurrentCulture, template, values); }
        }

        private static string Render(object? content)
        {
            using var writer = new StringWriter();
            ((IHtmlContent)content!).WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
            return writer.ToString();
        }

        /// <summary>Phrases asked for in Arabic that have no translation yet, for the translator.</summary>
        public IReadOnlyCollection<string> Missing => _missing.Keys.OrderBy(x => x).ToList();

        public int Count => _arabic.Count;

        private string Translate(string english)
        {
            if (string.IsNullOrEmpty(english) || !UiLanguages.IsArabic)
                return english;

            if (_arabic.TryGetValue(Key(english), out var arabic) && !string.IsNullOrWhiteSpace(arabic))
                return arabic;

            _missing.TryAdd(english, 0);
            return english;
        }

        // Keys are matched on their words, so a change of indentation in a view never loses a translation.
        private static string Key(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        private static Dictionary<string, string> Normalise(Dictionary<string, string> source)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (english, arabic) in source)
                result[Key(english)] = arabic;
            return result;
        }
    }

    /// <summary>
    /// Sets the screen language of each request from the language cookie. Only the UI
    /// culture changes: numbers, dates and amounts keep the application's own format in
    /// both languages, so forms, model binding and reports behave the same.
    /// </summary>
    public class UiLanguageMiddleware
    {
        private static readonly CultureInfo ArabicUi = CultureInfo.GetCultureInfo("ar-AE");
        private static readonly CultureInfo EnglishUi = CultureInfo.GetCultureInfo("en-GB");
        private readonly RequestDelegate _next;

        public UiLanguageMiddleware(RequestDelegate next) => _next = next;

        public Task InvokeAsync(HttpContext context)
        {
            // ?lang=ar shows one page in that language without changing the reader's choice
            // (the manual PDFs are printed this way).
            var language = UiLanguages.Clean(context.Request.Query.TryGetValue("lang", out var asked)
                ? asked.ToString()
                : context.Request.Cookies[UiLanguages.CookieName]);
            CultureInfo.CurrentUICulture = language == UiLanguages.Arabic ? ArabicUi : EnglishUi;
            return _next(context);
        }

        /// <summary>Remembers the language in this browser for a year.</summary>
        public static void Remember(HttpResponse response, string language) =>
            response.Cookies.Append(UiLanguages.CookieName, UiLanguages.Clean(language), new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = response.HttpContext.Request.IsHttps
            });
    }
}

namespace RealEstateManager.Services.Localization
{
    using System.Text.RegularExpressions;
    using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

    /// <summary>
    /// Translates the labels written by <c>&lt;label asp-for="..."&gt;</c>: the property's
    /// [Display] name, or its name split into words ("ReceivedDate" is "Received Date").
    /// In English the labels are exactly as before.
    /// </summary>
    public class LocalizedDisplayMetadataProvider : IDisplayMetadataProvider
    {
        private static readonly Regex WordBreak = new("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);
        private readonly UiText _text;

        public LocalizedDisplayMetadataProvider(UiText text) => _text = text;

        public void CreateDisplayMetadata(DisplayMetadataProviderContext context)
        {
            if (context.Key.MetadataKind != ModelMetadataKind.Property || context.Key.Name == null)
                return;

            var declared = context.DisplayMetadata.DisplayName;
            var name = context.Key.Name;
            context.DisplayMetadata.DisplayName = () =>
            {
                var english = declared?.Invoke();
                if (!UiLanguages.IsArabic)
                    return english;
                return _text[english ?? WordBreak.Replace(name, " ")];
            };
        }
    }
}

namespace RealEstateManager.Services.Localization
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Filters;
    using Microsoft.AspNetCore.Mvc.ViewFeatures;

    /// <summary>
    /// Messages written in code - validation errors and the notes passed to the next page
    /// in TempData - are translated just before an Arabic page is drawn, so no controller or
    /// service has to know the user's language.
    /// </summary>
    public class UiMessageTranslationFilter : IResultFilter
    {
        private readonly UiText _text;
        private readonly ITempDataDictionaryFactory _tempDataFactory;

        public UiMessageTranslationFilter(UiText text, ITempDataDictionaryFactory tempDataFactory)
        {
            _text = text;
            _tempDataFactory = tempDataFactory;
        }

        public void OnResultExecuting(ResultExecutingContext context)
        {
            if (!UiLanguages.IsArabic || context.Result is not (ViewResult or PartialViewResult))
                return;

            foreach (var entry in context.ModelState.Values)
            {
                if (entry.Errors.Count == 0)
                    continue;
                var errors = entry.Errors.ToList();
                entry.Errors.Clear();
                foreach (var error in errors)
                    entry.Errors.Add(_text.Message(error.ErrorMessage));
            }

            var tempData = _tempDataFactory.GetTempData(context.HttpContext);
            foreach (var key in tempData.Keys.ToList())
            {
                if (tempData.Peek(key) is string message)
                {
                    tempData[key] = _text.Message(message);
                    tempData.Keep(key);
                }
            }
        }

        public void OnResultExecuted(ResultExecutedContext context) { }
    }
}
