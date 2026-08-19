-- =============================================================================
-- 03_Finish_Setup.sql
-- -----------------------------------------------------------------------------
-- Completes whatever is still missing from scripts 01 and 02.
--
-- USE THIS INSTEAD OF 01 AND 02 FROM NOW ON.
--
-- Both tables (PdcReceipts, OwnerWithdrawalCategories) already exist - the
-- "There is already an object named ..." messages simply mean those CREATE TABLE
-- steps succeeded on an earlier attempt. This script skips anything already
-- present and applies only what is left.
--
-- HOW TO RUN
--   1. Select the whole file and execute it.
--   2. If any statement reports an error, just RUN THE WHOLE FILE AGAIN.
--      Every statement is guarded, so re-running is harmless and each pass gets
--      further. Two passes is normally enough - the second pass is only needed
--      because an index or foreign key cannot be created in the same batch as
--      the column it references.
--   3. Run SECTION A on its own at the end. When every row says EXISTS you are
--      done.
--   4. Restart the application from your ControlPanel.
--
-- No block comments, no EXEC, no PRINT, no TRY/CATCH, no transactions - nothing
-- the site4now console rejects.
-- =============================================================================


-- =============================================================================
-- SECTION A -- STATUS. Safe to run on its own at any time.
-- =============================================================================
SELECT 'Accounts.IsOwnerAccount column' AS ObjectName,
    CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Accounts]') AND name = N'IsOwnerAccount') THEN 'EXISTS' ELSE 'MISSING' END AS Status
UNION ALL SELECT 'JournalEntryLines.OwnerWithdrawalCategoryId column',
    CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[JournalEntryLines]') AND name = N'OwnerWithdrawalCategoryId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'IX_JournalEntryLines_OwnerWithdrawalCategoryId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntryLines_OwnerWithdrawalCategoryId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'FK_JournalEntryLines_OwnerWithdrawalCategories_OwnerWithdrawalCategoryId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_JournalEntryLines_OwnerWithdrawalCategories_OwnerWithdrawalCategoryId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'FK_OwnerWithdrawalCategories_Companies_CompanyId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_OwnerWithdrawalCategories_Companies_CompanyId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'IX_OwnerWithdrawalCategories_CompanyId_Code',
    CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OwnerWithdrawalCategories_CompanyId_Code') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'IX_PdcReceipts_CompanyId_ChequeNumber',
    CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PdcReceipts_CompanyId_ChequeNumber') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'IX_PdcReceipts_LeaseId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PdcReceipts_LeaseId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'IX_PdcReceipts_TenantId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PdcReceipts_TenantId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'IX_PdcReceipts_CustomerInvoiceId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PdcReceipts_CustomerInvoiceId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'IX_PdcReceipts_MemorandumJournalEntryId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PdcReceipts_MemorandumJournalEntryId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'IX_PdcReceipts_ClearanceJournalEntryId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PdcReceipts_ClearanceJournalEntryId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'FK_PdcReceipts_Companies_CompanyId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PdcReceipts_Companies_CompanyId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'FK_PdcReceipts_Leases_LeaseId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PdcReceipts_Leases_LeaseId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'FK_PdcReceipts_Tenants_TenantId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PdcReceipts_Tenants_TenantId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'FK_PdcReceipts_CustomerInvoices_CustomerInvoiceId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PdcReceipts_CustomerInvoices_CustomerInvoiceId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'FK_PdcReceipts_JournalEntries_MemorandumJournalEntryId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PdcReceipts_JournalEntries_MemorandumJournalEntryId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'FK_PdcReceipts_JournalEntries_ClearanceJournalEntryId',
    CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PdcReceipts_JournalEntries_ClearanceJournalEntryId') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'Migration history row for AddPdcReceipts',
    CASE WHEN EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260421210256_AddPdcReceipts') THEN 'EXISTS' ELSE 'MISSING' END;


-- =============================================================================
-- SECTION B -- THE COLUMN THAT IS CRASHING THE APPLICATION.
-- This one matters most: without it every query against Accounts fails with
-- "Invalid column name 'IsOwnerAccount'", which is what produces HTTP 500.30.
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[Accounts]') AND name = N'IsOwnerAccount')
    ALTER TABLE [dbo].[Accounts]
        ADD [IsOwnerAccount] BIT NOT NULL
        CONSTRAINT [DF_Accounts_IsOwnerAccount] DEFAULT (0);


-- =============================================================================
-- SECTION C -- THE OWNER WITHDRAWAL DIMENSION COLUMN.
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[JournalEntryLines]') AND name = N'OwnerWithdrawalCategoryId')
    ALTER TABLE [dbo].[JournalEntryLines] ADD [OwnerWithdrawalCategoryId] INT NULL;


-- =============================================================================
-- SECTION D -- INDEX AND FOREIGN KEY ON THE COLUMN ADDED IN SECTION C.
-- If section C only just created the column, these two may report an error on
-- this pass. That is expected. Run the whole file a second time and they will
-- apply cleanly.
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntryLines_OwnerWithdrawalCategoryId')
    CREATE INDEX [IX_JournalEntryLines_OwnerWithdrawalCategoryId]
        ON [dbo].[JournalEntryLines] ([OwnerWithdrawalCategoryId]);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_JournalEntryLines_OwnerWithdrawalCategories_OwnerWithdrawalCategoryId')
    ALTER TABLE [dbo].[JournalEntryLines] WITH CHECK
        ADD CONSTRAINT [FK_JournalEntryLines_OwnerWithdrawalCategories_OwnerWithdrawalCategoryId]
        FOREIGN KEY ([OwnerWithdrawalCategoryId])
        REFERENCES [dbo].[OwnerWithdrawalCategories] ([Id]);


