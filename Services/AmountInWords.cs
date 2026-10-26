namespace RealEstateManager.Services
{
    /// <summary>
    /// Writes an amount the way a UAE cheque expects it:
    /// "UAE Dirhams Twelve Thousand Five Hundred and Fils 50/100 Only".
    /// </summary>
    public static class AmountInWords
    {
        private static readonly string[] Ones =
        {
            "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
            "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"
        };

        private static readonly string[] Tens =
        {
            "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
        };

        public static string Dirhams(decimal amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "A cheque amount cannot be negative.");

            amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
            var whole = (long)Math.Floor(amount);
            var fils = (int)Math.Round((amount - whole) * 100m);

            var words = $"UAE Dirhams {Words(whole)}";
            if (fils > 0)
                words += $" and Fils {fils:D2}/100";
            return words + " Only";
        }

        public static string Words(long number)
        {
            if (number == 0)
                return Ones[0];

            var parts = new List<string>();
            foreach (var (value, name) in new (long, string)[] { (1_000_000_000, "Billion"), (1_000_000, "Million"), (1_000, "Thousand") })
            {
                if (number >= value)
                {
                    parts.Add($"{BelowThousand((int)(number / value))} {name}");
                    number %= value;
                }
            }

            if (number > 0)
                parts.Add(BelowThousand((int)number));

            return string.Join(" ", parts);
        }

        private static string BelowThousand(int n)
        {
            var parts = new List<string>();
            if (n >= 100)
            {
                parts.Add($"{Ones[n / 100]} Hundred");
                n %= 100;
            }

            if (n >= 20)
            {
                parts.Add(n % 10 == 0 ? Tens[n / 10] : $"{Tens[n / 10]}-{Ones[n % 10]}");
            }
            else if (n > 0)
            {
                parts.Add(Ones[n]);
            }

            return string.Join(" ", parts);
        }
    }
}
