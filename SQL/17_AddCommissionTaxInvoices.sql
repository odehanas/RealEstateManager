-- ============================================================================
-- 17_AddCommissionTaxInvoices.sql
--
-- VAT on our management commission to the landlords of managed properties.
-- The commission is a standard-rated service (5%) even when the rent is exempt.
-- One commission tax invoice is issued to each landlord at every month end.
--
-- Adds:
--   Properties.OwnerTrn                 the landlord's TRN, printed on our tax invoice
--   OwnerSettlements.CommissionVat      the VAT on commission shown on a settlement
--   OwnerCommissionInvoices             the monthly commission tax invoices
--   OwnerCommissionInvoiceLines         one line per contract per month
--
-- Nothing existing is changed. No VAT is charged on past months until you
-- issue the commission invoices for them from Operations > Owner Settlements.
--
-- RUN 16_AddOwnerSettlements.sql FIRST if you have not run it yet.
--
-- HOW TO RUN in site4now SQL Studio: run ONE numbered statement at a time, top
-- to bottom. The console executes only the first batch, so pasting the whole
-- file runs statement 1 and nothing else.
--
-- Run statements 1 to 8 BEFORE deploying the new build.
-- ============================================================================


-- 1. The landlord's TRN on each property. Empty for every existing property.
ALTER TABLE [Properties] ADD [OwnerTrn] nvarchar(20) NULL;


-- 2. VAT on commission on each settlement. Zero for settlements already made.
ALTER TABLE [OwnerSettlements] ADD [CommissionVat] decimal(18,2) NOT NULL CONSTRAINT [DF_OwnerSettlements_CommissionVat] DEFAULT (0);


-- 3. The commission tax invoices.
CREATE TABLE [OwnerCommissionInvoices] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [InvoiceNumber] nvarchar(30) NOT NULL,
    [TaxInvoiceNumber] nvarchar(30) NULL,
    [OwnerName] nvarchar(200) NOT NULL,
    [RecipientTrn] nvarchar(20) NULL,
    [InvoiceDate] datetime2 NOT NULL,
    [PeriodStart] datetime2 NOT NULL,
    [PeriodEnd] datetime2 NOT NULL,
    [Commission] decimal(18,2) NOT NULL,
    [VatRate] decimal(9,4) NOT NULL,
    [VatAmount] decimal(18,2) NOT NULL,
    [TotalAmount] decimal(18,2) NOT NULL,
    [JournalEntryId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_OwnerCommissionInvoices] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OwnerCommissionInvoices_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_OwnerCommissionInvoices_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries] ([Id])
);


-- 4. Their lines: one month's commission on one contract.
CREATE TABLE [OwnerCommissionInvoiceLines] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [OwnerCommissionInvoiceId] int NOT NULL,
    [RevenueRecognitionScheduleId] int NOT NULL,
    [PropertyId] int NOT NULL,
    [LeaseId] int NOT NULL,
    [RecognitionDate] datetime2 NOT NULL,
    [Description] nvarchar(300) NOT NULL,
    [Commission] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_OwnerCommissionInvoiceLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OwnerCommissionInvoiceLines_OwnerCommissionInvoices_OwnerCommissionInvoiceId] FOREIGN KEY ([OwnerCommissionInvoiceId]) REFERENCES [OwnerCommissionInvoices] ([Id]) ON DELETE CASCADE
);


-- 5. One invoice number per company.
CREATE UNIQUE INDEX [IX_OwnerCommissionInvoices_CompanyId_InvoiceNumber] ON [OwnerCommissionInvoices] ([CompanyId], [InvoiceNumber]);


-- 6. A month's commission on a contract can be invoiced only once.
CREATE UNIQUE INDEX [IX_OwnerCommissionInvoiceLines_RevenueRecognitionScheduleId] ON [OwnerCommissionInvoiceLines] ([RevenueRecognitionScheduleId]);


-- 7. Finding an owner's commission invoices.
CREATE INDEX [IX_OwnerCommissionInvoices_CompanyId_OwnerName] ON [OwnerCommissionInvoices] ([CompanyId], [OwnerName]);


-- 8. Check: both tables are there and empty. Expect one row showing 0 and 0.
SELECT (SELECT COUNT(*) FROM [OwnerCommissionInvoices]) AS [Invoices], (SELECT COUNT(*) FROM [OwnerCommissionInvoiceLines]) AS [Lines];
