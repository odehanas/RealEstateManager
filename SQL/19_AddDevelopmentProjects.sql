-- ============================================================================
-- 19_AddDevelopmentProjects.sql
--
-- Development projects: buildings under construction (development phase 2).
--
-- Adds:
--   DevelopmentProjects        the projects
--   DevelopmentBudgetLines     each project's budget per cost category
--   JournalEntryLines          the project and cost category a ledger line belongs to
--   VendorBillLines            the same, on supplier bill lines
--
-- Nothing existing is changed. The three new accounts - 13100 Properties Under
-- Development - For Sale, 15500 Investment Property Under Construction and
-- 15600 Capital Work in Progress - are added to every company automatically
-- the first time the new build starts after this script has been run.
--
-- RUN 18_AddPropertyRegistration.sql FIRST.
--
-- HOW TO RUN in site4now SQL Studio: run ONE numbered statement at a time, top
-- to bottom. The console executes only the first batch, so pasting the whole
-- file runs statement 1 and nothing else.
--
-- Run statements 1 to 9 BEFORE deploying the new build.
-- ============================================================================


-- 1. The projects.
CREATE TABLE [DevelopmentProjects] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [Code] nvarchar(20) NOT NULL,
    [Name] nvarchar(150) NOT NULL,
    [PropertyId] int NULL,
    [IntendedUse] nvarchar(20) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [WipAccountId] int NOT NULL,
    [StartDate] datetime2 NULL,
    [ExpectedCompletionDate] datetime2 NULL,
    [CompletionDate] datetime2 NULL,
    [CompletionCertificateNumber] nvarchar(50) NULL,
    [MainContractorVendorId] int NULL,
    [Notes] nvarchar(1000) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_DevelopmentProjects] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DevelopmentProjects_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_DevelopmentProjects_Properties_PropertyId] FOREIGN KEY ([PropertyId]) REFERENCES [Properties] ([Id]),
    CONSTRAINT [FK_DevelopmentProjects_Accounts_WipAccountId] FOREIGN KEY ([WipAccountId]) REFERENCES [Accounts] ([Id]),
    CONSTRAINT [FK_DevelopmentProjects_Vendors_MainContractorVendorId] FOREIGN KEY ([MainContractorVendorId]) REFERENCES [Vendors] ([Id])
);


-- 2. One project code per company.
CREATE UNIQUE INDEX [IX_DevelopmentProjects_CompanyId_Code] ON [DevelopmentProjects] ([CompanyId], [Code]);


-- 3. The budget lines.
CREATE TABLE [DevelopmentBudgetLines] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [DevelopmentProjectId] int NOT NULL,
    [CostCategory] nvarchar(30) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_DevelopmentBudgetLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DevelopmentBudgetLines_DevelopmentProjects_DevelopmentProjectId] FOREIGN KEY ([DevelopmentProjectId]) REFERENCES [DevelopmentProjects] ([Id]) ON DELETE CASCADE
);


-- 4. One budget line per category per project.
CREATE UNIQUE INDEX [IX_DevelopmentBudgetLines_DevelopmentProjectId_CostCategory] ON [DevelopmentBudgetLines] ([DevelopmentProjectId], [CostCategory]);


-- 5. The project on each ledger line. Empty for every existing line.
ALTER TABLE [JournalEntryLines] ADD [DevelopmentProjectId] int NULL, [DevelopmentCostCategory] nvarchar(30) NULL;


-- 6. Link the ledger line to its project.
ALTER TABLE [JournalEntryLines] ADD CONSTRAINT [FK_JournalEntryLines_DevelopmentProjects_DevelopmentProjectId] FOREIGN KEY ([DevelopmentProjectId]) REFERENCES [DevelopmentProjects] ([Id]);


-- 7. Finding a project's ledger lines.
CREATE INDEX [IX_JournalEntryLines_DevelopmentProjectId] ON [JournalEntryLines] ([DevelopmentProjectId]);


-- 8. The project on each supplier bill line, linked. Empty for every existing line.
ALTER TABLE [VendorBillLines] ADD [DevelopmentProjectId] int NULL CONSTRAINT [FK_VendorBillLines_DevelopmentProjects_DevelopmentProjectId] FOREIGN KEY REFERENCES [DevelopmentProjects] ([Id]), [DevelopmentCostCategory] nvarchar(30) NULL;


-- 9. Check: expect one row showing 0 projects.
SELECT COUNT(*) AS [DevelopmentProjects] FROM [DevelopmentProjects];
