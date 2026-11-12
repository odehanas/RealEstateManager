using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// A company's fixed asset categories and the depreciation / amortization programme
    /// of each. The first time the list is opened it is filled with the standard classes
    /// of asset, plus any category name the company's existing assets already carry.
    /// </summary>
    public class FixedAssetCategoryService
    {
        private readonly ApplicationDbContext _db;

        public FixedAssetCategoryService(ApplicationDbContext db)
        {
            _db = db;
        }

        private sealed record Standard(
            string Name, string AssetClass, string Method, int LifeMonths,
            string? AssetAccountCode = null, string? AccumulatedAccountCode = null);

        // Useful lives are the usual starting points; each company sets its own.
        private static readonly Standard[] Standards =
        {
            new("Land", FixedAssetClasses.Tangible, DepreciationMethods.None, 0),
            new("Buildings", FixedAssetClasses.Tangible, DepreciationMethods.StraightLine, 480),
            new("Investment property - building", FixedAssetClasses.Tangible, DepreciationMethods.StraightLine, 480, "15100", "15150"),
            new("Building plant (lifts, air conditioning)", FixedAssetClasses.Tangible, DepreciationMethods.StraightLine, 180),
            new("Leasehold improvements", FixedAssetClasses.Tangible, DepreciationMethods.StraightLine, 120),
            new("Machinery & equipment", FixedAssetClasses.Tangible, DepreciationMethods.StraightLine, 120),
            new("Furniture & fixtures", FixedAssetClasses.Tangible, DepreciationMethods.StraightLine, 60),
            new("Office equipment", FixedAssetClasses.Tangible, DepreciationMethods.StraightLine, 60),
            new("Computers & IT equipment", FixedAssetClasses.Tangible, DepreciationMethods.StraightLine, 36),
            new("Vehicles", FixedAssetClasses.Tangible, DepreciationMethods.StraightLine, 60),
            new("Capital work in progress", FixedAssetClasses.Tangible, DepreciationMethods.None, 0),
            new("Software", FixedAssetClasses.Intangible, DepreciationMethods.StraightLine, 36),
            new("Licences & franchises", FixedAssetClasses.Intangible, DepreciationMethods.StraightLine, 60),
            new("Goodwill", FixedAssetClasses.Intangible, DepreciationMethods.None, 0),
            new("General", FixedAssetClasses.Tangible, DepreciationMethods.StraightLine, 60)
        };

        public sealed record Row(FixedAssetCategory Category, int Assets);

        /// <summary>The company's categories for the setup screen, with how many assets carry each.</summary>
        public async Task<List<Row>> ListAsync(int companyId)
        {
            await EnsureSeededAsync(companyId);

            var categories = await _db.FixedAssetCategories.AsNoTracking()
                .Include(x => x.AssetAccount)
                .Include(x => x.AccumulatedDepreciationAccount)
                .Include(x => x.DepreciationExpenseAccount)
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
                .ToListAsync();

            var used = await _db.FixedAssets.AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .GroupBy(x => x.AssetCategory)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .ToListAsync();

            return categories
                .Select(x => new Row(x, used.Where(u => string.Equals(u.Name, x.Name, StringComparison.OrdinalIgnoreCase)).Sum(u => u.Count)))
                .ToList();
        }

        /// <summary>The categories offered for a new asset.</summary>
        public async Task<List<FixedAssetCategory>> ActiveAsync(int companyId)
        {
            await EnsureSeededAsync(companyId);
            return await _db.FixedAssetCategories.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
                .ToListAsync();
        }

        public Task<FixedAssetCategory?> FindAsync(int companyId, int id) =>
            _db.FixedAssetCategories.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id);

        public Task<FixedAssetCategory?> FindByNameAsync(int companyId, string? name) =>
            _db.FixedAssetCategories.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Name == (name ?? string.Empty).Trim());

        /// <summary>Adds (Id 0) or changes a category. Returns what is wrong, or null when saved.</summary>
        public async Task<string?> SaveAsync(int companyId, FixedAssetCategory input)
        {
            var name = string.Join(' ', (input.Name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (name.Length == 0)
                return "Type the name of the category.";
            if (name.Length > FixedAssetCategory.MaxNameLength)
                return $"Keep the name within {FixedAssetCategory.MaxNameLength} characters.";
            if (!FixedAssetClasses.All.Contains(input.AssetClass))
                return "Choose whether the category is tangible or intangible.";
            if (!DepreciationMethods.All.Contains(input.DepreciationMethod))
                return "Choose the depreciation method.";

            var error = ValidateProgramme(input.DepreciationMethod, input.UsefulLifeMonths, input.DecliningRatePercent);
            if (error != null)
                return error;
            if (input.ResidualPercent < 0m || input.ResidualPercent >= 100m)
                return "The residual value must be between 0% and less than 100% of cost.";

            if (await _db.FixedAssetCategories.AnyAsync(x => x.CompanyId == companyId && x.Id != input.Id && x.Name == name))
                return "Another category already has that name.";

            foreach (var accountId in new[] { input.AssetAccountId, input.AccumulatedDepreciationAccountId, input.DepreciationExpenseAccountId })
            {
                if (accountId.HasValue &&
                    !await _db.Accounts.AnyAsync(x => x.Id == accountId.Value && x.CompanyId == companyId && x.IsActive && x.IsPostable))
                    return "Choose active, postable accounts of this company.";
            }

            FixedAssetCategory category;
            if (input.Id == 0)
            {
                var last = await _db.FixedAssetCategories.Where(x => x.CompanyId == companyId).MaxAsync(x => (int?)x.SortOrder) ?? 0;
                category = new FixedAssetCategory { CompanyId = companyId, SortOrder = last + 10 };
                _db.FixedAssetCategories.Add(category);
            }
            else
            {
                var found = await _db.FixedAssetCategories.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == input.Id);
                if (found == null)
                    return "That category was not found.";
                category = found;

                // The assets carry the name, so they follow a rename.
                if (category.Name != name)
                {
                    var assets = await _db.FixedAssets.Where(x => x.CompanyId == companyId && x.AssetCategory == category.Name).ToListAsync();
                    foreach (var asset in assets)
                        asset.AssetCategory = name;
                }
            }

            var isNone = input.DepreciationMethod == DepreciationMethods.None;
            category.Name = name;
            category.AssetClass = input.AssetClass;
            category.DepreciationMethod = input.DepreciationMethod;
            category.UsefulLifeMonths = isNone ? 0 : input.UsefulLifeMonths;
            category.DecliningRatePercent = input.DepreciationMethod == DepreciationMethods.DecliningBalance ? input.DecliningRatePercent : null;
            category.ResidualPercent = isNone ? 0m : input.ResidualPercent;
            category.AssetAccountId = input.AssetAccountId;
            category.AccumulatedDepreciationAccountId = input.AccumulatedDepreciationAccountId;
            category.DepreciationExpenseAccountId = input.DepreciationExpenseAccountId;

            await _db.SaveChangesAsync();
            return null;
        }

        /// <summary>Switches a category off (no longer offered) or back on.</summary>
        public async Task<string?> ToggleAsync(int companyId, int id)
        {
            var category = await _db.FixedAssetCategories.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id);
            if (category == null)
                return "That category was not found.";

            if (category.IsActive && await _db.FixedAssetCategories.CountAsync(x => x.CompanyId == companyId && x.IsActive) <= 1)
                return "Keep at least one category switched on.";

            category.IsActive = !category.IsActive;
            await _db.SaveChangesAsync();
            return null;
        }

        /// <summary>What is wrong with a method / life / rate combination, or null.</summary>
        public static string? ValidateProgramme(string method, int usefulLifeMonths, decimal? decliningRatePercent)
        {
            if (method == DepreciationMethods.None)
                return null;
            if (usefulLifeMonths <= 0)
                return "Useful life in months must be greater than zero.";
            if (usefulLifeMonths > 1200)
                return "Useful life cannot be more than 1,200 months (100 years).";
            if (method == DepreciationMethods.DecliningBalance &&
                (!decliningRatePercent.HasValue || decliningRatePercent <= 0m || decliningRatePercent > 100m))
                return "Enter the yearly declining balance rate, between 0 and 100%.";
            return null;
        }

        private async Task EnsureSeededAsync(int companyId)
        {
            if (await _db.FixedAssetCategories.AnyAsync(x => x.CompanyId == companyId))
                return;

            var accountIds = await _db.Accounts.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.IsActive && x.IsPostable && (x.Code == "15100" || x.Code == "15150"))
                .ToDictionaryAsync(x => x.Code, x => x.Id);

            var order = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var standard in Standards)
            {
                names.Add(standard.Name);
                _db.FixedAssetCategories.Add(new FixedAssetCategory
                {
                    CompanyId = companyId,
                    Name = standard.Name,
                    AssetClass = standard.AssetClass,
                    DepreciationMethod = standard.Method,
                    UsefulLifeMonths = standard.LifeMonths,
                    AssetAccountId = standard.AssetAccountCode != null && accountIds.TryGetValue(standard.AssetAccountCode, out var a) ? a : null,
                    AccumulatedDepreciationAccountId = standard.AccumulatedAccountCode != null && accountIds.TryGetValue(standard.AccumulatedAccountCode, out var d) ? d : null,
                    SortOrder = order += 10
                });
            }

            // Whatever the company typed on its assets before the list existed stays a category.
            var typed = await _db.FixedAssets.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.AssetCategory != "")
                .Select(x => x.AssetCategory)
                .Distinct()
                .ToListAsync();
            foreach (var name in typed.Select(x => x.Trim()).Where(x => x.Length > 0 && x.Length <= FixedAssetCategory.MaxNameLength))
            {
                if (!names.Add(name))
                    continue;
                _db.FixedAssetCategories.Add(new FixedAssetCategory
                {
                    CompanyId = companyId,
                    Name = name,
                    UsefulLifeMonths = 60,
                    SortOrder = order += 10
                });
            }

            await _db.SaveChangesAsync();
        }
    }
}
