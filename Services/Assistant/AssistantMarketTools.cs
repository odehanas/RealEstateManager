using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;

namespace RealEstateManager.Services.Assistant
{
    /// <summary>
    /// Outside data: published market rents found on the web, always set beside the
    /// company's own rents for the same kind of unit in the same place.
    /// </summary>
    public static class AssistantMarketTools
    {
        public static IEnumerable<AssistantTool> All()
        {
            yield return new AssistantTool
            {
                Name = "market_rent_benchmark",
                Description = "Market rent benchmark from the web for a kind of unit in a UAE city or area, compared with the company's own rents " +
                              "for the same kind of unit there. Takes up to a minute. Always give the user the sources with their dates, say whether " +
                              "the figures are asking or contracted rents, and call them indicative.",
                Screen = ("Reports", "PropertyUtilization"),
                Parameters = Schema.Object(
                    ("city", Schema.Str("Emirate or city, e.g. Ras Al Khaimah, Dubai"), true),
                    ("area", Schema.Str("Area or community, e.g. Al Marjan Island; leave out for the whole city"), false),
                    ("unit_type", Schema.Str("Kind of unit, e.g. studio, 2-bedroom apartment, 4-bedroom villa, office, retail shop"), true)),
                Run = async (ctx, args) =>
                {
                    var city = Args.Required(args, "city");
                    var area = Args.Str(args, "area");
                    var unitType = Args.Required(args, "unit_type");

                    var ours = await OwnRentsAsync(ctx, city, area, unitType);

                    MarketRentService.MarketRent market;
                    try
                    {
                        market = await ctx.Get<MarketRentService>().LookUpAsync(city, area, unitType);
                    }
                    catch (MarketRentService.MarketRentException ex)
                    {
                        return new { error = ex.Message, ours };
                    }

                    decimal? gap = market.Found && market.TypicalAnnualRent is > 0m && ours.AverageAnnualRent is > 0m
                        ? Math.Round((ours.AverageAnnualRent.Value - market.TypicalAnnualRent.Value) / market.TypicalAnnualRent.Value * 100m, 1)
                        : null;

                    return new
                    {
                        query = new { city, area, unit_type = unitType },
                        market = new
                        {
                            market.Found,
                            typical_annual_rent = market.TypicalAnnualRent,
                            range_annual = market.LowAnnualRent.HasValue || market.HighAnnualRent.HasValue
                                ? $"{market.LowAnnualRent:N0} - {market.HighAnnualRent:N0}" : null,
                            per_sqft_range = market.PerSqftLow.HasValue || market.PerSqftHigh.HasValue
                                ? $"{market.PerSqftLow:N0} - {market.PerSqftHigh:N0}" : null,
                            rent_basis = market.RentBasis,
                            as_of = market.AsOf,
                            notes = market.Notes,
                            searched_at = market.SearchedAt.ToString("yyyy-MM-dd HH:mm"),
                            sources = market.Sources.Select(s => new { s.Title, s.Url, s.Published })
                        },
                        ours,
                        our_average_vs_market_typical_percent = gap,
                        caution = "Market figures come from published web sources and are indicative only; they may be asking rents rather than " +
                                  "signed contracts, and units differ in size, view and finish. Only the place and the kind of unit were searched; " +
                                  "no company data was sent."
                    };
                }
            };
        }

        public sealed class OwnRents
        {
            public string Scope { get; init; } = string.Empty;
            public int Units { get; init; }
            public int Let { get; init; }
            public decimal? AverageAnnualRent { get; init; }
            public decimal? LowestAnnualRent { get; init; }
            public decimal? HighestAnnualRent { get; init; }
            public decimal? AverageRentPerSqft { get; init; }
            public decimal? AverageAskingRentOfVacant { get; init; }
            public List<string> Examples { get; init; } = new();
        }

