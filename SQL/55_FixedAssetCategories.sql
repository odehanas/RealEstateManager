-- ============================================================================
-- 55_FixedAssetCategories.sql
--
-- Fixed asset categories with their depreciation / amortization programme:
--
--   * FixedAssetCategories - each company's classes of asset (Buildings,
--     Vehicles, Software...), tangible or intangible, with the method, useful
--     life, residual share and accounts a new asset of the class starts from.
--   * FixedAssets.AssetClass - Tangible (depreciated) or Intangible (amortized).
--   * FixedAssets.DecliningRatePercent - the yearly rate of an asset on the
--     declining balance method.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. A company's classes of fixed asset.
IF OBJECT_ID('FixedAssetCategories', 'U') IS NULL CREATE TABLE [FixedAssetCategories] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [Name] nvarchar(80) NOT NULL,
    [AssetClass] nvarchar(20) NOT NULL,
    [DepreciationMethod] nvarchar(30) NOT NULL,
    [UsefulLifeMonths] int NOT NULL CONSTRAINT [DF_FixedAssetCategories_Life] DEFAULT 0,
    [DecliningRatePercent] decimal(9,4) NULL,
    [ResidualPercent] decimal(9,4) NOT NULL CONSTRAINT [DF_FixedAssetCategories_Residual] DEFAULT 0,
    [AssetAccountId] int NULL,
    [AccumulatedDepreciationAccountId] int NULL,
    [DepreciationExpenseAccountId] int NULL,
    [IsActive] bit NOT NULL CONSTRAINT [DF_FixedAssetCategories_IsActive] DEFAULT 1,
    [SortOrder] int NOT NULL CONSTRAINT [DF_FixedAssetCategories_SortOrder] DEFAULT 0,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_FixedAssetCategories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_FixedAssetCategories_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_FixedAssetCategories_AssetAccount] FOREIGN KEY ([AssetAccountId]) REFERENCES [Accounts] ([Id]),
    CONSTRAINT [FK_FixedAssetCategories_AccumAccount] FOREIGN KEY ([AccumulatedDepreciationAccountId]) REFERENCES [Accounts] ([Id]),
    CONSTRAINT [FK_FixedAssetCategories_ExpenseAccount] FOREIGN KEY ([DepreciationExpenseAccountId]) REFERENCES [Accounts] ([Id])
);


-- 2. A company does not have the same category twice.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FixedAssetCategories_CompanyId_Name' AND object_id = OBJECT_ID('FixedAssetCategories')) CREATE UNIQUE INDEX [IX_FixedAssetCategories_CompanyId_Name] ON [FixedAssetCategories] ([CompanyId], [Name]);


-- 3. Whether an asset is depreciated (tangible) or amortized (intangible).
IF COL_LENGTH('FixedAssets', 'AssetClass') IS NULL ALTER TABLE [FixedAssets] ADD [AssetClass] nvarchar(20) NOT NULL CONSTRAINT [DF_FixedAssets_AssetClass] DEFAULT 'Tangible';


-- 4. The yearly rate of an asset on the declining balance method.
IF COL_LENGTH('FixedAssets', 'DecliningRatePercent') IS NULL ALTER TABLE [FixedAssets] ADD [DecliningRatePercent] decimal(9,4) NULL;
