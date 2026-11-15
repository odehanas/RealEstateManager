-- ============================================================================
-- 58_CustomerSettlements.sql
--
-- Receiving a payment by tenant. One payment can settle several invoices, or
-- pay part of one as a payment on account.
--
--   * CustomerSettlements: the payment as a whole - the settlement document.
--   * CustomerReceipts.CustomerSettlementId: the payment a receipt is part of.
--   * CustomerReceipts.OutstandingBefore: what the invoice owed just before the
--     receipt, which is how a payment on account is told from a full settlement.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The payment received from a tenant.
IF OBJECT_ID('CustomerSettlements', 'U') IS NULL CREATE TABLE [CustomerSettlements] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [TenantId] int NOT NULL,
    [SettlementNumber] nvarchar(100) NOT NULL,
    [SettlementDate] datetime2 NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [PaymentMethod] nvarchar(30) NOT NULL,
    [Reference] nvarchar(100) NULL,
    [Notes] nvarchar(500) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_CustomerSettlements] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CustomerSettlements_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_CustomerSettlements_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
);


-- 2. A company does not use the same settlement number twice.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CustomerSettlements_CompanyId_SettlementNumber' AND object_id = OBJECT_ID('CustomerSettlements')) CREATE UNIQUE INDEX [IX_CustomerSettlements_CompanyId_SettlementNumber] ON [CustomerSettlements] ([CompanyId], [SettlementNumber]);


-- 3. Settlements are looked up by tenant.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CustomerSettlements_TenantId' AND object_id = OBJECT_ID('CustomerSettlements')) CREATE INDEX [IX_CustomerSettlements_TenantId] ON [CustomerSettlements] ([TenantId]);


-- 4. The payment a receipt is part of.
IF COL_LENGTH('CustomerReceipts', 'CustomerSettlementId') IS NULL ALTER TABLE [CustomerReceipts] ADD [CustomerSettlementId] int NULL CONSTRAINT [FK_CustomerReceipts_CustomerSettlements] FOREIGN KEY REFERENCES [CustomerSettlements] ([Id]);


-- 5. What the invoice owed just before the receipt.
IF COL_LENGTH('CustomerReceipts', 'OutstandingBefore') IS NULL ALTER TABLE [CustomerReceipts] ADD [OutstandingBefore] decimal(18,2) NULL;


-- 6. Receipts are listed under their settlement.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CustomerReceipts_CustomerSettlementId' AND object_id = OBJECT_ID('CustomerReceipts')) CREATE INDEX [IX_CustomerReceipts_CustomerSettlementId] ON [CustomerReceipts] ([CustomerSettlementId]);
