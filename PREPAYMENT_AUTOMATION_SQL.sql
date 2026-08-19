SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH('dbo.CompanyAccountSettings', 'PrepaidExpenseDefaultAccountId') IS NULL
BEGIN
    ALTER TABLE dbo.CompanyAccountSettings
    ADD PrepaidExpenseDefaultAccountId INT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_CompanyAccountSettings_Accounts_PrepaidExpenseDefaultAccountId'
)
BEGIN
    ALTER TABLE dbo.CompanyAccountSettings
    ADD CONSTRAINT FK_CompanyAccountSettings_Accounts_PrepaidExpenseDefaultAccountId
    FOREIGN KEY (PrepaidExpenseDefaultAccountId) REFERENCES dbo.Accounts(Id);
END;

IF OBJECT_ID(N'dbo.PrepaymentSchedules', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PrepaymentSchedules
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId INT NOT NULL,
        ScheduleNumber NVARCHAR(30) NOT NULL,
        Description NVARCHAR(300) NOT NULL,
        PrepaymentType NVARCHAR(30) NOT NULL,
        PaymentDate DATETIME2 NOT NULL,
        FirstRecognitionDate DATETIME2 NOT NULL,
        TotalAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_PrepaymentSchedules_TotalAmount DEFAULT(0),
        RecognitionMonths INT NOT NULL,
        PrepaidAccountId INT NOT NULL,
        ExpenseAccountId INT NOT NULL,
        OffsetAccountId INT NOT NULL,
        VendorId INT NULL,
        VendorBillId INT NULL,
        InitialJournalEntryId INT NULL,
        Status NVARCHAR(30) NOT NULL CONSTRAINT DF_PrepaymentSchedules_Status DEFAULT('Active'),
        ReferenceNumber NVARCHAR(100) NULL,
        Notes NVARCHAR(1000) NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_PrepaymentSchedules_CreatedAt DEFAULT(SYSUTCDATETIME())
    );
END;

IF COL_LENGTH('dbo.PrepaymentSchedules', 'VendorId') IS NULL
BEGIN
    ALTER TABLE dbo.PrepaymentSchedules
    ADD VendorId INT NULL;
END;

IF COL_LENGTH('dbo.PrepaymentSchedules', 'VendorBillId') IS NULL
BEGIN
    ALTER TABLE dbo.PrepaymentSchedules
    ADD VendorBillId INT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_PrepaymentSchedules_Companies_CompanyId'
)
BEGIN
    ALTER TABLE dbo.PrepaymentSchedules
    ADD CONSTRAINT FK_PrepaymentSchedules_Companies_CompanyId
    FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_PrepaymentSchedules_Accounts_PrepaidAccountId'
)
BEGIN
    ALTER TABLE dbo.PrepaymentSchedules
    ADD CONSTRAINT FK_PrepaymentSchedules_Accounts_PrepaidAccountId
    FOREIGN KEY (PrepaidAccountId) REFERENCES dbo.Accounts(Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_PrepaymentSchedules_Accounts_ExpenseAccountId'
)
BEGIN
    ALTER TABLE dbo.PrepaymentSchedules
    ADD CONSTRAINT FK_PrepaymentSchedules_Accounts_ExpenseAccountId
    FOREIGN KEY (ExpenseAccountId) REFERENCES dbo.Accounts(Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_PrepaymentSchedules_Accounts_OffsetAccountId'
)
BEGIN
    ALTER TABLE dbo.PrepaymentSchedules
    ADD CONSTRAINT FK_PrepaymentSchedules_Accounts_OffsetAccountId
    FOREIGN KEY (OffsetAccountId) REFERENCES dbo.Accounts(Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_PrepaymentSchedules_Vendors_VendorId'
)
BEGIN
    ALTER TABLE dbo.PrepaymentSchedules
    ADD CONSTRAINT FK_PrepaymentSchedules_Vendors_VendorId
    FOREIGN KEY (VendorId) REFERENCES dbo.Vendors(Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_PrepaymentSchedules_VendorBills_VendorBillId'
)
BEGIN
    ALTER TABLE dbo.PrepaymentSchedules
    ADD CONSTRAINT FK_PrepaymentSchedules_VendorBills_VendorBillId
    FOREIGN KEY (VendorBillId) REFERENCES dbo.VendorBills(Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_PrepaymentSchedules_JournalEntries_InitialJournalEntryId'
)
BEGIN
    ALTER TABLE dbo.PrepaymentSchedules
    ADD CONSTRAINT FK_PrepaymentSchedules_JournalEntries_InitialJournalEntryId
    FOREIGN KEY (InitialJournalEntryId) REFERENCES dbo.JournalEntries(Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_PrepaymentSchedules_CompanyId_ScheduleNumber'
      AND object_id = OBJECT_ID(N'dbo.PrepaymentSchedules')
)
BEGIN
    CREATE UNIQUE INDEX IX_PrepaymentSchedules_CompanyId_ScheduleNumber
    ON dbo.PrepaymentSchedules (CompanyId, ScheduleNumber);
END;

IF OBJECT_ID(N'dbo.PrepaymentScheduleLines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PrepaymentScheduleLines
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PrepaymentScheduleId INT NOT NULL,
        RecognitionDate DATETIME2 NOT NULL,
        Amount DECIMAL(18,2) NOT NULL CONSTRAINT DF_PrepaymentScheduleLines_Amount DEFAULT(0),
        Status NVARCHAR(30) NOT NULL CONSTRAINT DF_PrepaymentScheduleLines_Status DEFAULT('Planned'),
        JournalEntryId INT NULL,
        PostedAt DATETIME2 NULL
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_PrepaymentScheduleLines_PrepaymentSchedules_PrepaymentScheduleId'
)
BEGIN
    ALTER TABLE dbo.PrepaymentScheduleLines
    ADD CONSTRAINT FK_PrepaymentScheduleLines_PrepaymentSchedules_PrepaymentScheduleId
    FOREIGN KEY (PrepaymentScheduleId) REFERENCES dbo.PrepaymentSchedules(Id) ON DELETE CASCADE;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_PrepaymentScheduleLines_JournalEntries_JournalEntryId'
)
BEGIN
    ALTER TABLE dbo.PrepaymentScheduleLines
    ADD CONSTRAINT FK_PrepaymentScheduleLines_JournalEntries_JournalEntryId
    FOREIGN KEY (JournalEntryId) REFERENCES dbo.JournalEntries(Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_PrepaymentScheduleLines_PrepaymentScheduleId_RecognitionDate'
      AND object_id = OBJECT_ID(N'dbo.PrepaymentScheduleLines')
)
BEGIN
    CREATE UNIQUE INDEX IX_PrepaymentScheduleLines_PrepaymentScheduleId_RecognitionDate
    ON dbo.PrepaymentScheduleLines (PrepaymentScheduleId, RecognitionDate);
END;
