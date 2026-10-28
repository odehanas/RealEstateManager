namespace RealEstateManager.Domain
{
    /// <summary>
    /// How the company passes on costs it incurs for an owner's managed property, chosen
    /// once per company (VAT Settings).
    /// </summary>
    public static class OwnerCostTreatments
    {
        /// <summary>
        /// The company buys and recharges: it reclaims the supplier's VAT and charges the
        /// owner the cost plus 5% VAT on its own tax invoice.
        /// </summary>
        public const string Recharge = "Recharge";

        /// <summary>
        /// The company pays as the owner's agent: the supplier's invoice is addressed to the
        /// owner, the cost including the supplier's VAT goes to the owner as it is, the
        /// company charges no VAT and reclaims none; the owner reclaims the supplier's VAT.
        /// </summary>
        public const string Disbursement = "Disbursement";

        public static readonly string[] All = { Recharge, Disbursement };

        public static string Describe(string? treatment) => treatment == Disbursement
            ? "Disbursement - paid on the owner's behalf, no VAT"
            : "Recharge - charged on with 5% VAT";
    }

    /// <summary>
    /// The kind of cost charged to an owner, a dimension on each journal line (like the
    /// owner withdrawal categories of the company's own owners). The owners' profitability
    /// is broken down by it.
    /// </summary>
    public static class OwnerCostCategories
    {
        public const string Maintenance = "Maintenance & repairs";
        public const string Utilities = "Utilities";
        public const string Cleaning = "Cleaning & security";
        public const string Insurance = "Insurance";
        public const string Government = "Government fees";
        public const string Leasing = "Leasing & marketing";
        public const string Professional = "Legal & professional";
        public const string Other = "Other";

        /// <summary>The line carrying the VAT on a recharge or a disbursement: reclaimable by a registered owner.</summary>
        public const string Vat = "VAT on costs";

        public static readonly string[] All = { Maintenance, Utilities, Cleaning, Insurance, Government, Leasing, Professional, Other };

        public static string Clean(string? category) => All.Contains(category) ? category! : Maintenance;
    }
}
