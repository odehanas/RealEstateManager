-- =============================================================================
-- 02_AddOwnerWithdrawalCategories.sql
-- -----------------------------------------------------------------------------
-- Adds the schema for the owner expenditure feature. Three changes, nothing else:
--
--   1. [OwnerWithdrawalCategories]                     new table, the new dimension
--   2. [JournalEntryLines].[OwnerWithdrawalCategoryId] new nullable FK column
--   3. [Accounts].[IsOwnerAccount]                     new bit column, default 0
--
-- Purely additive. No existing table, column or row is modified or dropped.
-- Existing journals are unaffected: the new column is nullable and every current
-- row simply holds NULL.
--
-- RUN ORDER
--   Run 01_Restore_PdcReceipts.sql first if you have not already, so the schema
--   is whole before adding to it.
--
-- HOW TO RUN IN A RESTRICTED WEB CONSOLE (site4now SQL Studio)
--   That console rejects block comments, EXEC, PRINT and TRY/CATCH, so this
--   script uses none of them. Run STEP 0 first to see what already exists, then
--   run steps 1 to 7 ONE AT A TIME, top to bottom. Skip any step whose object
--   STEP 0 shows as already present. Finish with the VERIFY query.
--
--   Steps 5 and 6 reference the column added in step 4, so step 4 must be
--   executed and completed before them. Running one step at a time guarantees
--   that, because each execution is its own batch.
--
--   In SSMS or Azure Data Studio you can select and run the whole file at once.
--
-- AFTER RUNNING
--   Restart the application. ChartOfAccountsSeeder runs at startup and will, for
--   every company, create equity account 34100 "Owner's Current Account" with
--   IsOwnerAccount = 1 and seed nine starter categories (OW-10 Cash Drawings
--   through OW-99 Other Personal). All of them are editable from
--   Setup > Owner Withdrawal Categories. No manual data setup is needed.
--
-- WHY THIS IS HAND-WRITTEN AND NOT AN EF MIGRATION
--   Migrations/ApplicationDbContextModelSnapshot.cs is out of sync with the live
--   schema, because 20260422093000_AddFixedAssets and
--   20260601100000_AddOpeningBalanceFields were hand-written without Designer
--   files and never folded into the snapshot. "dotnet ef migrations add"
--   therefore diffs against a stale baseline and emits a migration that would
--   CREATE tables which already exist in production and ADD columns which
--   already exist. Do not run any writing "dotnet ef" command against this
--   database until the snapshot is rebaselined.
-- =============================================================================


-- STEP 0 -- Pre-check. Run this first and note which objects already exist.
SELECT
    'OwnerWithdrawalCategories table' AS ObjectName,
    CASE WHEN OBJECT_ID(N'[dbo].[OwnerWithdrawalCategories]', N'U') IS NULL THEN 'MISSING' ELSE 'EXISTS' END AS Status
UNION ALL SELECT 'FK_OwnerWithdrawalCategories_Companies_CompanyId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_OwnerWithdrawalCategories_Companies_CompanyId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'IX_OwnerWithdrawalCategories_CompanyId_Code',
    CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OwnerWithdrawalCategories_CompanyId_Code') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'JournalEntryLines.OwnerWithdrawalCategoryId column',
    CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[JournalEntryLines]') AND name = N'OwnerWithdrawalCategoryId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'IX_JournalEntryLines_OwnerWithdrawalCategoryId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntryLines_OwnerWithdrawalCategoryId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'FK_JournalEntryLines_OwnerWithdrawalCategories_OwnerWithdrawalCategoryId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_JournalEntryLines_OwnerWithdrawalCategories_OwnerWithdrawalCategoryId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'Accounts.IsOwnerAccount column',
    CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Accounts]') AND name = N'IsOwnerAccount') THEN 'EXISTS' ELSE 'MISSING' END;


