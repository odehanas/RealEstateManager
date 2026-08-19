-- ============================================================================
-- 11_AddVendorBillLines.sql
--
-- Multi-line vendor bills, Odoo style. Turns the single-amount vendor bill into
-- a proper purchase document: a header with the supplier and the dates, and a
-- grid of lines each with its own account, property, quantity, price and tax.
--
-- Adds:
--   Vendors.IsVatRegistered        bit           - live registration switch
--   VendorBills.VendorReference    nvarchar(100) - the supplier's own bill number
--   VendorBills.AccountingDate     datetime2     - period the journal books to
--   VendorBills.Notes              nvarchar(1000)
--   VendorBills.VendorIsVatRegistered bit        - snapshot at entry time
--   VendorBillLines                new table
--
-- Then back-fills, so every bill already in the system keeps working:
--   - AccountingDate  = BillDate
--   - VendorReference = the number that was typed into BillNumber
--   - IsVatRegistered = 1 for any vendor that already has a TRN on file
--   - one line per existing bill, carrying the whole amount
--
-- HOW TO RUN in site4now SQL Studio: run ONE numbered statement at a time, top
-- to bottom. The console executes only the first batch, so pasting the whole
-- file runs statement 1 and nothing else.
--
-- Run every statement BEFORE deploying the new build. The application reads
-- these columns and will fail without them.
-- ============================================================================


-- 1. Live VAT registration flag on the supplier.
ALTER TABLE [Vendors] ADD [IsVatRegistered] bit NOT NULL CONSTRAINT [DF_Vendors_IsVatRegistered] DEFAULT (0);


-- 2. The supplier's own document number, kept apart from our internal bill number.
ALTER TABLE [VendorBills] ADD [VendorReference] nvarchar(100) NULL;


-- 3. The date the journal books to. Defaults to the epoch for existing rows;
--    statement 7 corrects them to the bill date.
ALTER TABLE [VendorBills] ADD [AccountingDate] datetime2 NOT NULL CONSTRAINT [DF_VendorBills_AccountingDate] DEFAULT ('1900-01-01');


-- 4. Free-text narration on the document.
ALTER TABLE [VendorBills] ADD [Notes] nvarchar(1000) NULL;


-- 5. Whether the supplier was VAT registered when the bill was entered. Snapshotted
--    so deregistering a supplier later never rewrites a document already booked.
ALTER TABLE [VendorBills] ADD [VendorIsVatRegistered] bit NOT NULL CONSTRAINT [DF_VendorBills_VendorIsVatRegistered] DEFAULT (0);


-- 6. Any supplier that already has a TRN on file is treated as registered. Review
--    this afterwards and untick anyone whose registration has since been cancelled.
UPDATE [Vendors] SET [IsVatRegistered] = 1 WHERE [TrnNumber] IS NOT NULL AND LTRIM(RTRIM([TrnNumber])) <> '';


-- 7. Existing bills book to their own bill date, and the number that was typed in
--    was always the supplier's, so it becomes the supplier reference.
UPDATE [VendorBills]
SET [AccountingDate] = [BillDate],
    [VendorReference] = [BillNumber],
    [VendorIsVatRegistered] = CASE WHEN [VatAmount] > 0 THEN 1 ELSE 0 END;


-- 8. The lines table.
CREATE TABLE [VendorBillLines] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [VendorBillId] int NOT NULL,
    [LineNumber] int NOT NULL,
    [Description] nvarchar(300) NOT NULL,
    [AccountId] int NULL,
    [PropertyId] int NULL,
    [UnitId] int NULL,
    [Quantity] decimal(18,4) NOT NULL,
    [UnitPrice] decimal(18,4) NOT NULL,
    [LineSubtotal] decimal(18,2) NOT NULL,
    [IsTaxable] bit NOT NULL,
    [VatRate] decimal(9,4) NOT NULL,
    [VatAmount] decimal(18,2) NOT NULL,
    [InputTaxAttribution] nvarchar(20) NOT NULL,
    [RecoverableVatAmount] decimal(18,2) NOT NULL,
    [NonRecoverableVatAmount] decimal(18,2) NOT NULL,
    [AppliedRecoveryRate] decimal(9,4) NOT NULL,
    [LineTotal] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_VendorBillLines] PRIMARY KEY ([Id])
);


-- 9. A line has no meaning apart from its bill, so it is deleted with it.
ALTER TABLE [VendorBillLines] ADD CONSTRAINT [FK_VendorBillLines_VendorBills]
    FOREIGN KEY ([VendorBillId]) REFERENCES [VendorBills]([Id]) ON DELETE CASCADE;


