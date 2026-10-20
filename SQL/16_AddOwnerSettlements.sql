-- ============================================================================
-- 16_AddOwnerSettlements.sql
--
-- Owner settlements: paying the landlords of managed properties the rent we
-- collected for them, less our commission, in full or in part.
--
-- Adds one new table, OwnerSettlements. Nothing existing is changed.
-- The Vendor Statement needs no database change.
--
-- HOW TO RUN in site4now SQL Studio: run ONE numbered statement at a time, top
-- to bottom. The console executes only the first batch, so pasting the whole
-- file runs statement 1 and nothing else.
--
-- Run statements 1 to 4 BEFORE deploying the new build. The Owner Settlements
-- screen reads this table and will error without it.
-- ============================================================================


-- 1. The settlements.
CREATE TABLE [OwnerSettlements] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [SettlementNumber] nvarchar(30) NOT NULL,
    [OwnerName] nvarchar(200) NOT NULL,
    [SettlementDate] datetime2 NOT NULL,
    [AsOfDate] datetime2 NOT NULL,
    [GrossRent] decimal(18,2) NOT NULL,
    [Commission] decimal(18,2) NOT NULL,
    [OwnerShare] decimal(18,2) NOT NULL,
    [CollectedOwnerShare] decimal(18,2) NOT NULL,
    [PreviouslySettled] decimal(18,2) NOT NULL,
    [AmountPaid] decimal(18,2) NOT NULL,
    [PaymentMethod] nvarchar(50) NOT NULL,
    [PaidFromAccountId] int NOT NULL,
    [ReferenceNumber] nvarchar(100) NULL,
    [Notes] nvarchar(1000) NULL,
    [JournalEntryId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedByAppUserId] int NULL,
    [CreatedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_OwnerSettlements] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OwnerSettlements_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_OwnerSettlements_Accounts_PaidFromAccountId] FOREIGN KEY ([PaidFromAccountId]) REFERENCES [Accounts] ([Id]),
    CONSTRAINT [FK_OwnerSettlements_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries] ([Id])
);


-- 2. One settlement number per company.
CREATE UNIQUE INDEX [IX_OwnerSettlements_CompanyId_SettlementNumber] ON [OwnerSettlements] ([CompanyId], [SettlementNumber]);


-- 3. Finding an owner's settlements.
CREATE INDEX [IX_OwnerSettlements_CompanyId_OwnerName] ON [OwnerSettlements] ([CompanyId], [OwnerName]);


-- 4. Check: the table is there and empty. Expect one row showing 0.
SELECT COUNT(*) AS [OwnerSettlements] FROM [OwnerSettlements];
