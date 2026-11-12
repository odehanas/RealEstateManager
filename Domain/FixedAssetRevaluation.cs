namespace RealEstateManager.Domain
{
    /// <summary>
    /// One revaluation of a fixed asset to its fair value. The depreciation accumulated
    /// to the date is eliminated against the asset's gross amount, which is then restated
    /// to the fair value; the difference goes to the revaluation surplus in equity
    /// (IAS 16) or to profit or loss (IAS 40 fair value model), and the months still to
    /// come are re-scheduled from the new amount.
    /// </summary>
    public class FixedAssetRevaluation
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int FixedAssetId { get; set; }
        public FixedAsset? FixedAsset { get; set; }

        public DateTime RevaluationDate { get; set; }

        /// <summary>The asset's gross amount and accumulated depreciation just before.</summary>
        public decimal CostBefore { get; set; }
        public decimal AccumulatedDepreciationBefore { get; set; }
        public decimal CarryingAmountBefore => CostBefore - AccumulatedDepreciationBefore;

        public decimal FairValue { get; set; }

        /// <summary>Fair value less the carrying amount before: positive is an increase.</summary>
        public decimal Difference { get; set; }

        /// <summary>See <see cref="RevaluationTreatments"/>.</summary>
        public string Treatment { get; set; } = RevaluationTreatments.Surplus;

        /// <summary>The part taken to (positive) or out of (negative) the revaluation surplus in equity.</summary>
        public decimal SurplusAmount { get; set; }

        /// <summary>The part taken to profit or loss: positive is a gain, negative a loss.</summary>
        public decimal ProfitOrLossAmount { get; set; }

        /// <summary>The useful life left from this date; 0 for an asset that is not depreciated.</summary>
        public int RemainingLifeMonths { get; set; }
        public decimal ResidualValue { get; set; }

        public string? ValuerName { get; set; }
        public string? Notes { get; set; }

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByName { get; set; }
    }

    public static class RevaluationTreatments
    {
        /// <summary>
        /// IAS 16 revaluation model: an increase goes to the revaluation surplus in equity
        /// (first reversing any earlier decrease of the same asset charged to profit or
        /// loss); a decrease first uses up the asset's surplus, the rest is a loss.
        /// </summary>
        public const string Surplus = "Surplus";

        /// <summary>IAS 40 fair value model: every change in fair value goes to profit or loss.</summary>
        public const string ProfitOrLoss = "ProfitOrLoss";

        public static readonly string[] All = { Surplus, ProfitOrLoss };

        public static string Label(string? treatment) => treatment == ProfitOrLoss
            ? "Profit or loss (fair value model)"
            : "Revaluation surplus in equity";
    }
}
