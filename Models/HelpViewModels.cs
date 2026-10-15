using RealEstateManager.Services;

namespace RealEstateManager.Models
{
    public class HelpCenterViewModel
    {
        public string? Query { get; set; }
        public IReadOnlyList<HelpGroup> Groups { get; set; } = Array.Empty<HelpGroup>();
        public List<HelpSearchResult> Results { get; set; } = new();

        public bool IsSearching => !string.IsNullOrWhiteSpace(Query);
    }

    public class HelpTopicViewModel
    {
        public HelpTopic Topic { get; set; } = new();

        /// <summary>The topic's HTML, cross-references already rewritten for this page.</summary>
        public string Content { get; set; } = string.Empty;

        public HelpGroup Group { get; set; } = new();
        public HelpTopic? Previous { get; set; }
        public HelpTopic? Next { get; set; }
    }

    public class HelpManualViewModel
    {
        public IReadOnlyList<HelpGroup> Groups { get; set; } = Array.Empty<HelpGroup>();

        /// <summary>Topic key to its HTML, cross-references left as in-page anchors.</summary>
        public Dictionary<string, string> Content { get; set; } = new();
    }
}
