-- =============================================================================
-- 01_Restore_PdcReceipts.sql
-- -----------------------------------------------------------------------------
-- Recreates the [PdcReceipts] table dropped from the live database on
-- 2026-08-12 at approximately 10:22 by an erroneous
-- "dotnet ef migrations remove --force" command, and restores its
-- __EFMigrationsHistory row.
--
-- READ FIRST
--   This restores the TABLE STRUCTURE ONLY. It does NOT restore rows.
--   If the PDC register held data you need, do a point-in-time BACKUP RESTORE
--   to just before 10:22 on 2026-08-12 instead. Do that first if the data
--   matters, because backup retention windows expire.
--
-- NOT AFFECTED
--   JournalEntries and JournalEntryLines were untouched. PDC memorandum and
--   clearance journals still exist, so the general ledger, Trial Balance and
--   Balance Sheet are unchanged. Only the PDC sub-ledger rows were lost.
--
-- SOURCE OF TRUTH
--   Reconstructed from Domain/PdcReceipt.cs and the Fluent API configuration in
--   Data/ApplicationDbContext.cs: max lengths lines 491-513, unique index
--   line 715, foreign keys lines 1004-1038.
--
-- HOW TO RUN IN A RESTRICTED WEB CONSOLE (site4now SQL Studio)
--   That console rejects block comments, EXEC, PRINT and TRY/CATCH, so this
--   script uses none of them. Run STEP 0 first to see what already exists, then
--   run steps 1 to 14 ONE AT A TIME, top to bottom. Skip any step whose object
--   STEP 0 shows as already present. Finish with the VERIFY query.
--
--   In SSMS or Azure Data Studio you can select and run the whole file at once.
-- =============================================================================


-- STEP 0 -- Pre-check. Run this first and note which objects already exist.
SELECT
    'PdcReceipts table' AS ObjectName,
    CASE WHEN OBJECT_ID(N'[dbo].[PdcReceipts]', N'U') IS NULL THEN 'MISSING' ELSE 'EXISTS' END AS Status
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
UNION ALL SELECT 'Migration history row',
    CASE WHEN EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260421210256_AddPdcReceipts') THEN 'EXISTS' ELSE 'MISSING' END;


-- STEP 1 -- Create the table.
CREATE TABLE [dbo].[PdcReceipts]
(
    [Id]                        INT             NOT NULL IDENTITY(1,1),
    [CompanyId]                 INT             NOT NULL,
    [LeaseId]                   INT             NOT NULL,
    [TenantId]                  INT             NULL,
    [CustomerInvoiceId]         INT             NULL,
    [ChequeNumber]              NVARCHAR(100)   NOT NULL,
    [BankName]                  NVARCHAR(200)   NULL,
    [ChequeDate]                DATETIME2       NOT NULL,
    [ReceivedDate]              DATETIME2       NOT NULL,
    [ClearedDate]               DATETIME2       NULL,
    [Amount]                    DECIMAL(18,2)   NOT NULL,
    [SettlementMode]            NVARCHAR(30)    NOT NULL,
    [Status]                    NVARCHAR(30)    NOT NULL,
    [Notes]                     NVARCHAR(1000)  NULL,
    [MemorandumJournalEntryId]  INT             NULL,
    [ClearanceJournalEntryId]   INT             NULL,
    CONSTRAINT [PK_PdcReceipts] PRIMARY KEY CLUSTERED ([Id] ASC)
);


-- STEP 2 -- Unique index on company + cheque number. This is what enforces the
-- "Cheque number already exists" rule in the application.
CREATE UNIQUE INDEX [IX_PdcReceipts_CompanyId_ChequeNumber]
    ON [dbo].[PdcReceipts] ([CompanyId] ASC, [ChequeNumber] ASC);


-- STEP 3 -- Foreign key index: lease.
CREATE INDEX [IX_PdcReceipts_LeaseId] ON [dbo].[PdcReceipts] ([LeaseId]);


-- STEP 4 -- Foreign key index: tenant.
CREATE INDEX [IX_PdcReceipts_TenantId] ON [dbo].[PdcReceipts] ([TenantId]);


