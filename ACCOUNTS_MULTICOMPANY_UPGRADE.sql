SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @DefaultCompanyId INT;
SELECT TOP (1) @DefaultCompanyId = Id
FROM dbo.Companies
ORDER BY Id;

IF @DefaultCompanyId IS NULL
BEGIN
    RAISERROR('No company record exists in dbo.Companies. Create a company first, then rerun this script.', 16, 1);
    RETURN;
END;

IF COL_LENGTH('dbo.Accounts', 'CompanyId') IS NULL
BEGIN
    ALTER TABLE dbo.Accounts
    ADD CompanyId INT NULL;
END;

EXEC sp_executesql
    N'UPDATE dbo.Accounts
      SET CompanyId = @CompanyId
      WHERE CompanyId IS NULL;',
    N'@CompanyId INT',
    @CompanyId = @DefaultCompanyId;

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Accounts')
      AND name = 'CompanyId'
      AND is_nullable = 1
)
BEGIN
    EXEC(N'ALTER TABLE dbo.Accounts ALTER COLUMN CompanyId INT NOT NULL;');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_Accounts_Companies_CompanyId'
)
BEGIN
    EXEC(N'
        ALTER TABLE dbo.Accounts
        ADD CONSTRAINT FK_Accounts_Companies_CompanyId
        FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id);
    ');
END;

DECLARE @OldCodeIndexName NVARCHAR(128);
SELECT TOP (1) @OldCodeIndexName = i.name
FROM sys.indexes i
INNER JOIN sys.index_columns ic
    ON i.object_id = ic.object_id
   AND i.index_id = ic.index_id
INNER JOIN sys.columns c
    ON ic.object_id = c.object_id
   AND ic.column_id = c.column_id
WHERE i.object_id = OBJECT_ID(N'dbo.Accounts')
  AND i.is_unique = 1
  AND i.name <> 'IX_Accounts_CompanyId_Code'
  AND c.name = 'Code'
GROUP BY i.name
HAVING COUNT(*) = 1;

IF @OldCodeIndexName IS NOT NULL
BEGIN
    EXEC(N'DROP INDEX [' + @OldCodeIndexName + N'] ON dbo.Accounts;');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_Accounts_CompanyId_Code'
      AND object_id = OBJECT_ID(N'dbo.Accounts')
)
BEGIN
    EXEC(N'
        CREATE UNIQUE INDEX IX_Accounts_CompanyId_Code
        ON dbo.Accounts (CompanyId, Code);
    ');
END;
