-- ============================================================================
-- 21_AddUnitSalesAndRealEstateVat.sql
--
-- Unit sales (development phase 4) and real estate VAT (phase 5).
--
-- Adds:
--   Units.FirstSupplyDate, FirstSupplyType, FirstSupplyReference
--                                   the unit's first sale or lease, for zero rating
--   UnitSales                       sale and purchase agreements
--   UnitSaleInstalments             their payment plans
--   UnitSaleReceipts                the buyers' payments
--   CapitalAssetVatRecords          buildings under the Capital Assets Scheme
--   CapitalAssetVatAdjustments      their yearly adjustments
--
-- Nothing existing is changed. The new accounts 11250, 21250, 41200, 51200 and
-- 61200 are added to every company automatically the first time the new build
-- starts.
--
-- RUN 20_AddCompletionTransfers.sql FIRST.
--
-- HOW TO RUN in site4now SQL Studio: run ONE numbered statement at a time, top
-- to bottom. The console executes only the first batch, so pasting the whole
-- file runs statement 1 and nothing else.
--
-- Run statements 1 to 9 BEFORE deploying the new build.
-- ============================================================================


-- 1. The unit's first supply. Empty for every existing unit.
ALTER TABLE [Units] ADD [FirstSupplyDate] datetime2 NULL, [FirstSupplyType] nvarchar(10) NULL, [FirstSupplyReference] nvarchar(100) NULL;


-- 2. Sale and purchase agreements.
CREATE TABLE [UnitSales] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [ContractNumber] nvarchar(30) NOT NULL,
    [UnitId] int NOT NULL,
    [BuyerId] int NOT NULL,
    [ContractDate] datetime2 NOT NULL,
    [SalePrice] decimal(18,2) NOT NULL,
    [VatCategory] nvarchar(20) NOT NULL,
    [VatRate] decimal(9,4) NOT NULL,
    [VatAmount] decimal(18,2) NOT NULL,
    [TotalPrice] decimal(18,2) NOT NULL,
    [VatReason] nvarchar(300) NULL,
    [Status] nvarchar(20) NOT NULL,
    [HandoverDate] datetime2 NULL,
    [CostOfSales] decimal(18,2) NOT NULL,
    [HandoverNetAmount] decimal(18,2) NOT NULL,
    [HandoverVatAmount] decimal(18,2) NOT NULL,
    [HandoverJournalEntryId] int NULL,
    [BuyerTitleDeedNumber] nvarchar(50) NULL,
    [Notes] nvarchar(1000) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_UnitSales] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UnitSales_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_UnitSales_Units_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Units] ([Id]),
    CONSTRAINT [FK_UnitSales_Tenants_BuyerId] FOREIGN KEY ([BuyerId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_UnitSales_JournalEntries_HandoverJournalEntryId] FOREIGN KEY ([HandoverJournalEntryId]) REFERENCES [JournalEntries] ([Id])
);


-- 3. One contract number per company.
CREATE UNIQUE INDEX [IX_UnitSales_CompanyId_ContractNumber] ON [UnitSales] ([CompanyId], [ContractNumber]);


-- 4. Payment plans.
CREATE TABLE [UnitSaleInstalments] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [UnitSaleId] int NOT NULL,
    [Sequence] int NOT NULL,
    [Description] nvarchar(150) NOT NULL,
    [DueDate] datetime2 NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_UnitSaleInstalments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UnitSaleInstalments_UnitSales_UnitSaleId] FOREIGN KEY ([UnitSaleId]) REFERENCES [UnitSales] ([Id]) ON DELETE CASCADE
);


-- 5. The buyers' payments.
CREATE TABLE [UnitSaleReceipts] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [UnitSaleId] int NOT NULL,
    [ReceiptNumber] nvarchar(30) NOT NULL,
    [TaxInvoiceNumber] nvarchar(30) NULL,
    [ReceiptDate] datetime2 NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [NetAmount] decimal(18,2) NOT NULL,
    [VatAmount] decimal(18,2) NOT NULL,
    [IsBeforeHandover] bit NOT NULL,
    [PaymentMethod] nvarchar(50) NOT NULL,
    [ReceivedIntoAccountId] int NOT NULL,
    [Reference] nvarchar(100) NULL,
    [JournalEntryId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_UnitSaleReceipts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UnitSaleReceipts_UnitSales_UnitSaleId] FOREIGN KEY ([UnitSaleId]) REFERENCES [UnitSales] ([Id]),
    CONSTRAINT [FK_UnitSaleReceipts_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries] ([Id])
);


-- 6. One receipt number per company.
CREATE UNIQUE INDEX [IX_UnitSaleReceipts_CompanyId_ReceiptNumber] ON [UnitSaleReceipts] ([CompanyId], [ReceiptNumber]);


-- 7. Buildings under the Capital Assets Scheme.
CREATE TABLE [CapitalAssetVatRecords] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [Name] nvarchar(150) NOT NULL,
    [PropertyId] int NOT NULL,
    [DevelopmentProjectId] int NULL,
    [AssetValue] decimal(18,2) NOT NULL,
    [InputVat] decimal(18,2) NOT NULL,
    [RecoveredVat] decimal(18,2) NOT NULL,
    [OriginalRecoveryPercent] decimal(9,4) NOT NULL,
    [StartDate] datetime2 NOT NULL,
    [Notes] nvarchar(1000) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_CapitalAssetVatRecords] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CapitalAssetVatRecords_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_CapitalAssetVatRecords_Properties_PropertyId] FOREIGN KEY ([PropertyId]) REFERENCES [Properties] ([Id]),
    CONSTRAINT [FK_CapitalAssetVatRecords_DevelopmentProjects_DevelopmentProjectId] FOREIGN KEY ([DevelopmentProjectId]) REFERENCES [DevelopmentProjects] ([Id])
);


-- 8. Their yearly adjustments, one per year.
CREATE TABLE [CapitalAssetVatAdjustments] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CapitalAssetVatRecordId] int NOT NULL,
    [YearNumber] int NOT NULL,
    [PeriodStart] datetime2 NOT NULL,
    [PeriodEnd] datetime2 NOT NULL,
    [TaxableUsePercent] decimal(9,4) NOT NULL,
    [Adjustment] decimal(18,2) NOT NULL,
    [AdjustmentDate] datetime2 NOT NULL,
    [JournalEntryId] int NULL,
    [Notes] nvarchar(500) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_CapitalAssetVatAdjustments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CapitalAssetVatAdjustments_CapitalAssetVatRecords_CapitalAssetVatRecordId] FOREIGN KEY ([CapitalAssetVatRecordId]) REFERENCES [CapitalAssetVatRecords] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_CapitalAssetVatAdjustments_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries] ([Id]),
    CONSTRAINT [IX_CapitalAssetVatAdjustments_CapitalAssetVatRecordId_YearNumber] UNIQUE ([CapitalAssetVatRecordId], [YearNumber])
);


-- 9. Check: expect one row showing 0 sales and 0 capital assets.
SELECT (SELECT COUNT(*) FROM [UnitSales]) AS [UnitSales], (SELECT COUNT(*) FROM [CapitalAssetVatRecords]) AS [CapitalAssets];
