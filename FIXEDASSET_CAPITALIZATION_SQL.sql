SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH('dbo.FixedAssets', 'CapitalizationDate') IS NULL
BEGIN
    ALTER TABLE dbo.FixedAssets
    ADD CapitalizationDate DATETIME2 NULL;
END;

IF COL_LENGTH('dbo.FixedAssets', 'CapitalizationJournalEntryId') IS NULL
BEGIN
    ALTER TABLE dbo.FixedAssets
    ADD CapitalizationJournalEntryId INT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_FixedAssets_JournalEntries_CapitalizationJournalEntryId'
)
BEGIN
    ALTER TABLE dbo.FixedAssets
    ADD CONSTRAINT FK_FixedAssets_JournalEntries_CapitalizationJournalEntryId
    FOREIGN KEY (CapitalizationJournalEntryId) REFERENCES dbo.JournalEntries(Id);
END;

IF OBJECT_ID(N'dbo.FixedAssetAcquisitionCosts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FixedAssetAcquisitionCosts
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FixedAssetId INT NOT NULL,
        CostDate DATETIME2 NOT NULL,
        CostType NVARCHAR(50) NOT NULL,
        Description NVARCHAR(300) NOT NULL,
        Amount DECIMAL(18,2) NOT NULL CONSTRAINT DF_FixedAssetAcquisitionCosts_Amount DEFAULT(0),
        IsCapitalizable BIT NOT NULL CONSTRAINT DF_FixedAssetAcquisitionCosts_IsCapitalizable DEFAULT(1),
        Status NVARCHAR(30) NOT NULL CONSTRAINT DF_FixedAssetAcquisitionCosts_Status DEFAULT('Open'),
        SourceDocumentNumber NVARCHAR(100) NULL,
        ReferenceNumber NVARCHAR(100) NULL,
        Notes NVARCHAR(1000) NULL,
        OffsetAccountId INT NULL,
        VendorId INT NULL,
        VendorBillId INT NULL,
        ExpenseAccountId INT NULL,
        JournalEntryId INT NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_FixedAssetAcquisitionCosts_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CapitalizedAt DATETIME2 NULL
    );
END;

IF COL_LENGTH('dbo.FixedAssetAcquisitionCosts', 'VendorId') IS NULL
BEGIN
    ALTER TABLE dbo.FixedAssetAcquisitionCosts
    ADD VendorId INT NULL;
END;

IF COL_LENGTH('dbo.FixedAssetAcquisitionCosts', 'VendorBillId') IS NULL
BEGIN
    ALTER TABLE dbo.FixedAssetAcquisitionCosts
    ADD VendorBillId INT NULL;
END;

IF COL_LENGTH('dbo.FixedAssetAcquisitionCosts', 'OffsetAccountId') IS NULL
BEGIN
    ALTER TABLE dbo.FixedAssetAcquisitionCosts
    ADD OffsetAccountId INT NULL;
END;

IF COL_LENGTH('dbo.FixedAssetAcquisitionCosts', 'ExpenseAccountId') IS NULL
BEGIN
    ALTER TABLE dbo.FixedAssetAcquisitionCosts
    ADD ExpenseAccountId INT NULL;
END;

IF COL_LENGTH('dbo.FixedAssetAcquisitionCosts', 'JournalEntryId') IS NULL
BEGIN
    ALTER TABLE dbo.FixedAssetAcquisitionCosts
    ADD JournalEntryId INT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_FixedAssetAcquisitionCosts_FixedAssets_FixedAssetId'
)
BEGIN
    ALTER TABLE dbo.FixedAssetAcquisitionCosts
    ADD CONSTRAINT FK_FixedAssetAcquisitionCosts_FixedAssets_FixedAssetId
    FOREIGN KEY (FixedAssetId) REFERENCES dbo.FixedAssets(Id) ON DELETE CASCADE;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_FixedAssetAcquisitionCosts_Accounts_OffsetAccountId'
)
BEGIN
    ALTER TABLE dbo.FixedAssetAcquisitionCosts
    ADD CONSTRAINT FK_FixedAssetAcquisitionCosts_Accounts_OffsetAccountId
    FOREIGN KEY (OffsetAccountId) REFERENCES dbo.Accounts(Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_FixedAssetAcquisitionCosts_Vendors_VendorId'
)
BEGIN
    ALTER TABLE dbo.FixedAssetAcquisitionCosts
    ADD CONSTRAINT FK_FixedAssetAcquisitionCosts_Vendors_VendorId
    FOREIGN KEY (VendorId) REFERENCES dbo.Vendors(Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_FixedAssetAcquisitionCosts_VendorBills_VendorBillId'
)
BEGIN
    ALTER TABLE dbo.FixedAssetAcquisitionCosts
    ADD CONSTRAINT FK_FixedAssetAcquisitionCosts_VendorBills_VendorBillId
    FOREIGN KEY (VendorBillId) REFERENCES dbo.VendorBills(Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_FixedAssetAcquisitionCosts_Accounts_ExpenseAccountId'
)
BEGIN
    ALTER TABLE dbo.FixedAssetAcquisitionCosts
    ADD CONSTRAINT FK_FixedAssetAcquisitionCosts_Accounts_ExpenseAccountId
    FOREIGN KEY (ExpenseAccountId) REFERENCES dbo.Accounts(Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_FixedAssetAcquisitionCosts_JournalEntries_JournalEntryId'
)
BEGIN
    ALTER TABLE dbo.FixedAssetAcquisitionCosts
    ADD CONSTRAINT FK_FixedAssetAcquisitionCosts_JournalEntries_JournalEntryId
    FOREIGN KEY (JournalEntryId) REFERENCES dbo.JournalEntries(Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_FixedAssetAcquisitionCosts_FixedAssetId_CostDate_Id'
      AND object_id = OBJECT_ID(N'dbo.FixedAssetAcquisitionCosts')
)
BEGIN
    CREATE INDEX IX_FixedAssetAcquisitionCosts_FixedAssetId_CostDate_Id
    ON dbo.FixedAssetAcquisitionCosts (FixedAssetId, CostDate, Id);
END;

IF COL_LENGTH('dbo.CompanyAccountSettings', 'FixedAssetClearingAccountId') IS NULL
BEGIN
    ALTER TABLE dbo.CompanyAccountSettings
    ADD FixedAssetClearingAccountId INT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_CompanyAccountSettings_Accounts_FixedAssetClearingAccountId'
)
BEGIN
    ALTER TABLE dbo.CompanyAccountSettings
    ADD CONSTRAINT FK_CompanyAccountSettings_Accounts_FixedAssetClearingAccountId
    FOREIGN KEY (FixedAssetClearingAccountId) REFERENCES dbo.Accounts(Id);
END;
