-- ============================================================================
-- 33_AddVendorClearingAndAdvances.sql
--
-- Vendor payments the way SAP handles them:
--   * A payment is made to a vendor and applied to one or more of the vendor's
--     open bills, in full or in part. Each application is a clearing document
--     with its own number (CLR-yyyy-nnnnnn); a bill paid in full shows the
--     clearing number that cleared it.
--   * An advance (down payment, SAP special G/L indicator A) is paid to the vendor
--     before any bill and kept on 11550 Advances to Suppliers, apart from Accounts
--     Payable, until it is applied to the vendor's bills (SAP F-54).
--
-- Adds:
--   VendorPayments   VendorId, IsAdvance, PaidFromAccountId, Reference, Notes,
--                    AdvanceApplied, ClearingNumber; VendorBillId becomes optional
--   VendorBills      ClearingNumber, ClearedDate
--   VendorClearings, VendorClearingLines
--   Accounts         11550 Advances to Suppliers for every company
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The vendor paid (older payments take it from their bill).
IF COL_LENGTH('VendorPayments', 'VendorId') IS NULL ALTER TABLE [VendorPayments] ADD [VendorId] int NULL CONSTRAINT [FK_VendorPayments_Vendors_VendorId] FOREIGN KEY REFERENCES [Vendors] ([Id]);


-- 2. A down payment (advance) rather than a payment of bills.
IF COL_LENGTH('VendorPayments', 'IsAdvance') IS NULL ALTER TABLE [VendorPayments] ADD [IsAdvance] bit NOT NULL CONSTRAINT [DF_VendorPayments_IsAdvance] DEFAULT 0;


-- 3. The bank or cash account paid from (empty: the company's default cash account).
IF COL_LENGTH('VendorPayments', 'PaidFromAccountId') IS NULL ALTER TABLE [VendorPayments] ADD [PaidFromAccountId] int NULL CONSTRAINT [FK_VendorPayments_Accounts_PaidFromAccountId] FOREIGN KEY REFERENCES [Accounts] ([Id]);


-- 4. Cheque or transfer reference.
IF COL_LENGTH('VendorPayments', 'Reference') IS NULL ALTER TABLE [VendorPayments] ADD [Reference] nvarchar(100) NULL;


-- 5. Notes.
IF COL_LENGTH('VendorPayments', 'Notes') IS NULL ALTER TABLE [VendorPayments] ADD [Notes] nvarchar(500) NULL;


-- 6. How much of an advance has been applied to bills.
IF COL_LENGTH('VendorPayments', 'AdvanceApplied') IS NULL ALTER TABLE [VendorPayments] ADD [AdvanceApplied] decimal(18,2) NOT NULL CONSTRAINT [DF_VendorPayments_AdvanceApplied] DEFAULT 0;


-- 7. The clearing document a payment created.
IF COL_LENGTH('VendorPayments', 'ClearingNumber') IS NULL ALTER TABLE [VendorPayments] ADD [ClearingNumber] nvarchar(30) NULL;


-- 8. An advance is paid before there is any bill.
IF COLUMNPROPERTY(OBJECT_ID('VendorPayments'), 'VendorBillId', 'AllowsNull') = 0 ALTER TABLE [VendorPayments] ALTER COLUMN [VendorBillId] int NULL;


-- 9. The clearing document that settled a bill in full.
IF COL_LENGTH('VendorBills', 'ClearingNumber') IS NULL ALTER TABLE [VendorBills] ADD [ClearingNumber] nvarchar(30) NULL;


-- 10. When the bill was cleared.
IF COL_LENGTH('VendorBills', 'ClearedDate') IS NULL ALTER TABLE [VendorBills] ADD [ClearedDate] datetime2 NULL;


-- 11. Clearing documents: a payment or an advance applied to a vendor's bills.
IF OBJECT_ID('VendorClearings', 'U') IS NULL CREATE TABLE [VendorClearings] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [ClearingNumber] nvarchar(30) NOT NULL,
    [ClearingDate] datetime2 NOT NULL,
    [VendorId] int NOT NULL,
    [ClearingType] nvarchar(20) NOT NULL,
    [VendorPaymentId] int NULL,
    [AdvancePaymentId] int NULL,
    [JournalEntryId] int NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [Notes] nvarchar(500) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_VendorClearings] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_VendorClearings_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_VendorClearings_Vendors_VendorId] FOREIGN KEY ([VendorId]) REFERENCES [Vendors] ([Id]),
    CONSTRAINT [FK_VendorClearings_VendorPayments_VendorPaymentId] FOREIGN KEY ([VendorPaymentId]) REFERENCES [VendorPayments] ([Id]),
    CONSTRAINT [FK_VendorClearings_VendorPayments_AdvancePaymentId] FOREIGN KEY ([AdvancePaymentId]) REFERENCES [VendorPayments] ([Id]),
    CONSTRAINT [FK_VendorClearings_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries] ([Id])
);


-- 12. One clearing number per company.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorClearings_CompanyId_ClearingNumber') CREATE UNIQUE INDEX [IX_VendorClearings_CompanyId_ClearingNumber] ON [VendorClearings] ([CompanyId], [ClearingNumber]);


-- 13. Clearings are looked up by payment.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorClearings_VendorPaymentId') CREATE INDEX [IX_VendorClearings_VendorPaymentId] ON [VendorClearings] ([VendorPaymentId]);


-- 14. The bills a clearing document settles, and how much of each.
IF OBJECT_ID('VendorClearingLines', 'U') IS NULL CREATE TABLE [VendorClearingLines] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [VendorClearingId] int NOT NULL,
    [VendorBillId] int NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_VendorClearingLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_VendorClearingLines_VendorClearings_VendorClearingId] FOREIGN KEY ([VendorClearingId]) REFERENCES [VendorClearings] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_VendorClearingLines_VendorBills_VendorBillId] FOREIGN KEY ([VendorBillId]) REFERENCES [VendorBills] ([Id])
);


-- 15. Clearing lines are looked up by bill.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorClearingLines_VendorBillId') CREATE INDEX [IX_VendorClearingLines_VendorBillId] ON [VendorClearingLines] ([VendorBillId]);


-- 16. The account down payments to suppliers sit on (SAP: special G/L reconciliation account).
INSERT INTO [Accounts] ([CompanyId], [Code], [Name], [AccountType], [ReportingGroup], [IsControlAccount], [IsOwnerAccount], [IsPettyCashAccount], [IsGroup], [IsPostable], [IsActive], [SortOrder], [OpeningBalance], [ParentAccountId])
SELECT c.[Id], N'11550', N'Advances to Suppliers', N'Asset', N'Assets', 0, 0, 0, 0, 1, 1, 11550, 0,
       (SELECT TOP 1 p.[Id] FROM [Accounts] p WHERE p.[CompanyId] = c.[Id] AND p.[Code] = N'10000')
FROM [Companies] c
WHERE EXISTS (SELECT 1 FROM [Accounts] a WHERE a.[CompanyId] = c.[Id])
  AND NOT EXISTS (SELECT 1 FROM [Accounts] a WHERE a.[CompanyId] = c.[Id] AND a.[Code] = N'11550');


-- 17. Check: expect one row showing how many companies have the advances account.
SELECT COUNT(*) AS [CompaniesWithSupplierAdvances] FROM [Accounts] WHERE [Code] = N'11550';
