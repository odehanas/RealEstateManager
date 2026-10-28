-- ============================================================================
-- 40_OwnerRecharges.sql
--
-- Costs the company incurs for an owner's managed property and charges on to the
-- owner (supplier bill lines, petty cash, maintenance store items), as the
-- company's own standard-rated supply with 5% VAT and a tax invoice.
--
-- Adds:
--   VendorBillLines.RechargeToOwner   the line is charged on to the owner
--   OwnerRecharges                    each recharge, its VAT and tax invoice number
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. A bill line charged on to the owner.
IF COL_LENGTH('VendorBillLines', 'RechargeToOwner') IS NULL ALTER TABLE [VendorBillLines] ADD [RechargeToOwner] bit NOT NULL CONSTRAINT [DF_VendorBillLines_RechargeToOwner] DEFAULT (0);


-- 2. The recharges.
IF OBJECT_ID('OwnerRecharges', 'U') IS NULL CREATE TABLE [OwnerRecharges] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [SourceType] nvarchar(30) NOT NULL,
    [SourceId] int NOT NULL,
    [SourceReference] nvarchar(100) NULL,
    [PropertyId] int NOT NULL,
    [UnitId] int NULL,
    [OwnerName] nvarchar(200) NOT NULL,
    [OwnerTrn] nvarchar(20) NULL,
    [RechargeDate] datetime2 NOT NULL,
    [Description] nvarchar(500) NOT NULL,
    [NetAmount] decimal(18,2) NOT NULL,
    [VatRate] decimal(5,2) NOT NULL,
    [VatAmount] decimal(18,2) NOT NULL,
    [TotalAmount] decimal(18,2) NOT NULL,
    [TaxInvoiceNumber] nvarchar(30) NULL,
    [JournalEntryId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_OwnerRecharges] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OwnerRecharges_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_OwnerRecharges_Properties_PropertyId] FOREIGN KEY ([PropertyId]) REFERENCES [Properties] ([Id]),
    CONSTRAINT [FK_OwnerRecharges_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries] ([Id])
);


-- 3. One recharge per source document and property.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OwnerRecharges_Source' AND object_id = OBJECT_ID('OwnerRecharges')) CREATE UNIQUE INDEX [IX_OwnerRecharges_Source] ON [OwnerRecharges] ([CompanyId], [SourceType], [SourceId], [PropertyId]);