-- STEP 5 -- Foreign key index: customer invoice.
CREATE INDEX [IX_PdcReceipts_CustomerInvoiceId] ON [dbo].[PdcReceipts] ([CustomerInvoiceId]);


-- STEP 6 -- Foreign key index: memorandum journal entry.
CREATE INDEX [IX_PdcReceipts_MemorandumJournalEntryId] ON [dbo].[PdcReceipts] ([MemorandumJournalEntryId]);


-- STEP 7 -- Foreign key index: clearance journal entry.
CREATE INDEX [IX_PdcReceipts_ClearanceJournalEntryId] ON [dbo].[PdcReceipts] ([ClearanceJournalEntryId]);


-- STEP 8 -- Foreign key to Companies. All six FKs are ON DELETE NO ACTION,
-- which is what EF Core's DeleteBehavior.Restrict maps to.
ALTER TABLE [dbo].[PdcReceipts] WITH CHECK
    ADD CONSTRAINT [FK_PdcReceipts_Companies_CompanyId]
    FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id]);


-- STEP 9 -- Foreign key to Leases.
ALTER TABLE [dbo].[PdcReceipts] WITH CHECK
    ADD CONSTRAINT [FK_PdcReceipts_Leases_LeaseId]
    FOREIGN KEY ([LeaseId]) REFERENCES [dbo].[Leases] ([Id]);


-- STEP 10 -- Foreign key to Tenants.
ALTER TABLE [dbo].[PdcReceipts] WITH CHECK
    ADD CONSTRAINT [FK_PdcReceipts_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);


-- STEP 11 -- Foreign key to CustomerInvoices.
ALTER TABLE [dbo].[PdcReceipts] WITH CHECK
    ADD CONSTRAINT [FK_PdcReceipts_CustomerInvoices_CustomerInvoiceId]
    FOREIGN KEY ([CustomerInvoiceId]) REFERENCES [dbo].[CustomerInvoices] ([Id]);


-- STEP 12 -- Foreign key to JournalEntries for the memorandum posting.
ALTER TABLE [dbo].[PdcReceipts] WITH CHECK
    ADD CONSTRAINT [FK_PdcReceipts_JournalEntries_MemorandumJournalEntryId]
    FOREIGN KEY ([MemorandumJournalEntryId]) REFERENCES [dbo].[JournalEntries] ([Id]);


-- STEP 13 -- Foreign key to JournalEntries for the clearance posting.
ALTER TABLE [dbo].[PdcReceipts] WITH CHECK
    ADD CONSTRAINT [FK_PdcReceipts_JournalEntries_ClearanceJournalEntryId]
    FOREIGN KEY ([ClearanceJournalEntryId]) REFERENCES [dbo].[JournalEntries] ([Id]);


-- STEP 14 -- Restore the migration history row so EF stops reporting
-- AddPdcReceipts as pending. Reuses the ProductVersion already recorded on the
-- other rows, so no version literal is hard-coded here.
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
SELECT N'20260421210256_AddPdcReceipts', MAX([ProductVersion])
FROM [dbo].[__EFMigrationsHistory];


-- VERIFY -- Expected: TableExists YES, ColumnCount 16, IndexCount 6,
-- ForeignKeyCount 6, HistoryRestored YES. RowCount_ will be 0 unless you
-- restored from a backup.
SELECT
    TableExists     = CASE WHEN OBJECT_ID(N'[dbo].[PdcReceipts]', N'U') IS NULL THEN 'NO' ELSE 'YES' END,
    ColumnCount     = (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[PdcReceipts]')),
    IndexCount      = (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[PdcReceipts]') AND is_primary_key = 0),
    ForeignKeyCount = (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'[dbo].[PdcReceipts]')),
    RowCount_       = (SELECT COUNT(*) FROM [dbo].[PdcReceipts]),
    HistoryRestored = CASE WHEN EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory]
                                        WHERE [MigrationId] = N'20260421210256_AddPdcReceipts')
                           THEN 'YES' ELSE 'NO' END;
