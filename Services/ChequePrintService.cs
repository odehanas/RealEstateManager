using System.Globalization;
using System.Text;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Prints salary cheques on the company's own pre-printed cheque leaves.
    ///
    /// One layout - where the date, payee, amount in words and amount in figures go, in
    /// millimetres from the cheque's top-left corner - drives both kinds of printer:
    ///
    ///  - Laser and inkjet (page printers): an HTML page sized exactly to the cheque, each
    ///    field placed in millimetres, printed from the browser at 100% scale.
    ///  - Dot matrix (impact, tractor or single-sheet feed): a plain text file in the
    ///    printer's own character grid (characters and lines per inch), sent through the
    ///    printer's "Generic / Text Only" driver, one cheque per form feed.
    ///
    /// Offsets in Payroll Settings nudge every field together after a test print, so one
    /// layout can be matched to a particular printer without re-measuring.
    /// </summary>
    public static class ChequePrintService
    {
        public sealed record ChequeItem(string ChequeNumber, string Payee, DateTime Date, decimal Amount)
        {
            public string AmountInWords => Services.AmountInWords.Dirhams(Amount);
        }

        public static string FormatAmount(decimal amount) =>
            "**" + amount.ToString("#,##0.00", CultureInfo.InvariantCulture) + "**";

        public static string FormatDate(PayrollSettings settings, DateTime date) =>
            date.ToString(string.IsNullOrWhiteSpace(settings.DateFormat) ? "dd/MM/yyyy" : settings.DateFormat, CultureInfo.InvariantCulture);

        /// <summary>
        /// The cheques as text for a dot matrix printer. Each field is placed on the
        /// character cell nearest its millimetre position; the amount in words wraps
        /// within its width onto the next line.
        /// </summary>
        public static string BuildDotMatrixText(PayrollSettings settings, IEnumerable<ChequeItem> cheques)
        {
            var cpi = settings.DotMatrixCpi <= 0 ? 10 : settings.DotMatrixCpi;
            var lpi = settings.DotMatrixLpi <= 0 ? 6 : settings.DotMatrixLpi;
            int Col(decimal mm) => Math.Max(0, (int)Math.Round((mm + settings.OffsetXMm) / 25.4m * cpi));
            int Row(decimal mm) => Math.Max(0, (int)Math.Round((mm + settings.OffsetYMm) / 25.4m * lpi));

            var rows = Math.Max(1, (int)Math.Ceiling(settings.ChequeHeightMm / 25.4m * lpi));
            var cols = Math.Max(20, (int)Math.Ceiling(settings.ChequeWidthMm / 25.4m * cpi));

            var output = new StringBuilder();
            var first = true;
            foreach (var cheque in cheques)
            {
                if (!first)
                    output.Append('\f');
                first = false;

                var grid = Enumerable.Range(0, rows).Select(_ => new char[cols]).ToList();
                foreach (var line in grid)
                    Array.Fill(line, ' ');

                void Put(int row, int col, string text)
                {
                    if (row >= grid.Count)
                        return;
                    for (var i = 0; i < text.Length && col + i < cols; i++)
                        grid[row][col + i] = text[i];
                }

                if (settings.PrintAccountPayeeOnly)
                    Put(Row(4m), Col(4m), "A/C PAYEE ONLY");

                Put(Row(settings.DateYMm), Col(settings.DateXMm), FormatDate(settings, cheque.Date));
                Put(Row(settings.PayeeYMm), Col(settings.PayeeXMm), "**" + cheque.Payee.ToUpperInvariant() + "**");

                var wordsWidth = Math.Max(10, (int)Math.Floor(settings.AmountWordsWidthMm / 25.4m * cpi));
                var wordsRow = Row(settings.AmountWordsYMm);
                foreach (var part in Wrap(cheque.AmountInWords, wordsWidth))
                    Put(wordsRow++, Col(settings.AmountWordsXMm), part);

                Put(Row(settings.AmountYMm), Col(settings.AmountXMm), FormatAmount(cheque.Amount));

                output.Append(string.Join("\r\n", grid.Select(x => new string(x).TrimEnd())));
                output.Append("\r\n");
            }

            return output.ToString();
        }

        private static IEnumerable<string> Wrap(string text, int width)
        {
            var line = new StringBuilder();
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    yield return line.ToString();
                    line.Clear();
                }

                if (line.Length > 0)
                    line.Append(' ');
                line.Append(word);
            }

            if (line.Length > 0)
                yield return line.ToString();
        }
    }
}
