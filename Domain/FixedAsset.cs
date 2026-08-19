namespace RealEstateManager.Domain
{
    public class FixedAsset
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int? PropertyId { get; set; }
        public Property? Property { get; set; }

        public string AssetCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AssetCategory { get; set; } = string.Empty;

        public DateTime AcquisitionDate { get; set; }
        public DateTime InServiceDate { get; set; }
        public DateTime? CapitalizationDate { get; set; }

        public decimal Cost { get; set; } = 0m;
        public decimal ResidualValue { get; set; } = 0m;
        public int UsefulLifeMonths { get; set; }

        // Opening sub-ledger values brought forward from a legacy system at the
        // opening balance cut-off date. They feed the GL Fixed Asset cost and
        // Accumulated Depreciation control accounts and do NOT generate journal
        // entries by themselves; the GL opening balance is taken from the
        // control account total which must reconcile with these per-asset values.
        public decimal OpeningCost { get; set; } = 0m;
        public decimal OpeningAccumulatedDepreciation { get; set; } = 0m;

        public string DepreciationMethod { get; set; } = "StraightLine";

        // Draft, Active, FullyDepreciated, Disposed
        public string Status { get; set; } = "Draft";

        public int AssetAccountId { get; set; }
        public Account? AssetAccount { get; set; }

        public int AccumulatedDepreciationAccountId { get; set; }
        public Account? AccumulatedDepreciationAccount { get; set; }

        public int DepreciationExpenseAccountId { get; set; }
        public Account? DepreciationExpenseAccount { get; set; }

        public string? Notes { get; set; }

        public int? CapitalizationJournalEntryId { get; set; }
        public JournalEntry? CapitalizationJournalEntry { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<FixedAssetAcquisitionCost> AcquisitionCosts { get; set; } = new List<FixedAssetAcquisitionCost>();
        public ICollection<FixedAssetDepreciation> DepreciationSchedules { get; set; } = new List<FixedAssetDepreciation>();
    }
}
