-- ============================================================================
-- 20_AddCompletionTransfers.sql
--
-- Completion transfers and the investment property note (development phase 3).
--
-- Adds:
--   Properties.FairValue, FairValueDate, ValuerName   latest valuation, for the note
--   DevelopmentTransfers        a completed project's cost moved out of work in progress
--   DevelopmentTransferLines    each unit's share: land, building, and where it went
--
-- Nothing existing is changed. The new accounts 13200 Completed Properties Held
-- for Sale and 15150 Accumulated Depreciation - Investment Property are added to
-- every company automatically the first time the new build starts.
--
-- RUN 18_AddPropertyRegistration.sql AND 19_AddDevelopmentProjects.sql FIRST.
--
-- HOW TO RUN in site4now SQL Studio: run ONE numbered statement at a time, top
-- to bottom. The console executes only the first batch, so pasting the whole
-- file runs statement 1 and nothing else.
--
-- Run statements 1 to 4 BEFORE deploying the new build.
-- ============================================================================


-- 1. The latest valuation on each property. Empty for every existing property.
ALTER TABLE [Properties] ADD [FairValue] decimal(18,2) NULL, [FairValueDate] datetime2 NULL, [ValuerName] nvarchar(150) NULL;


-- 2. The transfers.
CREATE TABLE [DevelopmentTransfers] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [DevelopmentProjectId] int NOT NULL,
    [TransferDate] datetime2 NOT NULL,
    [LandCost] decimal(18,2) NOT NULL,
    [BuildingCost] decimal(18,2) NOT NULL,
    [BuildingUsefulLifeYears] int NOT NULL,
    [RentalFixedAssetId] int NULL,
    [OwnUseFixedAssetId] int NULL,
    [JournalEntryId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_DevelopmentTransfers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DevelopmentTransfers_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_DevelopmentTransfers_DevelopmentProjects_DevelopmentProjectId] FOREIGN KEY ([DevelopmentProjectId]) REFERENCES [DevelopmentProjects] ([Id]),
    CONSTRAINT [FK_DevelopmentTransfers_FixedAssets_RentalFixedAssetId] FOREIGN KEY ([RentalFixedAssetId]) REFERENCES [FixedAssets] ([Id]),
    CONSTRAINT [FK_DevelopmentTransfers_FixedAssets_OwnUseFixedAssetId] FOREIGN KEY ([OwnUseFixedAssetId]) REFERENCES [FixedAssets] ([Id]),
    CONSTRAINT [FK_DevelopmentTransfers_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries] ([Id])
);


-- 3. Each unit's share of a transfer.
CREATE TABLE [DevelopmentTransferLines] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [DevelopmentTransferId] int NOT NULL,
    [UnitId] int NOT NULL,
    [IntendedUse] nvarchar(20) NOT NULL,
    [AreaSqFt] decimal(18,2) NOT NULL,
    [LandCost] decimal(18,2) NOT NULL,
    [BuildingCost] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_DevelopmentTransferLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DevelopmentTransferLines_DevelopmentTransfers_DevelopmentTransferId] FOREIGN KEY ([DevelopmentTransferId]) REFERENCES [DevelopmentTransfers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_DevelopmentTransferLines_Units_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Units] ([Id])
);


-- 4. Check: expect one row showing 0 transfers.
SELECT COUNT(*) AS [DevelopmentTransfers] FROM [DevelopmentTransfers];
