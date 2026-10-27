using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services.Assistant
{
    /// <summary>
    /// Adding units to a property, one or many at once, with the same rules as the New
    /// Unit screen: the unit code is generated from the property code, never typed.
    /// </summary>
    public static class AssistantUnitTools
    {
        private const int MaxUnits = 50;
        private static readonly string[] NewUnitStatuses = { "Vacant", "Reserved", "UnderMaintenance" };
        private static readonly string[] VatCategories = { "Standard", "Exempt", "ZeroRated" };
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        /// <summary>One unit as it will be saved; the code is assigned on save.</summary>
        public sealed class NewUnit
        {
            public string? Floor { get; set; }
            public int? FloorNumber { get; set; }
            public int? FloorPosition { get; set; }
            public string? FloorSide { get; set; }
            public string? UsageType { get; set; }
            public decimal AreaSqFt { get; set; }
            public decimal StandardAnnualRent { get; set; }
            public string Status { get; set; } = "Vacant";
            public string? VatCategory { get; set; }
            public int? ParkingBays { get; set; }
            public string? TitleDeedNumber { get; set; }
        }

        public sealed class NewUnits
        {
            public int PropertyId { get; set; }
            public string PropertyLabel { get; set; } = string.Empty;
            public List<NewUnit> Units { get; set; } = new();
        }

        public static IEnumerable<AssistantTool> All()
        {
            yield return new AssistantTool
            {
                Name = "propose_create_units",
                Description = "Prepare adding one or more units (up to 50) to a property, for the user to confirm. Ask first for what is missing: " +
                              "the property, how many units, the usage type (e.g. Apartment 2BR, Studio, Office, Retail, Villa 4BR), the area in sq ft " +
                              "and the standard annual rent; floors when they differ. Unit codes are generated from the property code when saved. " +
                              "Give values shared by all units at the top level and per-unit differences in 'units' (or just 'count' for identical units).",
                Screen = ("Units", "Create"),
                IsWrite = true,
                Parameters = Schema.Object(
                    ("property", Schema.Str("Property code or name"), true),
                    ("count", Schema.Int("How many identical units, when 'units' is not given"), false),
                    ("usage_type", Schema.Str("Usage type for all units, e.g. Apartment 2BR, Office"), false),
                    ("area_sqft", Schema.Num("Area in sq ft for all units"), false),
                    ("annual_rent", Schema.Num("Standard annual rent for all units"), false),
                    ("status", Schema.Enum("Status for all units, default Vacant", NewUnitStatuses), false),
                    ("vat_category", Schema.Enum("VAT treatment; leave out to inherit the property's", VatCategories), false),
                    ("units", Schema.Array("Per-unit values, one entry per unit; anything left out takes the top-level value",
                        Schema.Object(
                            ("floor", Schema.Str("Floor as written, e.g. 3rd, Ground, Mezzanine"), false),
                            ("floor_number", Schema.Int("Floor number for the building view (0 = ground)"), false),
                            ("floor_position", Schema.Int("Position on the floor, 1 = first"), false),
                            ("floor_side", Schema.Enum("Side of the building", UnitSides.All), false),
                            ("usage_type", Schema.Str("Usage type"), false),
                            ("area_sqft", Schema.Num("Area in sq ft"), false),
                            ("annual_rent", Schema.Num("Standard annual rent"), false),
                            ("parking_bays", Schema.Int("Parking bays"), false),
                            ("title_deed_number", Schema.Str("Unit title deed number"), false))), false)),
                Propose = async (ctx, args) =>
                {
                    var (property, error) = await ResolvePropertyAsync(ctx.Db, Args.Required(args, "property"));
                    if (property == null) return AssistantProposal.Refused(error!);

                    var common = new NewUnit
                    {
                        UsageType = Args.Str(args, "usage_type"),
                        AreaSqFt = Args.Dec(args, "area_sqft") ?? 0m,
                        StandardAnnualRent = Args.Dec(args, "annual_rent") ?? 0m,
                        Status = Args.Str(args, "status") ?? "Vacant",
                        VatCategory = Args.Str(args, "vat_category")
                    };
                    if (!NewUnitStatuses.Contains(common.Status))
                        return AssistantProposal.Refused($"A new unit's status is one of {string.Join(", ", NewUnitStatuses)}; Occupied comes from a contract.");
                    if (common.VatCategory != null && !VatCategories.Contains(common.VatCategory))
                        return AssistantProposal.Refused($"VAT treatment is one of {string.Join(", ", VatCategories)}, or left out to inherit the property's.");

                    var units = new List<NewUnit>();
                    if (args.TryGetProperty("units", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0)
                    {
                        if (list.GetArrayLength() > MaxUnits) return AssistantProposal.Refused($"At most {MaxUnits} units at a time.");
                        foreach (var item in list.EnumerateArray())
                        {
                            var side = Args.Str(item, "floor_side");
                            units.Add(new NewUnit
                            {
                                Floor = Args.Str(item, "floor"),
                                FloorNumber = Args.Int(item, "floor_number"),
                                FloorPosition = Args.Int(item, "floor_position"),
                                FloorSide = side == null ? null : UnitSides.All.FirstOrDefault(x => string.Equals(x, side, StringComparison.OrdinalIgnoreCase)),
                                UsageType = Args.Str(item, "usage_type") ?? common.UsageType,
                                AreaSqFt = Args.Dec(item, "area_sqft") ?? common.AreaSqFt,
                                StandardAnnualRent = Args.Dec(item, "annual_rent") ?? common.StandardAnnualRent,
                                Status = common.Status,
                                VatCategory = common.VatCategory,
                                ParkingBays = Args.Int(item, "parking_bays"),
                                TitleDeedNumber = Args.Str(item, "title_deed_number")
                            });
                        }
                    }
                    else
                    {
                        var count = Args.Int(args, "count") ?? 0;
                        if (count < 1) return AssistantProposal.Refused("Say how many units to add.");
                        if (count > MaxUnits) return AssistantProposal.Refused($"At most {MaxUnits} units at a time.");
                        for (var i = 0; i < count; i++)
                            units.Add(new NewUnit
                            {
                                UsageType = common.UsageType, AreaSqFt = common.AreaSqFt, StandardAnnualRent = common.StandardAnnualRent,
                                Status = common.Status, VatCategory = common.VatCategory
                            });
                    }

                    if (units.Any(u => string.IsNullOrWhiteSpace(u.UsageType)))
                        return AssistantProposal.Refused("Give the usage type of every unit (e.g. Apartment 2BR, Office).");
                    if (units.Any(u => u.AreaSqFt < 0m || u.StandardAnnualRent < 0m))
                        return AssistantProposal.Refused("Area and rent cannot be negative.");

                    var deeds = units.Where(u => u.TitleDeedNumber != null).Select(u => u.TitleDeedNumber!).ToList();
                    if (deeds.Count != deeds.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                        return AssistantProposal.Refused("The same title deed number is given to two units.");

                    var warnings = new List<string>();
                    if (units.Any(u => u.AreaSqFt == 0m)) warnings.Add("Some units have no area; add it later on the unit.");
                    if (units.Any(u => u.StandardAnnualRent == 0m)) warnings.Add("Some units have no standard rent; contracts will need the rent typed in.");
                    var existingDeeds = deeds.Count == 0 ? new List<string>()
                        : await ctx.Db.Units.Where(x => x.TitleDeedNumber != null && deeds.Contains(x.TitleDeedNumber)).Select(x => x.TitleDeedNumber!).ToListAsync();
                    if (existingDeeds.Count > 0) warnings.Add($"Title deed number(s) already on another unit: {string.Join(", ", existingDeeds)}.");

                    var nextCode = await ctx.Get<CodeGenerationService>().GenerateUnitCodeAsync(property.Id);
                    var lines = units.Take(20).Select((u, i) =>
                        $"{i + 1}. {u.UsageType}" + (u.Floor != null ? $", floor {u.Floor}" : u.FloorNumber.HasValue ? $", floor {u.FloorNumber}" : string.Empty) +
                        $", {u.AreaSqFt:N0} sq ft, rent {u.StandardAnnualRent:N0}");
                    return new AssistantProposal
                    {
                        Ok = true,
                        Summary = $"Add {units.Count} unit(s) to {property.Code} {property.Name}, status {common.Status}" +
                                  (common.VatCategory != null ? $", VAT {common.VatCategory}" : string.Empty) +
                                  $": {string.Join("; ", lines)}{(units.Count > 20 ? $"; and {units.Count - 20} more" : string.Empty)}. " +
                                  $"Codes are assigned when saved, starting at {nextCode}.",
                        Warnings = warnings,
                        Normalized = new NewUnits { PropertyId = property.Id, PropertyLabel = $"{property.Code} {property.Name}", Units = units }
                    };
                },
                Execute = async (ctx, args) =>
                {
                    var request = args.Deserialize<NewUnits>(Json)!;
                    if (!await ctx.Db.Properties.AnyAsync(x => x.Id == request.PropertyId))
                        return AssistantActionResult.Failed("The property was not found.");

                    var codes = ctx.Get<CodeGenerationService>();
                    var saved = new List<string>();
                    foreach (var u in request.Units)
                    {
                        // One at a time: each code is worked out from the units already saved.
                        var unit = new Unit
                        {
                            PropertyId = request.PropertyId,
                            UnitNumber = await codes.GenerateUnitCodeAsync(request.PropertyId),
                            Floor = u.Floor,
                            FloorNumber = u.FloorNumber,
                            FloorPosition = u.FloorPosition,
                            FloorSide = u.FloorSide,
                            UsageType = u.UsageType,
                            AreaSqFt = u.AreaSqFt,
                            StandardAnnualRent = u.StandardAnnualRent,
                            Status = u.Status,
                            VatCategory = u.VatCategory,
                            ParkingBays = u.ParkingBays,
                            TitleDeedNumber = u.TitleDeedNumber,
                            IsActive = true
                        };
                        ctx.Db.Units.Add(unit);
                        try
                        {
                            await ctx.Db.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            return AssistantActionResult.Failed(
                                (saved.Count > 0 ? $"Saved before the problem: {string.Join(", ", saved)}. " : string.Empty) +
                                $"The next unit could not be saved: {ex.GetBaseException().Message}");
                        }

                        saved.Add(unit.UnitNumber);
                    }

                    return new AssistantActionResult
                    {
                        Ok = true,
                        Reference = saved.Count == 1 ? saved[0] : $"{saved.First()} to {saved.Last()}",
                        Link = "/Units",
                        Message = $"{saved.Count} unit(s) added to {request.PropertyLabel}: {string.Join(", ", saved)}."
                    };
                }
            };
        }

        private static async Task<(Property? Property, string? Error)> ResolvePropertyAsync(ApplicationDbContext db, string text)
        {
            var exact = await db.Properties.FirstOrDefaultAsync(x => x.Code == text || x.Name == text);
            if (exact != null) return (exact, null);
            var matches = await db.Properties.Where(x => x.Code.Contains(text) || x.Name.Contains(text)).Take(6).ToListAsync();
            return matches.Count switch
            {
                1 => (matches[0], null),
                0 => (null, $"No property matches '{text}'. Use find_properties."),
                _ => (null, $"Several properties match '{text}': {string.Join("; ", matches.Select(m => m.Code + " " + m.Name))}. Ask which one.")
            };
        }
    }
}