-- STEP 1 -- Create the dimension table.
CREATE TABLE [dbo].[OwnerWithdrawalCategories]
(
    [Id]          INT            NOT NULL IDENTITY(1,1),
    [CompanyId]   INT            NOT NULL,
    [Code]        NVARCHAR(30)   NOT NULL,
    [Name]        NVARCHAR(150)  NOT NULL,
    [Description] NVARCHAR(500)  NULL,
    [SortOrder]   INT            NOT NULL CONSTRAINT [DF_OwnerWithdrawalCategories_SortOrder] DEFAULT (0),
    [IsActive]    BIT            NOT NULL CONSTRAINT [DF_OwnerWithdrawalCategories_IsActive]  DEFAULT (1),
    CONSTRAINT [PK_OwnerWithdrawalCategories] PRIMARY KEY CLUSTERED ([Id] ASC)
);


-- STEP 2 -- Scope categories to a company.
ALTER TABLE [dbo].[OwnerWithdrawalCategories] WITH CHECK
    ADD CONSTRAINT [FK_OwnerWithdrawalCategories_Companies_CompanyId]
    FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id]);


-- STEP 3 -- Category codes are unique within a company.
CREATE UNIQUE INDEX [IX_OwnerWithdrawalCategories_CompanyId_Code]
    ON [dbo].[OwnerWithdrawalCategories] ([CompanyId] ASC, [Code] ASC);


-- STEP 4 -- Add the dimension column to the journal line.
-- Nullable by design: only lines posted to an owner equity account carry a
-- category. Every existing row keeps NULL and is unaffected.
ALTER TABLE [dbo].[JournalEntryLines] ADD [OwnerWithdrawalCategoryId] INT NULL;


-- STEP 5 -- Index the new column. Requires step 4 to have completed.
CREATE INDEX [IX_JournalEntryLines_OwnerWithdrawalCategoryId]
    ON [dbo].[JournalEntryLines] ([OwnerWithdrawalCategoryId]);


-- STEP 6 -- Foreign key on the new column. Requires step 4 to have completed.
ALTER TABLE [dbo].[JournalEntryLines] WITH CHECK
    ADD CONSTRAINT [FK_JournalEntryLines_OwnerWithdrawalCategories_OwnerWithdrawalCategoryId]
    FOREIGN KEY ([OwnerWithdrawalCategoryId])
    REFERENCES [dbo].[OwnerWithdrawalCategories] ([Id]);


-- STEP 7 -- Flag column on the chart of accounts. Defaults to 0, so every
-- existing account is unchanged. The seeder sets it to 1 on account 34100.
ALTER TABLE [dbo].[Accounts]
    ADD [IsOwnerAccount] BIT NOT NULL
    CONSTRAINT [DF_Accounts_IsOwnerAccount] DEFAULT (0);


-- VERIFY -- Expected: all four YES.
SELECT
    CategoryTableExists = CASE WHEN OBJECT_ID(N'[dbo].[OwnerWithdrawalCategories]', N'U') IS NULL THEN 'NO' ELSE 'YES' END,
    LineColumnExists    = CASE WHEN EXISTS (SELECT 1 FROM sys.columns
                                            WHERE object_id = OBJECT_ID(N'[dbo].[JournalEntryLines]')
                                              AND name = N'OwnerWithdrawalCategoryId')
                               THEN 'YES' ELSE 'NO' END,
    AccountColumnExists = CASE WHEN EXISTS (SELECT 1 FROM sys.columns
                                            WHERE object_id = OBJECT_ID(N'[dbo].[Accounts]')
                                              AND name = N'IsOwnerAccount')
                               THEN 'YES' ELSE 'NO' END,
    ForeignKeyExists    = CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys
                                            WHERE name = N'FK_JournalEntryLines_OwnerWithdrawalCategories_OwnerWithdrawalCategoryId')
                               THEN 'YES' ELSE 'NO' END;


-- POST-RESTART CHECK -- after restarting the application these should return rows.
-- SELECT Code, Name, IsOwnerAccount FROM Accounts WHERE IsOwnerAccount = 1;
-- SELECT Code, Name, SortOrder, IsActive FROM OwnerWithdrawalCategories ORDER BY SortOrder;
