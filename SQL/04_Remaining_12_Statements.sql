-- =============================================================================
-- 04_Remaining_12_Statements.sql
-- -----------------------------------------------------------------------------
-- The 12 objects still reported MISSING by Section A of 03_Finish_Setup.sql.
--
-- IMPORTANT - HOW TO RUN
--   The site4now console executes only the FIRST batch of whatever you paste or
--   select. That is why previous runs applied one thing and then stopped.
--
--   So: paste ONE numbered statement, press Run, clear the editor, paste the
--   next. Twelve times. Each is a single statement with no GO and no guard.
--
--   None of these will error, because Section A has confirmed all twelve are
--   absent. If one does error with "already exists", it simply means it landed
--   on an earlier attempt - skip it and continue.
--
--   Both columns (Accounts.IsOwnerAccount and
--   JournalEntryLines.OwnerWithdrawalCategoryId) already EXIST, so statements 1
--   and 2 below can be created safely - the column they index is already there.
--
-- WHEN FINISHED
--   Re-run Section A of 03_Finish_Setup.sql. All 19 rows should read EXISTS.
-- =============================================================================


-- 1 --------------------------------------------------------------------------
CREATE INDEX [IX_JournalEntryLines_OwnerWithdrawalCategoryId] ON [dbo].[JournalEntryLines] ([OwnerWithdrawalCategoryId]);


-- 2 --------------------------------------------------------------------------
ALTER TABLE [dbo].[JournalEntryLines] WITH CHECK ADD CONSTRAINT [FK_JournalEntryLines_OwnerWithdrawalCategories_OwnerWithdrawalCategoryId] FOREIGN KEY ([OwnerWithdrawalCategoryId]) REFERENCES [dbo].[OwnerWithdrawalCategories] ([Id]);


-- 3 --------------------------------------------------------------------------
CREATE INDEX [IX_PdcReceipts_MemorandumJournalEntryId] ON [dbo].[PdcReceipts] ([MemorandumJournalEntryId]);


-- 4 --------------------------------------------------------------------------
CREATE INDEX [IX_PdcReceipts_ClearanceJournalEntryId] ON [dbo].[PdcReceipts] ([ClearanceJournalEntryId]);


-- 5 --------------------------------------------------------------------------
ALTER TABLE [dbo].[PdcReceipts] WITH CHECK ADD CONSTRAINT [FK_PdcReceipts_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id]);


-- 6 --------------------------------------------------------------------------
ALTER TABLE [dbo].[PdcReceipts] WITH CHECK ADD CONSTRAINT [FK_PdcReceipts_Leases_LeaseId] FOREIGN KEY ([LeaseId]) REFERENCES [dbo].[Leases] ([Id]);


-- 7 --------------------------------------------------------------------------
ALTER TABLE [dbo].[PdcReceipts] WITH CHECK ADD CONSTRAINT [FK_PdcReceipts_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);


-- 8 --------------------------------------------------------------------------
ALTER TABLE [dbo].[PdcReceipts] WITH CHECK ADD CONSTRAINT [FK_PdcReceipts_CustomerInvoices_CustomerInvoiceId] FOREIGN KEY ([CustomerInvoiceId]) REFERENCES [dbo].[CustomerInvoices] ([Id]);


-- 9 --------------------------------------------------------------------------
ALTER TABLE [dbo].[PdcReceipts] WITH CHECK ADD CONSTRAINT [FK_PdcReceipts_JournalEntries_MemorandumJournalEntryId] FOREIGN KEY ([MemorandumJournalEntryId]) REFERENCES [dbo].[JournalEntries] ([Id]);


-- 10 -------------------------------------------------------------------------
ALTER TABLE [dbo].[PdcReceipts] WITH CHECK ADD CONSTRAINT [FK_PdcReceipts_JournalEntries_ClearanceJournalEntryId] FOREIGN KEY ([ClearanceJournalEntryId]) REFERENCES [dbo].[JournalEntries] ([Id]);


-- 11 -------------------------------------------------------------------------
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) SELECT N'20260421210256_AddPdcReceipts', MAX([ProductVersion]) FROM [dbo].[__EFMigrationsHistory];


-- 12 -- Confirmation. Expected: 16 columns, 6 indexes, 6 foreign keys, 0 rows.
SELECT ColumnCount = (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[PdcReceipts]')), IndexCount = (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[PdcReceipts]') AND is_primary_key = 0), ForeignKeyCount = (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'[dbo].[PdcReceipts]')), PdcRowCount = (SELECT COUNT(*) FROM [dbo].[PdcReceipts]);
