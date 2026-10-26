-- ============================================================================
-- 31_AddSavedReports.sql
--
-- Reports saved from the Report Builder: each person's own, and those shared
-- with the company.
--
-- Adds:
--   SavedReports
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. Saved reports.
IF OBJECT_ID('SavedReports', 'U') IS NULL CREATE TABLE [SavedReports] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [AppUserId] int NOT NULL,
    [OwnerName] nvarchar(200) NOT NULL,
    [Name] nvarchar(150) NOT NULL,
    [DefinitionJson] nvarchar(max) NOT NULL,
    [IsShared] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_SavedReports] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SavedReports_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


-- 2. Reports are listed by company and person.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SavedReports_CompanyId_AppUserId') CREATE INDEX [IX_SavedReports_CompanyId_AppUserId] ON [SavedReports] ([CompanyId], [AppUserId]);


-- 3. Check: expect one row showing 0 saved reports.
SELECT COUNT(*) AS [SavedReports] FROM [SavedReports];
