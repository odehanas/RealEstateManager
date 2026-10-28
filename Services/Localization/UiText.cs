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
        }

        public string this[string english] => Translate(english);

        public IHtmlContent Html(string englishHtml) => new HtmlString(Translate(englishHtml));

        public string Format(string englishTemplate, params object?[] values) =>
            string.Format(CultureInfo.CurrentCulture, Translate(englishTemplate), values);

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
            var language = UiLanguages.Clean(context.Request.Cookies[UiLanguages.CookieName]);
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
