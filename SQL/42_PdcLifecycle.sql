-- ============================================================================
-- 42_PdcLifecycle.sql
--
-- The full life of a post-dated cheque: on hand, deposited to the bank for
-- collection, then cleared or bounced - or withdrawn by the tenant before it is
-- deposited, against a charge.
--
-- Adds:
--   Account 11450 Cheques Under Collection, under assets, for every company
--   whose chart has the 10000 assets heading, and its mapping (an
--   unmapped company falls back to account 11450).
--   CompanyAccountSettings.ChequesUnderCollectionAccountId
--   CompanyAccountSettings.PdcChargeIncomeAccountId   income for cheque charges
--   Companies.PdcWithdrawalCharge (default 500), Companies.PdcBounceCharge (0)
--   PdcReceipts: deposit, bounce and withdrawal dates and journals, the bounce
--   reason and the charge invoice raised to the tenant.
--   CustomerInvoices.ChargeType   null for rent; the kind of charge otherwise
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The cheques under collection account.
INSERT INTO [Accounts] ([CompanyId], [Code], [Name], [AccountType], [ReportingGroup], [IsControlAccount], [IsOwnerAccount], [IsPettyCashAccount], [IsGroup], [IsPostable], [IsActive], [SortOrder], [OpeningBalance], [ParentAccountId])
SELECT g.[CompanyId], N'11450', N'Cheques Under Collection', N'Asset', g.[ReportingGroup], 0, 0, 0, 0, 1, 1, 11450, 0, g.[Id]
FROM [Accounts] g
WHERE g.[Code] = N'10000'
  AND NOT EXISTS (SELECT 1 FROM [Accounts] a WHERE a.[CompanyId] = g.[CompanyId] AND a.[Code] = N'11450');


-- 2. Cheques under collection mapping.
IF COL_LENGTH('CompanyAccountSettings', 'ChequesUnderCollectionAccountId') IS NULL ALTER TABLE [CompanyAccountSettings] ADD [ChequesUnderCollectionAccountId] int NULL CONSTRAINT [FK_CompanyAccountSettings_Accounts_ChequesUnderCollectionAccountId] REFERENCES [Accounts]([Id]);


-- 3. Income account for cheque charges.
IF COL_LENGTH('CompanyAccountSettings', 'PdcChargeIncomeAccountId') IS NULL ALTER TABLE [CompanyAccountSettings] ADD [PdcChargeIncomeAccountId] int NULL CONSTRAINT [FK_CompanyAccountSettings_Accounts_PdcChargeIncomeAccountId] REFERENCES [Accounts]([Id]);


-- 4. Default withdrawal charge.
IF COL_LENGTH('Companies', 'PdcWithdrawalCharge') IS NULL ALTER TABLE [Companies] ADD [PdcWithdrawalCharge] decimal(18,2) NOT NULL CONSTRAINT [DF_Companies_PdcWithdrawalCharge] DEFAULT (500);


-- 5. Default bounce charge.
IF COL_LENGTH('Companies', 'PdcBounceCharge') IS NULL ALTER TABLE [Companies] ADD [PdcBounceCharge] decimal(18,2) NOT NULL CONSTRAINT [DF_Companies_PdcBounceCharge] DEFAULT (0);


-- 6. Date deposited.
IF COL_LENGTH('PdcReceipts', 'DepositedDate') IS NULL ALTER TABLE [PdcReceipts] ADD [DepositedDate] datetime2 NULL;


-- 7. Deposit journal.
IF COL_LENGTH('PdcReceipts', 'DepositJournalEntryId') IS NULL ALTER TABLE [PdcReceipts] ADD [DepositJournalEntryId] int NULL CONSTRAINT [FK_PdcReceipts_JournalEntries_DepositJournalEntryId] REFERENCES [JournalEntries]([Id]);


-- 8. Date bounced.
IF COL_LENGTH('PdcReceipts', 'BouncedDate') IS NULL ALTER TABLE [PdcReceipts] ADD [BouncedDate] datetime2 NULL;


-- 9. Bounce reason.
IF COL_LENGTH('PdcReceipts', 'BounceReason') IS NULL ALTER TABLE [PdcReceipts] ADD [BounceReason] nvarchar(300) NULL;


-- 10. Bounce journal.
IF COL_LENGTH('PdcReceipts', 'BounceJournalEntryId') IS NULL ALTER TABLE [PdcReceipts] ADD [BounceJournalEntryId] int NULL CONSTRAINT [FK_PdcReceipts_JournalEntries_BounceJournalEntryId] REFERENCES [JournalEntries]([Id]);


-- 11. Date withdrawn.
IF COL_LENGTH('PdcReceipts', 'WithdrawnDate') IS NULL ALTER TABLE [PdcReceipts] ADD [WithdrawnDate] datetime2 NULL;


-- 12. Withdrawal journal.
IF COL_LENGTH('PdcReceipts', 'WithdrawalJournalEntryId') IS NULL ALTER TABLE [PdcReceipts] ADD [WithdrawalJournalEntryId] int NULL CONSTRAINT [FK_PdcReceipts_JournalEntries_WithdrawalJournalEntryId] REFERENCES [JournalEntries]([Id]);


-- 13. Charge invoice to the tenant.
IF COL_LENGTH('PdcReceipts', 'ChargeInvoiceId') IS NULL ALTER TABLE [PdcReceipts] ADD [ChargeInvoiceId] int NULL CONSTRAINT [FK_PdcReceipts_CustomerInvoices_ChargeInvoiceId] REFERENCES [CustomerInvoices]([Id]);


-- 14. The kind of charge on a charge invoice (null for rent).
IF COL_LENGTH('CustomerInvoices', 'ChargeType') IS NULL ALTER TABLE [CustomerInvoices] ADD [ChargeType] nvarchar(60) NULL;
