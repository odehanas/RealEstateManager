namespace RealEstateManager.Domain
{
    /// <summary>
    /// How a contract's rent is agreed. Annual is the ordinary tenancy: a yearly rent paid
    /// in instalments. The others are a stay priced at a rate per day, week or month and
    /// billed for exactly the days it lasts - short-term letting.
    /// </summary>
    public static class RentBases
    {
        public const string Annual = "Annual";
        public const string Monthly = "Monthly";
        public const string Weekly = "Weekly";
        public const string Daily = "Daily";

        public static readonly string[] All = { Annual, Monthly, Weekly, Daily };

        /// <summary>Priced by a rate for the stay rather than by a yearly rent.</summary>
        public static bool IsStay(string? basis) => basis is Monthly or Weekly or Daily;

        public static string Clean(string? basis) => All.Contains(basis) ? basis! : Annual;

        /// <summary>"day", "week", "month" or "year".</summary>
        public static string Unit(string? basis) => basis switch
        {
            Daily => "day",
            Weekly => "week",
            Monthly => "month",
            _ => "year"
        };

        public static string Label(string? basis) => basis switch
        {
            Daily => "Daily rate",
            Weekly => "Weekly rate",
            Monthly => "Monthly rate",
            _ => "Annual rent"
        };

        /// <summary>
        /// The rate as a yearly figure, kept in the contract's AnnualRent so the reports
        /// that compare rents per year still have one for a short stay.
        /// </summary>
        public static decimal Annualise(string? basis, decimal rate) => basis switch
        {
            Daily => rate * 365m,
            Weekly => rate * 52m,
            Monthly => rate * 12m,
            _ => rate
        };
    }
}