-- 10. The account the line is charged to.
ALTER TABLE [VendorBillLines] ADD CONSTRAINT [FK_VendorBillLines_Accounts]
    FOREIGN KEY ([AccountId]) REFERENCES [Accounts]([Id]);


-- 11. Analytic dimension - lets one bill be split across properties.
ALTER TABLE [VendorBillLines] ADD CONSTRAINT [FK_VendorBillLines_Properties]
    FOREIGN KEY ([PropertyId]) REFERENCES [Properties]([Id]);


-- 12. Optional unit-level dimension.
ALTER TABLE [VendorBillLines] ADD CONSTRAINT [FK_VendorBillLines_Units]
    FOREIGN KEY ([UnitId]) REFERENCES [Units]([Id]);


-- 13. Lookup index for loading a bill's lines in order.
CREATE INDEX [IX_VendorBillLines_VendorBillId_LineNumber]
    ON [VendorBillLines] ([VendorBillId], [LineNumber]);


-- 14. Back-fill one line per existing bill, carrying the whole amount.
--     The account is taken from the expense debit already in the bill's journal
--     where there is one; otherwise it falls back to the company's default direct
--     expense account. If neither exists the line is created with no account and
--     the bill will ask for one before it can be posted.
INSERT INTO [VendorBillLines] (
    [VendorBillId], [LineNumber], [Description], [AccountId], [PropertyId], [UnitId],
    [Quantity], [UnitPrice], [LineSubtotal], [IsTaxable], [VatRate], [VatAmount],
    [InputTaxAttribution], [RecoverableVatAmount], [NonRecoverableVatAmount],
    [AppliedRecoveryRate], [LineTotal])
SELECT
    b.[Id],
    1,
    CASE WHEN LTRIM(RTRIM(ISNULL(b.[ExpenseCategory], ''))) = ''
         THEN 'Bill total (migrated)'
         ELSE b.[ExpenseCategory] END,
    COALESCE(
        (SELECT TOP 1 jl.[AccountId]
         FROM [JournalEntryLines] jl
         INNER JOIN [Accounts] a ON a.[Id] = jl.[AccountId]
         WHERE jl.[VendorBillId] = b.[Id] AND jl.[Debit] > 0 AND a.[AccountType] = 'Expense'
         ORDER BY jl.[Debit] DESC),
        (SELECT TOP 1 s.[DirectExpenseDefaultAccountId]
         FROM [CompanyAccountSettings] s
         WHERE s.[CompanyId] = b.[CompanyId])),
    b.[PropertyId],
    NULL,
    1,
    b.[Subtotal],
    b.[Subtotal],
    CASE WHEN b.[VatAmount] > 0 THEN 1 ELSE 0 END,
    CASE WHEN b.[VatAmount] > 0 THEN 5 ELSE 0 END,
    b.[VatAmount],
    ISNULL(b.[InputTaxAttribution], 'Taxable'),
    ISNULL(b.[RecoverableVatAmount], 0),
    ISNULL(b.[NonRecoverableVatAmount], 0),
    ISNULL(b.[AppliedRecoveryRate], 100),
    b.[Subtotal] + b.[VatAmount]
FROM [VendorBills] b
WHERE NOT EXISTS (SELECT 1 FROM [VendorBillLines] l WHERE l.[VendorBillId] = b.[Id]);


-- 15. VERIFY - every bill should now have at least one line, and the line totals
--     should equal the header totals. Expect zero rows back; anything returned is
--     a bill whose lines do not add up to its header and needs a look.
SELECT b.[Id], b.[BillNumber], b.[Subtotal] AS HeaderNet, b.[VatAmount] AS HeaderVat,
       ISNULL(SUM(l.[LineSubtotal]), 0) AS LinesNet,
       ISNULL(SUM(l.[VatAmount]), 0) AS LinesVat,
       COUNT(l.[Id]) AS LineCount
FROM [VendorBills] b
LEFT JOIN [VendorBillLines] l ON l.[VendorBillId] = b.[Id]
GROUP BY b.[Id], b.[BillNumber], b.[Subtotal], b.[VatAmount]
HAVING COUNT(l.[Id]) = 0
    OR ISNULL(SUM(l.[LineSubtotal]), 0) <> b.[Subtotal]
    OR ISNULL(SUM(l.[VatAmount]), 0) <> b.[VatAmount];


-- 16. VERIFY - lines that ended up with no account. Each one needs an account
--     chosen on the bill before that bill can be posted again. Existing posted
--     bills are unaffected: their journals are already in the accounts.
SELECT b.[BillNumber], b.[BillDate], b.[TotalAmount], b.[Status]
FROM [VendorBillLines] l
INNER JOIN [VendorBills] b ON b.[Id] = l.[VendorBillId]
WHERE l.[AccountId] IS NULL
ORDER BY b.[BillDate];
