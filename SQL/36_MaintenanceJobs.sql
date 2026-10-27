-- ============================================================================
-- 36_MaintenanceJobs.sql
--
-- Maintenance jobs carried out by the maintenance team:
--   * when work on a job started, so the dashboard can measure response times;
--   * the materials used on each job, from the maintenance store or bought for it;
--   * the store itself: account 11650 Maintenance Materials Stock, under assets,
--     for every company whose chart has the 10000 assets heading.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. When the technician started work.
IF COL_LENGTH('TenantServiceRequests', 'WorkStartedAt') IS NULL ALTER TABLE [TenantServiceRequests] ADD [WorkStartedAt] datetime2 NULL;


-- 2. Materials used on jobs.
IF OBJECT_ID('MaintenanceMaterials', 'U') IS NULL CREATE TABLE [MaintenanceMaterials] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [TenantServiceRequestId] int NOT NULL,
    [MaterialDate] datetime2 NOT NULL,
    [Description] nvarchar(300) NOT NULL,
    [Quantity] decimal(18,3) NOT NULL,
    [UnitCost] decimal(18,2) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Source] nvarchar(20) NOT NULL,
    [Reference] nvarchar(100) NULL,
    [ChargedTo] nvarchar(20) NOT NULL,
    [JournalEntryId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_MaintenanceMaterials] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_MaintenanceMaterials_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_MaintenanceMaterials_TenantServiceRequests_TenantServiceRequestId] FOREIGN KEY ([TenantServiceRequestId]) REFERENCES [TenantServiceRequests] ([Id]),
    CONSTRAINT [FK_MaintenanceMaterials_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries] ([Id])
);


-- 3. Materials are listed by job.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MaintenanceMaterials_CompanyId_TenantServiceRequestId' AND object_id = OBJECT_ID('MaintenanceMaterials')) CREATE INDEX [IX_MaintenanceMaterials_CompanyId_TenantServiceRequestId] ON [MaintenanceMaterials] ([CompanyId], [TenantServiceRequestId]);


-- 4. The maintenance store account for every company that has the assets heading.
INSERT INTO [Accounts] ([CompanyId], [Code], [Name], [AccountType], [ReportingGroup], [IsControlAccount], [IsOwnerAccount], [IsPettyCashAccount], [IsGroup], [IsPostable], [IsActive], [SortOrder], [OpeningBalance], [ParentAccountId])
SELECT g.[CompanyId], N'11650', N'Maintenance Materials Stock', N'Asset', g.[ReportingGroup], 0, 0, 0, 0, 1, 1, 11650, 0, g.[Id]
FROM [Accounts] g
WHERE g.[Code] = N'10000'
  AND NOT EXISTS (SELECT 1 FROM [Accounts] a WHERE a.[CompanyId] = g.[CompanyId] AND a.[Code] = N'11650');
