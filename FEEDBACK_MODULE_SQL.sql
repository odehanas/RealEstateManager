SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'dbo.UserFeedback', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserFeedback
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserFeedback PRIMARY KEY,
        CompanyId INT NOT NULL,
        AppUserId INT NOT NULL,
        ReviewedByAppUserId INT NULL,
        Username NVARCHAR(100) NOT NULL,
        DisplayName NVARCHAR(200) NOT NULL,
        CompanyName NVARCHAR(200) NOT NULL,
        Category NVARCHAR(50) NOT NULL CONSTRAINT DF_UserFeedback_Category DEFAULT(N'General'),
        Subject NVARCHAR(200) NOT NULL,
        Message NVARCHAR(2000) NOT NULL,
        Status NVARCHAR(50) NOT NULL CONSTRAINT DF_UserFeedback_Status DEFAULT(N'New'),
        AdminNotes NVARCHAR(1000) NULL,
        SubmittedAt DATETIME2 NOT NULL CONSTRAINT DF_UserFeedback_SubmittedAt DEFAULT(SYSUTCDATETIME()),
        ReviewedAt DATETIME2 NULL
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_UserFeedback_Companies_CompanyId'
)
BEGIN
    ALTER TABLE dbo.UserFeedback
    ADD CONSTRAINT FK_UserFeedback_Companies_CompanyId
        FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id) ON DELETE CASCADE;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_UserFeedback_AppUsers_AppUserId'
)
BEGIN
    ALTER TABLE dbo.UserFeedback
    ADD CONSTRAINT FK_UserFeedback_AppUsers_AppUserId
        FOREIGN KEY (AppUserId) REFERENCES dbo.AppUsers(Id);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_UserFeedback_AppUsers_ReviewedByAppUserId'
)
BEGIN
    ALTER TABLE dbo.UserFeedback
    ADD CONSTRAINT FK_UserFeedback_AppUsers_ReviewedByAppUserId
        FOREIGN KEY (ReviewedByAppUserId) REFERENCES dbo.AppUsers(Id);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_UserFeedback_CompanyId_SubmittedAt'
      AND object_id = OBJECT_ID(N'dbo.UserFeedback')
)
BEGIN
    CREATE INDEX IX_UserFeedback_CompanyId_SubmittedAt
    ON dbo.UserFeedback (CompanyId, SubmittedAt DESC);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_UserFeedback_Status'
      AND object_id = OBJECT_ID(N'dbo.UserFeedback')
)
BEGIN
    CREATE INDEX IX_UserFeedback_Status
    ON dbo.UserFeedback (Status);
END;
