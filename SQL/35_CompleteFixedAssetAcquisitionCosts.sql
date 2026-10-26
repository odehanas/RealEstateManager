-- ============================================================================
-- 35_CompleteFixedAssetAcquisitionCosts.sql
--
-- Fixed asset capitalisation (acquisition costs, the clearing account and the
-- capitalisation entry) was introduced by the hand-run script
-- FIXEDASSET_CAPITALIZATION_SQL.sql in the project root. A database where that
-- script was never run fails on Fixed Assets > Acquisition with
-- "Invalid column name 'VendorId' / 'VendorBillId'". This brings every such
-- database up to date; on one where it was run, every statement is skipped.
--
-- Adds, when missing:
--   FixedAssets                  CapitalizationDate, CapitalizationJournalEntryId
--   FixedAssetAcquisitionCosts   the table, and its OffsetAccountId, VendorId,
--                                VendorBillId, ExpenseAccountId, JournalEntryId,
--                                CapitalizedAt columns and their foreign keys
--   CompanyAccountSettings       FixedAssetClearingAccountId
--
-- Applied automatically when the application starts (see SQL/README.md).
-- ============================================================================


-- 1. When the asset was capitalised.
IF COL_LENGTH('FixedAssets', 'CapitalizationDate') IS NULL ALTER TABLE [FixedAssets] ADD [CapitalizationDate] datetime2 NULL;


-- 2. The capitalisation journal entry.
IF COL_LENGTH('FixedAssets', 'CapitalizationJournalEntryId') IS NULL ALTER TABLE [FixedAssets] ADD [CapitalizationJournalEntryId] int NULL;


-- 3. Its foreign key.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FixedAssets_JournalEntries_CapitalizationJournalEntryId') ALTER TABLE [FixedAssets] ADD CONSTRAINT [FK_FixedAssets_JournalEntries_CapitalizationJournalEntryId] FOREIGN KEY ([CapitalizationJournalEntryId]) REFERENCES [JournalEntries] ([Id]);


-- 4. The costs that make up an asset's acquisition.
IF OBJECT_ID('FixedAssetAcquisitionCosts', 'U') IS NULL CREATE TABLE [FixedAssetAcquisitionCosts] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [FixedAssetId] int NOT NULL,
    [CostDate] datetime2 NOT NULL,
    [CostType] nvarchar(50) NOT NULL,
    [Description] nvarchar(300) NOT NULL,
    [Amount] decimal(18,2) NOT NULL CONSTRAINT [DF_FixedAssetAcquisitionCosts_Amount] DEFAULT (0),
    [IsCapitalizable] bit NOT NULL CONSTRAINT [DF_FixedAssetAcquisitionCosts_IsCapitalizable] DEFAULT (1),
    [Status] nvarchar(30) NOT NULL CONSTRAINT [DF_FixedAssetAcquisitionCosts_Status] DEFAULT ('Open'),
    [SourceDocumentNumber] nvarchar(100) NULL,
    [ReferenceNumber] nvarchar(100) NULL,
    [Notes] nvarchar(1000) NULL,
    [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_FixedAssetAcquisitionCosts_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_FixedAssetAcquisitionCosts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_FixedAssetAcquisitionCosts_FixedAssets_FixedAssetId] FOREIGN KEY ([FixedAssetId]) REFERENCES [FixedAssets] ([Id]) ON DELETE CASCADE
);


-- 5. The account credited when the cost was recorded.
IF COL_LENGTH('FixedAssetAcquisitionCosts', 'OffsetAccountId') IS NULL ALTER TABLE [FixedAssetAcquisitionCosts] ADD [OffsetAccountId] int NULL;


-- 6. The supplier.
IF COL_LENGTH('FixedAssetAcquisitionCosts', 'VendorId') IS NULL ALTER TABLE [FixedAssetAcquisitionCosts] ADD [VendorId] int NULL;


