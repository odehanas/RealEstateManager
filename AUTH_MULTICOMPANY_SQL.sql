SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'dbo.AppUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppUsers
    (
        Id INT IDENTITY(1,1) NOT NULL,
        Username NVARCHAR(100) NOT NULL,
        DisplayName NVARCHAR(200) NOT NULL,
        PasswordHash NVARCHAR(500) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_AppUsers_IsActive DEFAULT (1),
        IsSystemAdmin BIT NOT NULL CONSTRAINT DF_AppUsers_IsSystemAdmin DEFAULT (0),
        CreatedAt DATETIME2 NOT NULL,
        LastLoginAt DATETIME2 NULL,
        CONSTRAINT PK_AppUsers PRIMARY KEY CLUSTERED (Id)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_AppUsers_Username'
      AND object_id = OBJECT_ID(N'dbo.AppUsers')
)
BEGIN
    CREATE UNIQUE INDEX IX_AppUsers_Username
    ON dbo.AppUsers (Username);
END;

IF OBJECT_ID(N'dbo.AppUserCompanies', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppUserCompanies
    (
        Id INT IDENTITY(1,1) NOT NULL,
        AppUserId INT NOT NULL,
        CompanyId INT NOT NULL,
        IsDefault BIT NOT NULL CONSTRAINT DF_AppUserCompanies_IsDefault DEFAULT (0),
        CONSTRAINT PK_AppUserCompanies PRIMARY KEY CLUSTERED (Id)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_AppUserCompanies_AppUsers_AppUserId'
)
BEGIN
    ALTER TABLE dbo.AppUserCompanies
    ADD CONSTRAINT FK_AppUserCompanies_AppUsers_AppUserId
    FOREIGN KEY (AppUserId) REFERENCES dbo.AppUsers(Id)
    ON DELETE CASCADE;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_AppUserCompanies_Companies_CompanyId'
)
BEGIN
    ALTER TABLE dbo.AppUserCompanies
    ADD CONSTRAINT FK_AppUserCompanies_Companies_CompanyId
    FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_AppUserCompanies_AppUserId_CompanyId'
      AND object_id = OBJECT_ID(N'dbo.AppUserCompanies')
)
BEGIN
    CREATE UNIQUE INDEX IX_AppUserCompanies_AppUserId_CompanyId
    ON dbo.AppUserCompanies (AppUserId, CompanyId);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_AppUserCompanies_CompanyId'
      AND object_id = OBJECT_ID(N'dbo.AppUserCompanies')
)
BEGIN
    CREATE INDEX IX_AppUserCompanies_CompanyId
    ON dbo.AppUserCompanies (CompanyId);
END;
