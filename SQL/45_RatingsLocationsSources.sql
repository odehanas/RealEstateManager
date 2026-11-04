-- ============================================================================
-- 45_RatingsLocationsSources.sql
--
--   * Where a tenant request came from: the portal, or WhatsApp, a phone call, a
--     visit or an email that a member of staff logged for the tenant.
--   * The tenant's rating of a finished maintenance job, from 1 to 10, with feedback.
--   * Owner withdrawal locations: a second dimension beside the category, saying
--     where the money was spent (the house, the farm, the private office...). The
--     starting list for each company comes from ChartOfAccountsSeeder.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. Where the request came from. Every request so far was raised on the portal.
IF COL_LENGTH('TenantServiceRequests', 'Source') IS NULL ALTER TABLE [TenantServiceRequests] ADD [Source] nvarchar(20) NOT NULL CONSTRAINT [DF_TenantServiceRequests_Source] DEFAULT N'Portal';


-- 2. The rating, 1 to 10.
IF COL_LENGTH('TenantServiceRequests', 'Rating') IS NULL ALTER TABLE [TenantServiceRequests] ADD [Rating] int NULL;


-- 3. What the tenant wrote about the job.
IF COL_LENGTH('TenantServiceRequests', 'RatingFeedback') IS NULL ALTER TABLE [TenantServiceRequests] ADD [RatingFeedback] nvarchar(1000) NULL;


-- 4. When the rating was given.
IF COL_LENGTH('TenantServiceRequests', 'RatedAt') IS NULL ALTER TABLE [TenantServiceRequests] ADD [RatedAt] datetime2 NULL;


-- 5. The member of staff who recorded it for the tenant; empty when the tenant rated on the portal.
IF COL_LENGTH('TenantServiceRequests', 'RatedByAppUserId') IS NULL ALTER TABLE [TenantServiceRequests] ADD [RatedByAppUserId] int NULL;


-- 6. A rating is always between 1 and 10.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_TenantServiceRequests_Rating') ALTER TABLE [TenantServiceRequests] ADD CONSTRAINT [CK_TenantServiceRequests_Rating] CHECK ([Rating] IS NULL OR ([Rating] >= 1 AND [Rating] <= 10));


-- 7. The locations.
IF OBJECT_ID('OwnerWithdrawalLocations', 'U') IS NULL CREATE TABLE [OwnerWithdrawalLocations] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [Code] nvarchar(30) NOT NULL,
    [Name] nvarchar(150) NOT NULL,
    [Description] nvarchar(500) NULL,
    [SortOrder] int NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_OwnerWithdrawalLocations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OwnerWithdrawalLocations_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


-- 8. A code is used once in a company.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OwnerWithdrawalLocations_CompanyId_Code' AND object_id = OBJECT_ID('OwnerWithdrawalLocations')) CREATE UNIQUE INDEX [IX_OwnerWithdrawalLocations_CompanyId_Code] ON [OwnerWithdrawalLocations] ([CompanyId], [Code]);


-- 9. The location on a journal line. Earlier withdrawals keep it empty.
IF COL_LENGTH('JournalEntryLines', 'OwnerWithdrawalLocationId') IS NULL ALTER TABLE [JournalEntryLines] ADD [OwnerWithdrawalLocationId] int NULL;


-- 10. It must be a real location.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_JournalEntryLines_OwnerWithdrawalLocations_OwnerWithdrawalLocationId') ALTER TABLE [JournalEntryLines] ADD CONSTRAINT [FK_JournalEntryLines_OwnerWithdrawalLocations_OwnerWithdrawalLocationId] FOREIGN KEY ([OwnerWithdrawalLocationId]) REFERENCES [OwnerWithdrawalLocations] ([Id]);


-- 11. Lines are found by location.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JournalEntryLines_OwnerWithdrawalLocationId' AND object_id = OBJECT_ID('JournalEntryLines')) CREATE INDEX [IX_JournalEntryLines_OwnerWithdrawalLocationId] ON [JournalEntryLines] ([OwnerWithdrawalLocationId]);
