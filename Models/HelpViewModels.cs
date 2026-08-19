namespace RealEstateManager.Models
{
    public class HelpPageViewModel
    {
        public List<HelpSectionViewModel> Sections { get; set; } = new();
    }

    public class HelpSectionViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<string> Steps { get; set; } = new();
    }
}
