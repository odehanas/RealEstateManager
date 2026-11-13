namespace RealEstateManager.Domain
{
    /// <summary>
    /// A class of fixed asset a company keeps - Buildings, Vehicles, Software... - with
    /// its depreciation (or, for an intangible asset, amortization) programme: the method,
    /// the useful life and the accounts. A new asset of the category starts from these;
    /// each can still be changed on the asset itself. An asset stores the category NAME,
    /// so renaming an entry renames it on the company's assets too.
    /// </summary>
    public class FixedAssetCategory
    {
        public const int MaxNameLength = 80;

        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Tangible (depreciated) or Intangible (amortized). See <see cref="FixedAssetClasses"/>.</summary>
        public string AssetClass { get; set; } = FixedAssetClasses.Tangible;

        /// <summary>See <see cref="DepreciationMethods"/>.</summary>
        public string DepreciationMethod { get; set; } = DepreciationMethods.StraightLine;

        /// <summary>Usual useful life; 0 for a category that is not depreciated.</summary>
        public int UsefulLifeMonths { get; set; }

        /// <summary>Yearly rate for the declining balance method, e.g. 25 for 25% a year.</summary>
        public decimal? DecliningRatePercent { get; set; }

        /// <summary>Usual residual value as a share of cost, e.g. 10 for 10%.</summary>
        public decimal ResidualPercent { get; set; }

        // Left empty, the company's default accounts from Account Mapping are used.
        public int? AssetAccountId { get; set; }
        public Account? AssetAccount { get; set; }

        public int? AccumulatedDepreciationAccountId { get; set; }
        public Account? AccumulatedDepreciationAccount { get; set; }

        public int? DepreciationExpenseAccountId { get; set; }
        public Account? DepreciationExpenseAccount { get; set; }

        /// <summary>Off: no longer offered for new assets. Assets that have it keep it.</summary>
        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public static class FixedAssetClasses
    {
        public const string Tangible = "Tangible";
        public const string Intangible = "Intangible";

        public static readonly string[] All = { Tangible, Intangible };

        /// <summary>"depreciation" for a tangible asset, "amortization" for an intangible one.</summary>
        public static string ChargeWord(string? assetClass) =>
            assetClass == Intangible ? "amortization" : "depreciation";
    }

    public static class DepreciationMethods
    {
        /// <summary>The same amount every month over the useful life.</summary>
        public const string StraightLine = "StraightLine";

        /// <summary>A yearly rate on the carrying amount at the start of each year of life, switching to straight line once that charges more.</summary>
        public const string DecliningBalance = "DecliningBalance";

        /// <summary>Land, work in progress, goodwill: carried at cost, no monthly charge.</summary>
        public const string None = "None";

        public static readonly string[] All = { StraightLine, DecliningBalance, None };

        public static string Label(string? method) => method switch
        {
            DecliningBalance => "Declining balance",
            None => "Not depreciated",
            _ => "Straight line"
        };
    }
}
