using System.Text.Json;

namespace RealEstateManager.Services.WhatsApp
{
    /// <summary>One message out of a delivery from Meta.</summary>
    public sealed record WhatsAppDeliveredMessage(
        string PhoneNumberId,
        string MessageId,
        string Sender,
        string? ProfileName,
        string Type,
        string? Text,
        DateTime? SentAtUtc);

    /// <summary>
    /// Reads the JSON Meta posts. Read by hand and tolerantly, because Meta adds fields and
    /// message types freely, and a delivery we cannot fully understand must still leave any
    /// message in it on record. Delivery receipts and the like carry no "messages" and yield
    /// nothing. Only call this once the signature has been checked.
    /// </summary>
    public static class WhatsAppWebhookParser
    {
        /// <summary>Not requests and not worth anyone's attention: a thumbs-up, "this person changed number".</summary>
        private static readonly HashSet<string> IgnoredTypes =
            new(StringComparer.OrdinalIgnoreCase) { "reaction", "system", "request_welcome" };

        /// <exception cref="JsonException">The body is not JSON.</exception>
        public static List<WhatsAppDeliveredMessage> Parse(byte[] body)
        {
            var messages = new List<WhatsAppDeliveredMessage>();

            using var document = JsonDocument.Parse(body);
            foreach (var entry in Array(document.RootElement, "entry"))
            {
                foreach (var change in Array(entry, "changes"))
                {
                    if (Text(change, "field") != "messages" ||
                        !change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object)
                        continue;

                    var phoneNumberId = value.TryGetProperty("metadata", out var metadata)
                        ? Text(metadata, "phone_number_id")
                        : null;
                    if (phoneNumberId == null)
                        continue;

                    var contacts = Array(value, "contacts").ToList();

                    foreach (var message in Array(value, "messages"))
                    {
                        var parsed = ParseMessage(message, phoneNumberId, contacts);
                        if (parsed != null)
                            messages.Add(parsed);
                    }
                }
            }

            return messages;
        }

        private static WhatsAppDeliveredMessage? ParseMessage(JsonElement message, string phoneNumberId, List<JsonElement> contacts)
        {
            var id = Text(message, "id");
            var type = Text(message, "type") ?? "unknown";
            if (id == null || IgnoredTypes.Contains(type))
                return null;

            var from = Text(message, "from") ?? Text(message, "from_user_id");
            var contact = contacts.Count == 1
                ? contacts[0]
                : contacts.FirstOrDefault(c => from != null && (Text(c, "wa_id") == from || Text(c, "user_id") == from));

            string? profileName = null;
            string? phone = null;
            if (contact.ValueKind == JsonValueKind.Object)
            {
                phone = Text(contact, "wa_id");
                if (contact.TryGetProperty("profile", out var profile))
                    profileName = Text(profile, "name");
                from ??= Text(contact, "user_id");
            }

            // For someone using a WhatsApp username "from" can be an opaque id, with the number
            // (when they have shared it) only on the contact.
            var sender = WhatsAppPhoneNumbers.SenderNumber(from) ?? WhatsAppPhoneNumbers.SenderNumber(phone) ?? from;
            if (string.IsNullOrWhiteSpace(sender))
                sender = "unknown";

            DateTime? sentAt = null;
            if (long.TryParse(Text(message, "timestamp"), out var seconds) && seconds > 0 && seconds < 253402300799)
                sentAt = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;

            return new WhatsAppDeliveredMessage(phoneNumberId, id, sender, profileName, type, MessageText(message, type), sentAt);
        }

        /// <summary>What the person wrote; for a photo or video, its caption.</summary>
        private static string? MessageText(JsonElement message, string type)
        {
            if (!message.TryGetProperty(type, out var content) || content.ValueKind != JsonValueKind.Object)
                return null;

            switch (type)
            {
                case "text":
                    return Text(content, "body");
                case "button":
                    return Text(content, "text");
                case "image":
                case "video":
                case "document":
                    return Text(content, "caption");
                case "interactive":
                    foreach (var reply in new[] { "button_reply", "list_reply" })
                    {
                        if (content.TryGetProperty(reply, out var chosen))
                            return Text(chosen, "title");
                    }
                    return null;
                default:
                    return null;
            }
        }

        private static IEnumerable<JsonElement> Array(JsonElement parent, string name) =>
            parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray()
                : Enumerable.Empty<JsonElement>();

        private static string? Text(JsonElement parent, string name)
        {
            if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
                return null;

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }
}
