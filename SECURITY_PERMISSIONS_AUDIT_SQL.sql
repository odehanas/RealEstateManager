SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'dbo.AppUserPermissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppUserPermissions
    (
        Id INT IDENTITY(1,1) NOT NULL,
        AppUserId INT NOT NULL,
        PermissionKey NVARCHAR(100) NOT NULL,
        GrantedAt DATETIME2 NOT NULL CONSTRAINT DF_AppUserPermissions_GrantedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_AppUserPermissions PRIMARY KEY CLUSTERED (Id)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_AppUserPermissions_AppUsers_AppUserId'
)
BEGIN
    ALTER TABLE dbo.AppUserPermissions
    ADD CONSTRAINT FK_AppUserPermissions_AppUsers_AppUserId
    FOREIGN KEY (AppUserId) REFERENCES dbo.AppUsers(Id)
    ON DELETE CASCADE;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_AppUserPermissions_AppUserId_PermissionKey'
      AND object_id = OBJECT_ID(N'dbo.AppUserPermissions')
)
BEGIN
    CREATE UNIQUE INDEX IX_AppUserPermissions_AppUserId_PermissionKey
    ON dbo.AppUserPermissions (AppUserId, PermissionKey);
END;

IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs
    (
        Id INT IDENTITY(1,1) NOT NULL,
        AppUserId INT NULL,
        CompanyId INT NULL,
        Username NVARCHAR(100) NULL,
        DisplayName NVARCHAR(200) NULL,
        CompanyName NVARCHAR(200) NULL,
        HttpMethod NVARCHAR(20) NOT NULL,
        RequestPath NVARCHAR(300) NOT NULL,
        QueryString NVARCHAR(1000) NULL,
        ControllerName NVARCHAR(100) NULL,
        ActionName NVARCHAR(100) NULL,
        ActivityName NVARCHAR(200) NULL,
        IsSuccess BIT NOT NULL CONSTRAINT DF_AuditLogs_IsSuccess DEFAULT (1),
        StatusCode INT NOT NULL,
        ErrorMessage NVARCHAR(1000) NULL,
        IpAddress NVARCHAR(100) NULL,
        OccurredAt DATETIME2 NOT NULL CONSTRAINT DF_AuditLogs_OccurredAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_AuditLogs PRIMARY KEY CLUSTERED (Id)
    );
END;

IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.columns
       WHERE object_id = OBJECT_ID(N'dbo.AuditLogs')
         AND name = 'CompanyName'
   )
BEGIN
    ALTER TABLE dbo.AuditLogs
    ADD CompanyName NVARCHAR(200) NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_AuditLogs_AppUsers_AppUserId'
)
BEGIN
    ALTER TABLE dbo.AuditLogs
    ADD CONSTRAINT FK_AuditLogs_AppUsers_AppUserId
    FOREIGN KEY (AppUserId) REFERENCES dbo.AppUsers(Id)
    ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_AuditLogs_Companies_CompanyId'
)
BEGIN
    ALTER TABLE dbo.AuditLogs
    ADD CONSTRAINT FK_AuditLogs_Companies_CompanyId
    FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id)
    ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_AuditLogs_OccurredAt'
      AND object_id = OBJECT_ID(N'dbo.AuditLogs')
)
BEGIN
    CREATE INDEX IX_AuditLogs_OccurredAt
    ON dbo.AuditLogs (OccurredAt);
END;