        /// <summary>
        /// The company's own rents for matching units: running contracts first, the
        /// standard (asking) rent of vacant ones beside them. Falls back from the area
        /// to the whole city when the area has none, and says so.
        /// </summary>
        private static async Task<OwnRents> OwnRentsAsync(AssistantToolContext ctx, string city, string? area, string unitType)
        {
            var units = await ctx.Db.Units.AsNoTracking()
                .Where(x => x.IsActive && x.Status != "Sold")
                .Select(x => new
                {
                    x.Id, x.UnitNumber, x.UsageType, x.AreaSqFt, x.StandardAnnualRent, x.Status,
                    PropertyCode = x.Property!.Code, x.Property.Emirate, x.Property.AreaName, x.Property.PropertyType,
                    Rent = x.Leases.Where(l => l.Status == LeaseStatuses.Active).OrderByDescending(l => l.StartDate)
                        .Select(l => (decimal?)l.AnnualRent).FirstOrDefault()
                })
                .ToListAsync();

            var wanted = Tokens(unitType);
            var inCity = units.Where(u => Contains(u.Emirate, city) || Contains(u.AreaName, city))
                .Where(u => wanted.All(t => Tokens($"{u.UsageType} {u.PropertyType}").Contains(t)))
                .ToList();
            var inArea = area == null ? inCity : inCity.Where(u => Contains(u.AreaName, area)).ToList();

            var scope = area == null ? city : $"{area}, {city}";
            var chosen = inArea;
            if (area != null && inArea.Count == 0 && inCity.Count > 0)
            {
                chosen = inCity;
                scope = $"{city} (none of the company's matching units are in {area})";
            }

            var let = chosen.Where(u => u.Rent is > 0m).ToList();
            var vacant = chosen.Where(u => u.Rent == null && u.StandardAnnualRent > 0m).ToList();
            var withArea = let.Where(u => u.AreaSqFt > 0m).ToList();

            return new OwnRents
            {
                Scope = chosen.Count == 0 ? $"the company has no {unitType} units in {scope}" : scope,
                Units = chosen.Count,
                Let = let.Count,
                AverageAnnualRent = let.Count > 0 ? Math.Round(let.Average(u => u.Rent!.Value), 0) : null,
                LowestAnnualRent = let.Count > 0 ? let.Min(u => u.Rent) : null,
                HighestAnnualRent = let.Count > 0 ? let.Max(u => u.Rent) : null,
                AverageRentPerSqft = withArea.Count > 0 ? Math.Round(withArea.Average(u => u.Rent!.Value / u.AreaSqFt), 1) : null,
                AverageAskingRentOfVacant = vacant.Count > 0 ? Math.Round(vacant.Average(u => u.StandardAnnualRent), 0) : null,
                Examples = chosen.Take(8)
                    .Select(u => $"{u.PropertyCode}/{u.UnitNumber} {u.UsageType}, {u.AreaSqFt:N0} sq ft, " +
                                 (u.Rent is > 0m ? $"let at {u.Rent:N0}" : $"{u.Status}, standard rent {u.StandardAnnualRent:N0}"))
                    .ToList()
            };
        }

        private static bool Contains(string? text, string part) =>
            text != null && text.Contains(part.Trim(), StringComparison.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
        {
            ["flat"] = "apartment", ["apartments"] = "apartment", ["villas"] = "villa", ["townhouse"] = "villa",
            ["shop"] = "retail", ["shops"] = "retail", ["store"] = "retail", ["offices"] = "office",
            ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4", ["five"] = "5"
        };

        private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "unit", "units", "space", "residential", "commercial", "for", "rent", "in"
        };

        /// <summary>"2-bedroom apartment", "two bed flat" and "Apartment 2BR" all become {2br, apartment}.</summary>
        private static HashSet<string> Tokens(string text)
        {
            var words = Regex.Split(text.ToLowerInvariant(), @"[^a-z0-9]+")
                .Where(w => w.Length > 0)
                .Select(w => Synonyms.TryGetValue(w, out var s) ? s : w)
                .ToList();
            var joined = Regex.Replace(string.Join(" ", words), @"\b(\d)\s*(bed|beds|bedroom|bedrooms|br|bhk)\b", "$1br");
            return joined.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Ignored.Contains(w)).ToHashSet();
        }
    }
}
