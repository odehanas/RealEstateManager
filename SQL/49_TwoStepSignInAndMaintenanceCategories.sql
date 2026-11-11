-- ============================================================================
-- 49_TwoStepSignInAndMaintenanceCategories.sql
--
--   * Two-step sign-in for staff users: after the password, a six-digit code from
--     an authenticator app on the user's phone. Each user switches it on for
--     themselves; a company can require it of everyone who works in it.
--   * The kinds of maintenance work (Plumbing, Electrical...) become a list each
--     company keeps for itself, instead of a fixed list in the program.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. Whether the user signs in with a code as well as the password.
IF COL_LENGTH('AppUsers', 'TwoFactorEnabled') IS NULL ALTER TABLE [AppUsers] ADD [TwoFactorEnabled] bit NOT NULL CONSTRAINT [DF_AppUsers_TwoFactorEnabled] DEFAULT 0;


-- 2. The key the user's authenticator app shares with us, stored encrypted.
IF COL_LENGTH('AppUsers', 'TwoFactorSecret') IS NULL ALTER TABLE [AppUsers] ADD [TwoFactorSecret] nvarchar(600) NULL;


-- 3. When the user switched it on.
IF COL_LENGTH('AppUsers', 'TwoFactorEnabledAt') IS NULL ALTER TABLE [AppUsers] ADD [TwoFactorEnabledAt] datetime2 NULL;


-- 4. The one-time recovery codes not yet used, stored as hashes.
IF COL_LENGTH('AppUsers', 'TwoFactorRecoveryCodes') IS NULL ALTER TABLE [AppUsers] ADD [TwoFactorRecoveryCodes] nvarchar(1000) NULL;


-- 5. The last code accepted, so the same code cannot be used twice.
IF COL_LENGTH('AppUsers', 'TwoFactorLastStep') IS NULL ALTER TABLE [AppUsers] ADD [TwoFactorLastStep] bigint NULL;


-- 6. A company can require two-step sign-in of all its users.
IF COL_LENGTH('Companies', 'RequireTwoFactor') IS NULL ALTER TABLE [Companies] ADD [RequireTwoFactor] bit NOT NULL CONSTRAINT [DF_Companies_RequireTwoFactor] DEFAULT 0;


-- 7. Each company's own kinds of maintenance work.
IF OBJECT_ID('MaintenanceCategories', 'U') IS NULL CREATE TABLE [MaintenanceCategories] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [Name] nvarchar(50) NOT NULL,
    [IsActive] bit NOT NULL CONSTRAINT [DF_MaintenanceCategories_IsActive] DEFAULT 1,
    [SortOrder] int NOT NULL CONSTRAINT [DF_MaintenanceCategories_SortOrder] DEFAULT 0,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_MaintenanceCategories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_MaintenanceCategories_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


-- 8. A company does not have the same kind of work twice.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MaintenanceCategories_CompanyId_Name' AND object_id = OBJECT_ID('MaintenanceCategories')) CREATE UNIQUE INDEX [IX_MaintenanceCategories_CompanyId_Name] ON [MaintenanceCategories] ([CompanyId], [Name]);
