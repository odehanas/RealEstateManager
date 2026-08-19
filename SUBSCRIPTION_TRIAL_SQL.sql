SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'dbo.CompanySubscriptions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompanySubscriptions
    (
        Id INT IDENTITY(1,1) NOT NULL,
        CompanyId INT NOT NULL,
        TrialStartDate DATETIME2 NOT NULL,
        TrialEndDate DATETIME2 NOT NULL,
        SubscriptionEndDate DATETIME2 NULL,
        IsSuspended BIT NOT NULL CONSTRAINT DF_CompanySubscriptions_IsSuspended DEFAULT (0),
        LastActivatedAt DATETIME2 NULL,
        Notes NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_CompanySubscriptions_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_CompanySubscriptions_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_CompanySubscriptions PRIMARY KEY CLUSTERED (Id)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_CompanySubscriptions_Companies_CompanyId'
)
BEGIN
    ALTER TABLE dbo.CompanySubscriptions
    ADD CONSTRAINT FK_CompanySubscriptions_Companies_CompanyId
    FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id)
    ON DELETE CASCADE;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_CompanySubscriptions_CompanyId'
      AND object_id = OBJECT_ID(N'dbo.CompanySubscriptions')
)
BEGIN
    CREATE UNIQUE INDEX IX_CompanySubscriptions_CompanyId
    ON dbo.CompanySubscriptions (CompanyId);
END;

IF OBJECT_ID(N'dbo.SubscriptionActivationCodes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SubscriptionActivationCodes
    (
        Id INT IDENTITY(1,1) NOT NULL,
        CompanyId INT NOT NULL,
        Code NVARCHAR(50) NOT NULL,
        DurationDays INT NOT NULL,
        Notes NVARCHAR(300) NULL,
        IssuedAt DATETIME2 NOT NULL CONSTRAINT DF_SubscriptionActivationCodes_IssuedAt DEFAULT (SYSUTCDATETIME()),
        ActivatedAt DATETIME2 NULL,
        IsCancelled BIT NOT NULL CONSTRAINT DF_SubscriptionActivationCodes_IsCancelled DEFAULT (0),
        CONSTRAINT PK_SubscriptionActivationCodes PRIMARY KEY CLUSTERED (Id)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_SubscriptionActivationCodes_Companies_CompanyId'
)
BEGIN
    ALTER TABLE dbo.SubscriptionActivationCodes
    ADD CONSTRAINT FK_SubscriptionActivationCodes_Companies_CompanyId
    FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id)
    ON DELETE CASCADE;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_SubscriptionActivationCodes_Code'
      AND object_id = OBJECT_ID(N'dbo.SubscriptionActivationCodes')
)
BEGIN
    CREATE UNIQUE INDEX IX_SubscriptionActivationCodes_Code
    ON dbo.SubscriptionActivationCodes (Code);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_SubscriptionActivationCodes_CompanyId_IssuedAt'
      AND object_id = OBJECT_ID(N'dbo.SubscriptionActivationCodes')
)
BEGIN
    CREATE INDEX IX_SubscriptionActivationCodes_CompanyId_IssuedAt
    ON dbo.SubscriptionActivationCodes (CompanyId, IssuedAt);
END;

INSERT INTO dbo.CompanySubscriptions
(
    CompanyId,
    TrialStartDate,
    TrialEndDate,
    SubscriptionEndDate,
    IsSuspended,
    LastActivatedAt,
    Notes,
    CreatedAt,
    UpdatedAt
)
SELECT
    c.Id,
    CAST(SYSUTCDATETIME() AS DATE),
    DATEADD(DAY, 15, CAST(SYSUTCDATETIME() AS DATE)),
    NULL,
    0,
    NULL,
    N'Auto-created 15-day trial during subscription module upgrade.',
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
FROM dbo.Companies c
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.CompanySubscriptions s
    WHERE s.CompanyId = c.Id
);
