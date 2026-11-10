using System.Text;
using System.Text.RegularExpressions;

namespace RealEstateManager.Services.WhatsApp
{
    /// <summary>
    /// Compares the number WhatsApp reports (always international digits, 971501234567)
    /// with Tenant.Phone, which is free text: "+971 50 123 4567", "050-123-4567",
    /// "٠٥٠١٢٣٤٥٦٧", "050 123 4567 / 04 222 3333".
    ///
    /// Each number on the record is brought to its full international form and compared
    /// whole. The search box on the Log a Request screen drops the country and accepts a
    /// partial match, which is right for a person looking down a list. Here nobody is
    /// looking, so a match has to be exact: a near miss goes to the review list rather than
    /// onto someone else's record.
    /// </summary>
    public static class WhatsAppPhoneNumbers
    {
        // E.164 allows up to 15 digits; below 8 it is an extension or a typing slip.
        private const int MinDigits = 8;
        private const int MaxDigits = 15;

        private static readonly Regex Separators = new(
            @"[/,;|\\&\r\n]|\b(?:or|and)\b|\s(?:أو|او)\s", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Extension = new(
            @"(?:\bext\b|\bextension\b|\bx\s*\d|تحويلة).*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The sender as a phone number, or null when it is not one (a WhatsApp username's opaque id).</summary>
        public static string? SenderNumber(string? sender)
        {
            if (string.IsNullOrWhiteSpace(sender))
                return null;

            var trimmed = sender.Trim().TrimStart('+');
            if (!trimmed.All(char.IsDigit))
                return null;

            var digits = Digits(trimmed);
            return IsPlausible(digits) ? digits : null;
        }

        /// <summary>Every international number the free text on a tenant's record can mean.</summary>
        public static HashSet<string> RecordedNumbers(string? recorded, string callingCode)
        {
            var numbers = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(recorded))
                return numbers;

            foreach (var part in Separators.Split(recorded))
            {
                var text = Extension.Replace(part, string.Empty).Trim();
                var digits = Digits(text);
                if (digits.Length < MinDigits)
                    continue;

                if (text.StartsWith('+'))
                {
                    Add(numbers, digits, callingCode);
                }
                else if (digits.StartsWith("00", StringComparison.Ordinal))
                {
                    Add(numbers, digits[2..], callingCode);
                }
                else if (digits.StartsWith('0'))
                {
                    // 050 123 4567: a local number.
                    Add(numbers, callingCode + digits.TrimStart('0'), callingCode);
                }
                else if (digits.StartsWith(callingCode, StringComparison.Ordinal))
                {
                    Add(numbers, digits, callingCode);
                }
                else
                {
                    // 501234567 is a local number without its 0; 919812345678 is a foreign one
                    // without its +. Nothing on the record says which, and both are exact.
                    Add(numbers, callingCode + digits, callingCode);
                    if (digits.Length >= 10)
                        Add(numbers, digits, callingCode);
                }
            }

            return numbers;
        }

        /// <summary>The form the screens show.</summary>
        public static string Display(string sender) => SenderNumber(sender) is { } number ? "+" + number : sender;

        private static void Add(HashSet<string> numbers, string digits, string callingCode)
        {
            // +971 (0) 50 123 4567: the 0 is not dialled from abroad.
            var trunk = callingCode + "0";
            if (digits.StartsWith(trunk, StringComparison.Ordinal))
                digits = callingCode + digits[trunk.Length..].TrimStart('0');

            if (IsPlausible(digits))
                numbers.Add(digits);
        }

        private static bool IsPlausible(string digits) =>
            digits.Length is >= MinDigits and <= MaxDigits && digits[0] != '0';

        /// <summary>Only the digits, with Arabic-Indic and other forms of them made plain.</summary>
        private static string Digits(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (char.IsDigit(character))
                    builder.Append((char)('0' + (int)char.GetNumericValue(character)));
            }
            return builder.ToString();
        }
    }
}