-- =============================================================================
-- SECTION E -- CONSTRAINTS ON THE CATEGORY TABLE (the table itself exists).
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_OwnerWithdrawalCategories_Companies_CompanyId')
    ALTER TABLE [dbo].[OwnerWithdrawalCategories] WITH CHECK
        ADD CONSTRAINT [FK_OwnerWithdrawalCategories_Companies_CompanyId]
        FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OwnerWithdrawalCategories_CompanyId_Code')
    CREATE UNIQUE INDEX [IX_OwnerWithdrawalCategories_CompanyId_Code]
        ON [dbo].[OwnerWithdrawalCategories] ([CompanyId] ASC, [Code] ASC);


-- =============================================================================
-- SECTION F -- INDEXES ON PdcReceipts (the table itself exists).
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PdcReceipts_CompanyId_ChequeNumber')
    CREATE UNIQUE INDEX [IX_PdcReceipts_CompanyId_ChequeNumber]
        ON [dbo].[PdcReceipts] ([CompanyId] ASC, [ChequeNumber] ASC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PdcReceipts_LeaseId')
    CREATE INDEX [IX_PdcReceipts_LeaseId] ON [dbo].[PdcReceipts] ([LeaseId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PdcReceipts_TenantId')
    CREATE INDEX [IX_PdcReceipts_TenantId] ON [dbo].[PdcReceipts] ([TenantId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PdcReceipts_CustomerInvoiceId')
    CREATE INDEX [IX_PdcReceipts_CustomerInvoiceId] ON [dbo].[PdcReceipts] ([CustomerInvoiceId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PdcReceipts_MemorandumJournalEntryId')
    CREATE INDEX [IX_PdcReceipts_MemorandumJournalEntryId] ON [dbo].[PdcReceipts] ([MemorandumJournalEntryId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PdcReceipts_ClearanceJournalEntryId')
    CREATE INDEX [IX_PdcReceipts_ClearanceJournalEntryId] ON [dbo].[PdcReceipts] ([ClearanceJournalEntryId]);


-- =============================================================================
-- SECTION G -- FOREIGN KEYS ON PdcReceipts.
-- All six are ON DELETE NO ACTION, matching EF Core's DeleteBehavior.Restrict.
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PdcReceipts_Companies_CompanyId')
    ALTER TABLE [dbo].[PdcReceipts] WITH CHECK
        ADD CONSTRAINT [FK_PdcReceipts_Companies_CompanyId]
        FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id]);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PdcReceipts_Leases_LeaseId')
    ALTER TABLE [dbo].[PdcReceipts] WITH CHECK
        ADD CONSTRAINT [FK_PdcReceipts_Leases_LeaseId]
        FOREIGN KEY ([LeaseId]) REFERENCES [dbo].[Leases] ([Id]);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PdcReceipts_Tenants_TenantId')
    ALTER TABLE [dbo].[PdcReceipts] WITH CHECK
        ADD CONSTRAINT [FK_PdcReceipts_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PdcReceipts_CustomerInvoices_CustomerInvoiceId')
    ALTER TABLE [dbo].[PdcReceipts] WITH CHECK
        ADD CONSTRAINT [FK_PdcReceipts_CustomerInvoices_CustomerInvoiceId]
        FOREIGN KEY ([CustomerInvoiceId]) REFERENCES [dbo].[CustomerInvoices] ([Id]);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PdcReceipts_JournalEntries_MemorandumJournalEntryId')
    ALTER TABLE [dbo].[PdcReceipts] WITH CHECK
        ADD CONSTRAINT [FK_PdcReceipts_JournalEntries_MemorandumJournalEntryId]
        FOREIGN KEY ([MemorandumJournalEntryId]) REFERENCES [dbo].[JournalEntries] ([Id]);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PdcReceipts_JournalEntries_ClearanceJournalEntryId')
    ALTER TABLE [dbo].[PdcReceipts] WITH CHECK
        ADD CONSTRAINT [FK_PdcReceipts_JournalEntries_ClearanceJournalEntryId]
        FOREIGN KEY ([ClearanceJournalEntryId]) REFERENCES [dbo].[JournalEntries] ([Id]);


-- =============================================================================
-- SECTION H -- RESTORE THE MIGRATION HISTORY ROW.
-- Reuses the ProductVersion already on the other rows, so no version literal is
-- hard-coded here.
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory]
               WHERE [MigrationId] = N'20260421210256_AddPdcReceipts')
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    SELECT N'20260421210256_AddPdcReceipts', MAX([ProductVersion])
    FROM [dbo].[__EFMigrationsHistory];


-- =============================================================================
-- FINISHED. Run SECTION A again. When every row says EXISTS, restart the
-- application from your ControlPanel.
--
-- After the restart, these should return rows:
--   SELECT Code, Name, IsOwnerAccount FROM Accounts WHERE IsOwnerAccount = 1;
--   SELECT Code, Name, SortOrder, IsActive FROM OwnerWithdrawalCategories ORDER BY SortOrder;
-- =============================================================================
