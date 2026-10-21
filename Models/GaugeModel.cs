using System.Globalization;

namespace RealEstateManager.Models
{
    /// <summary>
    /// A speedometer gauge: a half-circle dial in coloured bands with a needle at the value.
    /// Drawn by Views/Shared/_Gauge.cshtml as plain SVG, so it needs no script and prints.
    /// </summary>
    public class GaugeModel
    {
        public string Title { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public decimal Min { get; set; }
        public decimal Max { get; set; } = 100m;

        /// <summary>Shown after the value: "%", " / 100", or nothing.</summary>
        public string Unit { get; set; } = "%";
        public string Format { get; set; } = "0.#";

        /// <summary>The coloured zones of the dial, from Min to Max.</summary>
        public List<GaugeBand> Bands { get; set; } = new();

        /// <summary>One or two short lines under the dial.</summary>
        public List<string> Notes { get; set; } = new();

        /// <summary>When there is nothing to measure yet - the dial shows "no data" instead of a reading.</summary>
        public bool HasValue { get; set; } = true;

        public string? Link { get; set; }
        public string? LinkText { get; set; }

        /// <summary>The band the value falls in - its colour and name describe the reading.</summary>
        public GaugeBand? CurrentBand => Bands.FirstOrDefault(b => Value >= b.From && Value < b.To)
                                         ?? (Value >= Max ? Bands.LastOrDefault() : Bands.FirstOrDefault());

        public string DisplayValue => HasValue ? Value.ToString(Format, CultureInfo.InvariantCulture) + Unit : "-";

        // ---- Common dials -------------------------------------------------------------

        public const string Red = "#dc3545", Amber = "#ffc107", Green = "#198754", Blue = "#0d6efd";

        /// <summary>Higher is better: red below <paramref name="amberFrom"/>, amber to <paramref name="greenFrom"/>, green above.</summary>
        public static List<GaugeBand> HigherIsBetter(decimal min, decimal amberFrom, decimal greenFrom, decimal max) => new()
        {
            new(min, amberFrom, Red, "Poor"),
            new(amberFrom, greenFrom, Amber, "Watch"),
            new(greenFrom, max, Green, "Good")
        };

        /// <summary>Lower is better: green below <paramref name="amberFrom"/>, amber to <paramref name="redFrom"/>, red above.</summary>
        public static List<GaugeBand> LowerIsBetter(decimal min, decimal amberFrom, decimal redFrom, decimal max) => new()
        {
            new(min, amberFrom, Green, "Good"),
            new(amberFrom, redFrom, Amber, "Watch"),
            new(redFrom, max, Red, "High")
        };
    }

    public sealed record GaugeBand(decimal From, decimal To, string Color, string Name);
}
