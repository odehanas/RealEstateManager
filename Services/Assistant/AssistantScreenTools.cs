using System.Text.Json.Nodes;

namespace RealEstateManager.Services.Assistant
{
    /// <summary>
    /// Tools that work on the form the user has open, run by the panel in the browser
    /// (wwwroot/js/assistant.js), never here. They only type into the page: saving is
    /// the screen's own Save button, which goes through the screen's own validation and
    /// permissions, so they need no permission of their own.
    /// </summary>
    public static class AssistantScreenTools
    {
        public const string ReadForm = "read_screen_form";
        public const string FillForm = "fill_screen_form";
        public const string ListScreens = "list_screens";
        public const string OpenScreen = "open_screen";

        public static IEnumerable<JsonNode> Definitions()
        {
            yield return Definition(ReadForm,
                "Read the entry form on the user's screen: each field's name, label, current value and, for lists, the choices. " +
                "Also lists the buttons that add line rows. Call it before fill_screen_form, and again after adding rows.",
                Schema.Object());

            yield return Definition(FillForm,
                "Type values into the form on the user's screen (any form: bills, contracts, tenants, journals...). Never saves: " +
                "the user checks the highlighted fields and clicks Save. Use the field names from read_screen_form. For a list, give " +
                "the choice's value or its text. For a checkbox, true or false.",
                Schema.Object(
                    ("add_rows", Schema.Object(
                        ("button", Schema.Str("Text of the add-row button, as read_screen_form listed it"), true),
                        ("times", Schema.Int("How many rows to add"), true)), false),
                    ("fields", Schema.Array("The values to type",
                        Schema.Object(
                            ("name", Schema.Str("Field name, e.g. VendorReference or Lines[0].Quantity"), true),
                            ("value", Schema.Str("Value to enter"), true))), true)));
        }

        /// <summary>Opening another screen, optionally with its form filled in on arrival.</summary>
        public static IEnumerable<JsonNode> Navigation()
        {
            yield return Definition(ListScreens,
                "List the screens in the user's menu (only those they may open) with their paths. New-record forms are " +
                "usually /<Screen>/Create, e.g. /Units/Create, /Tenants/Create, /VendorBills/Create.",
                Schema.Object());

            yield return Definition(OpenScreen,
                "Open a screen in the user's browser, optionally filling in its form when it opens (for the user to check and save). " +
                "Use it when the user asks to open a screen, or wants to finish an entry themselves. Opening a page ENDS this conversation, " +
                "so collect everything first and say so in one short sentence. Give fields by the label shown on the screen " +
                "(e.g. Property, Area (sq ft), Standard annual rent) or by field name.",
                Schema.Object(
                    ("path", Schema.Str("The screen's path from list_screens, e.g. /Units/Create"), true),
                    ("fields", Schema.Array("Values to fill in when it opens",
                        Schema.Object(
                            ("name", Schema.Str("Label on the screen, or field name"), true),
                            ("value", Schema.Str("Value to enter"), true))), false)));
        }

        private static JsonNode Definition(string name, string description, JsonObject parameters) => new JsonObject
        {
            ["type"] = "function",
            ["name"] = name,
            ["description"] = description,
            ["parameters"] = parameters
        };
    }
}
