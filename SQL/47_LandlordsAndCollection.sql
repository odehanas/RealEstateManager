-- ============================================================================
-- 47_LandlordsAndCollection.sql
--
--   * Landlords as records of their own: TRN, contact, their own bank account and
--     how their rent is collected by default.
--   * The company's bank accounts, each tied to its ledger account, marked as the
--     company's operating money or as owners' funds.
--   * On a managed property, who holds the rent: the company, an owners' funds
--     account, or the landlord directly. Receipts and cheques record which applied.
--   * Every owner name already on a managed property becomes a landlord, and the
--     property is linked to it, so nothing has to be typed again.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there. Statement 13 is an UPDATE
-- that only fills the new LandlordId column of properties that have none yet.
-- @allow-risky-statements
-- ============================================================================


-- 1. The landlords.
IF OBJECT_ID('Landlords', 'U') IS NULL CREATE TABLE [Landlords] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Trn] nvarchar(20) NULL,
    [Email] nvarchar(200) NULL,
    [Phone] nvarchar(50) NULL,
    [BankName] nvarchar(100) NULL,
    [AccountName] nvarchar(200) NULL,
    [Iban] nvarchar(34) NULL,
    [DefaultCollectionMode] nvarchar(20) NOT NULL,
    [Notes] nvarchar(1000) NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Landlords] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Landlords_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


-- 2. A landlord is named once in a company.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Landlords_CompanyId_Name' AND object_id = OBJECT_ID('Landlords')) CREATE UNIQUE INDEX [IX_Landlords_CompanyId_Name] ON [Landlords] ([CompanyId], [Name]);


-- 3. The company's bank accounts.
IF OBJECT_ID('BankAccounts', 'U') IS NULL CREATE TABLE [BankAccounts] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [BankName] nvarchar(100) NULL,
    [Iban] nvarchar(34) NULL,
    [AccountId] int NOT NULL,
    [Purpose] nvarchar(20) NOT NULL,
    [LandlordId] int NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_BankAccounts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_BankAccounts_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_BankAccounts_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [Accounts] ([Id]),
    CONSTRAINT [FK_BankAccounts_Landlords_LandlordId] FOREIGN KEY ([LandlordId]) REFERENCES [Landlords] ([Id])
);


-- 4. A bank account is named once in a company.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BankAccounts_CompanyId_Name' AND object_id = OBJECT_ID('BankAccounts')) CREATE UNIQUE INDEX [IX_BankAccounts_CompanyId_Name] ON [BankAccounts] ([CompanyId], [Name]);


-- 5. A ledger account belongs to one bank account.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BankAccounts_AccountId' AND object_id = OBJECT_ID('BankAccounts')) CREATE UNIQUE INDEX [IX_BankAccounts_AccountId] ON [BankAccounts] ([AccountId]);


-- 6. The landlord of a property.
IF COL_LENGTH('Properties', 'LandlordId') IS NULL ALTER TABLE [Properties] ADD [LandlordId] int NULL;


-- 7. Who holds the rent. Every property so far banked it with the company.
IF COL_LENGTH('Properties', 'CollectionMode') IS NULL ALTER TABLE [Properties] ADD [CollectionMode] nvarchar(20) NOT NULL CONSTRAINT [DF_Properties_CollectionMode] DEFAULT N'Company';


-- 8. It must be a real landlord.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Properties_Landlords_LandlordId') ALTER TABLE [Properties] ADD CONSTRAINT [FK_Properties_Landlords_LandlordId] FOREIGN KEY ([LandlordId]) REFERENCES [Landlords] ([Id]);


-- 9. A landlord's properties are found by landlord.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Properties_LandlordId' AND object_id = OBJECT_ID('Properties')) CREATE INDEX [IX_Properties_LandlordId] ON [Properties] ([LandlordId]);


-- 10. Who held the money of a cheque, fixed when it was recorded.
IF COL_LENGTH('PdcReceipts', 'CollectionMode') IS NULL ALTER TABLE [PdcReceipts] ADD [CollectionMode] nvarchar(20) NULL;


-- 11. Who received the money of a receipt.
IF COL_LENGTH('CustomerReceipts', 'CollectionMode') IS NULL ALTER TABLE [CustomerReceipts] ADD [CollectionMode] nvarchar(20) NULL;


-- 12. A landlord for every owner name already on a managed property, with the TRN given there.
INSERT INTO [Landlords] ([CompanyId], [Name], [Trn], [DefaultCollectionMode], [IsActive])
SELECT p.[CompanyId], LEFT(LTRIM(RTRIM(p.[OwnerName])), 200), MAX(NULLIF(LTRIM(RTRIM(p.[OwnerTrn])), N'')), N'Company', 1
FROM [Properties] p
WHERE p.[OwnershipType] = N'Managed' AND p.[OwnerName] IS NOT NULL AND LTRIM(RTRIM(p.[OwnerName])) <> N''
  AND NOT EXISTS (SELECT 1 FROM [Landlords] l WHERE l.[CompanyId] = p.[CompanyId] AND l.[Name] = LEFT(LTRIM(RTRIM(p.[OwnerName])), 200))
GROUP BY p.[CompanyId], LEFT(LTRIM(RTRIM(p.[OwnerName])), 200);


-- 13. Each managed property linked to its landlord.
UPDATE p SET p.[LandlordId] = l.[Id]
FROM [Properties] p
JOIN [Landlords] l ON l.[CompanyId] = p.[CompanyId] AND l.[Name] = LEFT(LTRIM(RTRIM(p.[OwnerName])), 200)
WHERE p.[LandlordId] IS NULL AND p.[OwnershipType] = N'Managed';