-- 7. The supplier bill the cost came from.
IF COL_LENGTH('FixedAssetAcquisitionCosts', 'VendorBillId') IS NULL ALTER TABLE [FixedAssetAcquisitionCosts] ADD [VendorBillId] int NULL;


-- 8. The expense account for a cost that is not capitalised.
IF COL_LENGTH('FixedAssetAcquisitionCosts', 'ExpenseAccountId') IS NULL ALTER TABLE [FixedAssetAcquisitionCosts] ADD [ExpenseAccountId] int NULL;


-- 9. The journal entry of the cost.
IF COL_LENGTH('FixedAssetAcquisitionCosts', 'JournalEntryId') IS NULL ALTER TABLE [FixedAssetAcquisitionCosts] ADD [JournalEntryId] int NULL;


-- 10. When the cost was capitalised.
IF COL_LENGTH('FixedAssetAcquisitionCosts', 'CapitalizedAt') IS NULL ALTER TABLE [FixedAssetAcquisitionCosts] ADD [CapitalizedAt] datetime2 NULL;


-- 11. Offset account foreign key.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FixedAssetAcquisitionCosts_Accounts_OffsetAccountId') ALTER TABLE [FixedAssetAcquisitionCosts] ADD CONSTRAINT [FK_FixedAssetAcquisitionCosts_Accounts_OffsetAccountId] FOREIGN KEY ([OffsetAccountId]) REFERENCES [Accounts] ([Id]);


-- 12. Supplier foreign key.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FixedAssetAcquisitionCosts_Vendors_VendorId') ALTER TABLE [FixedAssetAcquisitionCosts] ADD CONSTRAINT [FK_FixedAssetAcquisitionCosts_Vendors_VendorId] FOREIGN KEY ([VendorId]) REFERENCES [Vendors] ([Id]);


-- 13. Supplier bill foreign key.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FixedAssetAcquisitionCosts_VendorBills_VendorBillId') ALTER TABLE [FixedAssetAcquisitionCosts] ADD CONSTRAINT [FK_FixedAssetAcquisitionCosts_VendorBills_VendorBillId] FOREIGN KEY ([VendorBillId]) REFERENCES [VendorBills] ([Id]);


-- 14. Expense account foreign key.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FixedAssetAcquisitionCosts_Accounts_ExpenseAccountId') ALTER TABLE [FixedAssetAcquisitionCosts] ADD CONSTRAINT [FK_FixedAssetAcquisitionCosts_Accounts_ExpenseAccountId] FOREIGN KEY ([ExpenseAccountId]) REFERENCES [Accounts] ([Id]);


-- 15. Journal entry foreign key.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FixedAssetAcquisitionCosts_JournalEntries_JournalEntryId') ALTER TABLE [FixedAssetAcquisitionCosts] ADD CONSTRAINT [FK_FixedAssetAcquisitionCosts_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries] ([Id]);


-- 16. Costs are listed by asset and date.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FixedAssetAcquisitionCosts_FixedAssetId_CostDate_Id' AND object_id = OBJECT_ID('FixedAssetAcquisitionCosts')) CREATE INDEX [IX_FixedAssetAcquisitionCosts_FixedAssetId_CostDate_Id] ON [FixedAssetAcquisitionCosts] ([FixedAssetId], [CostDate], [Id]);


-- 17. The clearing account fixed asset purchases are booked to before capitalisation.
IF COL_LENGTH('CompanyAccountSettings', 'FixedAssetClearingAccountId') IS NULL ALTER TABLE [CompanyAccountSettings] ADD [FixedAssetClearingAccountId] int NULL;


-- 18. Its foreign key.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_CompanyAccountSettings_Accounts_FixedAssetClearingAccountId') ALTER TABLE [CompanyAccountSettings] ADD CONSTRAINT [FK_CompanyAccountSettings_Accounts_FixedAssetClearingAccountId] FOREIGN KEY ([FixedAssetClearingAccountId]) REFERENCES [Accounts] ([Id]);
