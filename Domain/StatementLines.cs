namespace RealEstateManager.Domain
{
    /// <summary>
    /// The line of the income statement a revenue or expense account is shown on. A
    /// classification of its own, beside the account type: it decides how the Profit
    /// &amp; Loss is laid out (revenue by kind, cost of revenue, fixed and recurring costs,
    /// finance costs), never what the account is.
    /// </summary>
    public static class StatementLines
    {
        public const string RentalIncome = "RentalIncome";
        public const string Commission = "Commission";
        public const string PropertySales = "PropertySales";
        public const string OtherIncome = "OtherIncome";
        public const string CostOfRevenue = "CostOfRevenue";
        public const string FixedCosts = "FixedCosts";
        public const string RecurringCosts = "RecurringCosts";
        public const string FinanceCosts = "FinanceCosts";

        public static readonly string[] Revenue = { RentalIncome, Commission, PropertySales, OtherIncome };
        public static readonly string[] Costs = { CostOfRevenue, FixedCosts, RecurringCosts, FinanceCosts };
        public static readonly string[] All = Revenue.Concat(Costs).ToArray();

        public static string Describe(string? line) => line switch
        {
            RentalIncome => "Rental income (owned properties)",
            Commission => "Management commission (managed properties)",
            PropertySales => "Property sales",
            OtherIncome => "Other income",
            CostOfRevenue => "Cost of revenue",
            FixedCosts => "Fixed costs",
            RecurringCosts => "Recurring costs",
            FinanceCosts => "Finance costs",
            _ => "Not set"
        };

        public static string Hint(string line) => line switch
        {
            CostOfRevenue => "costs of earning the revenue: property operating costs, maintenance of owned properties, cost of units sold",
            FixedCosts => "the same every month whatever the activity: salaries, office rent, depreciation, insurance",
            RecurringCosts => "running costs that move with the activity: utilities, office supplies, overtime, irrecoverable VAT",
            FinanceCosts => "interest and bank charges",
            _ => string.Empty
        };

        /// <summary>
        /// The line for an account nobody has set: the standard chart's accounts by code,
        /// otherwise by reporting group. The account's own setting, or its group's, wins.
        /// </summary>
        public static string Default(string code, string accountType, string reportingGroup) => code switch
        {
            "41100" => RentalIncome,
            "42100" => Commission,
            "41200" => PropertySales,
            "63100" => FinanceCosts,
            "61100" or "61200" or "64300" => RecurringCosts,
            _ when accountType == "Revenue" => OtherIncome,
            _ when reportingGroup == "DirectExpense" => CostOfRevenue,
            _ => FixedCosts
        };

        public static bool IsRevenueLine(string line) => Revenue.Contains(line);
    }
}
