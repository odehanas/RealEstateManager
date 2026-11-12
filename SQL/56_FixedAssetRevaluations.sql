-- ============================================================================
-- 56_FixedAssetRevaluations.sql
--
-- Revaluation of a fixed asset (an owned building, land...) to its fair value:
--
--   * FixedAssetRevaluations - each revaluation: the amounts before, the fair
--     value, where the difference went (revaluation surplus in equity, or
--     profit or loss) and the journal that booked it.
--   * FixedAssets.RevaluationEliminatedDepreciation - the depreciation posted
--     before a revaluation and eliminated by it, so the register shows only
--     what has accumulated since.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The revaluations of an asset.
IF OBJECT_ID('FixedAssetRevaluations', 'U') IS NULL CREATE TABLE [FixedAssetRevaluations] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [FixedAssetId] int NOT NULL,
    [RevaluationDate] datetime2 NOT NULL,
    [CostBefore] decimal(18,2) NOT NULL,
    [AccumulatedDepreciationBefore] decimal(18,2) NOT NULL,
    [FairValue] decimal(18,2) NOT NULL,
    [Difference] decimal(18,2) NOT NULL,
    [Treatment] nvarchar(20) NOT NULL,
    [SurplusAmount] decimal(18,2) NOT NULL CONSTRAINT [DF_FixedAssetRevaluations_Surplus] DEFAULT 0,
    [ProfitOrLossAmount] decimal(18,2) NOT NULL CONSTRAINT [DF_FixedAssetRevaluations_ProfitOrLoss] DEFAULT 0,
    [RemainingLifeMonths] int NOT NULL CONSTRAINT [DF_FixedAssetRevaluations_Life] DEFAULT 0,
    [ResidualValue] decimal(18,2) NOT NULL CONSTRAINT [DF_FixedAssetRevaluations_Residual] DEFAULT 0,
    [ValuerName] nvarchar(200) NULL,
    [Notes] nvarchar(1000) NULL,
    [JournalEntryId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_FixedAssetRevaluations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_FixedAssetRevaluations_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_FixedAssetRevaluations_FixedAssets] FOREIGN KEY ([FixedAssetId]) REFERENCES [FixedAssets] ([Id]),
    CONSTRAINT [FK_FixedAssetRevaluations_JournalEntries] FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries] ([Id])
);


-- 2. An asset's revaluations in date order.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FixedAssetRevaluations_FixedAssetId_RevaluationDate' AND object_id = OBJECT_ID('FixedAssetRevaluations')) CREATE INDEX [IX_FixedAssetRevaluations_FixedAssetId_RevaluationDate] ON [FixedAssetRevaluations] ([FixedAssetId], [RevaluationDate]);


-- 3. Depreciation posted before a revaluation and eliminated by it.
IF COL_LENGTH('FixedAssets', 'RevaluationEliminatedDepreciation') IS NULL ALTER TABLE [FixedAssets] ADD [RevaluationEliminatedDepreciation] decimal(18,2) NOT NULL CONSTRAINT [DF_FixedAssets_RevaluationEliminated] DEFAULT 0;
