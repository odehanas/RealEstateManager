-- ============================================================================
-- 28_AddInternalControls.sql
--
-- Internal controls: a payment approval ladder (levels with limits, users on a
-- level, temporary delegation), payments held for release, sign-in lockout for
-- staff, and the employee self-service portal (logins and document requests).
--
-- Adds:
--   ApprovalLevels, ApprovalDelegations, PaymentApprovals,
--   EmployeePortalUsers, EmployeeRequests
--   Companies          RequirePaymentApproval, AllowSelfReleaseWithinLimit
--   AppUserCompanies   ApprovalLevelId
--   AppUsers           FailedLoginAttempts, LockedOutUntil, MustChangePassword, PasswordChangedAt
--   AppUserPermissions Owner oversight and payment approval for company administrators
--
-- Payment approval starts switched OFF for every company; it is switched on from
-- System Setup > Approval Limits once the levels and people are set.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. Approval levels.
IF OBJECT_ID('ApprovalLevels', 'U') IS NULL CREATE TABLE [ApprovalLevels] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [Name] nvarchar(50) NOT NULL,
    [MaxAmount] decimal(18,2) NULL,
    [SortOrder] int NOT NULL,
    CONSTRAINT [PK_ApprovalLevels] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ApprovalLevels_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


-- 2. One level name per company.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalLevels_CompanyId_Name') CREATE UNIQUE INDEX [IX_ApprovalLevels_CompanyId_Name] ON [ApprovalLevels] ([CompanyId], [Name]);


-- 3. Each user's level in each company.
IF COL_LENGTH('AppUserCompanies', 'ApprovalLevelId') IS NULL ALTER TABLE [AppUserCompanies] ADD [ApprovalLevelId] int NULL CONSTRAINT [FK_AppUserCompanies_ApprovalLevels_ApprovalLevelId] FOREIGN KEY REFERENCES [ApprovalLevels] ([Id]);


-- 4. Temporary delegation of authority.
IF OBJECT_ID('ApprovalDelegations', 'U') IS NULL CREATE TABLE [ApprovalDelegations] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [FromAppUserId] int NOT NULL,
    [FromName] nvarchar(200) NOT NULL,
    [ToAppUserId] int NOT NULL,
    [ToName] nvarchar(200) NOT NULL,
    [MaxAmount] decimal(18,2) NULL,
    [StartDate] datetime2 NOT NULL,
    [EndDate] datetime2 NOT NULL,
    [Reason] nvarchar(300) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [RevokedByName] nvarchar(200) NULL,
    [RevokedAt] datetime2 NULL,
    CONSTRAINT [PK_ApprovalDelegations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ApprovalDelegations_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


-- 5. Finding a person's delegations.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalDelegations_CompanyId_ToAppUserId') CREATE INDEX [IX_ApprovalDelegations_CompanyId_ToAppUserId] ON [ApprovalDelegations] ([CompanyId], [ToAppUserId]);


-- 6. Payments held for release.
IF OBJECT_ID('PaymentApprovals', 'U') IS NULL CREATE TABLE [PaymentApprovals] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [JournalEntryId] int NOT NULL,
    [SourceType] nvarchar(50) NOT NULL,
    [Description] nvarchar(300) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [RequiredLevel] nvarchar(50) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [RequestedByAppUserId] int NULL,
    [RequestedByName] nvarchar(200) NOT NULL,
    [RequestedAt] datetime2 NOT NULL,
    [DecidedByAppUserId] int NULL,
    [DecidedByName] nvarchar(200) NULL,
    [DecidedAt] datetime2 NULL,
    [Comment] nvarchar(500) NULL,
    CONSTRAINT [PK_PaymentApprovals] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PaymentApprovals_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_PaymentApprovals_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries] ([Id]) ON DELETE CASCADE
);


-- 7. The queue of payments waiting.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PaymentApprovals_CompanyId_Status') CREATE INDEX [IX_PaymentApprovals_CompanyId_Status] ON [PaymentApprovals] ([CompanyId], [Status]);


-- 8. A payment's approval history.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PaymentApprovals_JournalEntryId') CREATE INDEX [IX_PaymentApprovals_JournalEntryId] ON [PaymentApprovals] ([JournalEntryId]);


-- 9. Company switch: payments need approval.
IF COL_LENGTH('Companies', 'RequirePaymentApproval') IS NULL ALTER TABLE [Companies] ADD [RequirePaymentApproval] bit NOT NULL CONSTRAINT [DF_Companies_RequirePaymentApproval] DEFAULT 0;


-- 10. Company switch: release within one's own limit.
IF COL_LENGTH('Companies', 'AllowSelfReleaseWithinLimit') IS NULL ALTER TABLE [Companies] ADD [AllowSelfReleaseWithinLimit] bit NOT NULL CONSTRAINT [DF_Companies_AllowSelfReleaseWithinLimit] DEFAULT 1;


-- 11. Staff sign-in: failed attempts.
IF COL_LENGTH('AppUsers', 'FailedLoginAttempts') IS NULL ALTER TABLE [AppUsers] ADD [FailedLoginAttempts] int NOT NULL CONSTRAINT [DF_AppUsers_FailedLoginAttempts] DEFAULT 0;


-- 12. Staff sign-in: locked until.
IF COL_LENGTH('AppUsers', 'LockedOutUntil') IS NULL ALTER TABLE [AppUsers] ADD [LockedOutUntil] datetime2 NULL;


-- 13. Staff sign-in: password must be changed.
IF COL_LENGTH('AppUsers', 'MustChangePassword') IS NULL ALTER TABLE [AppUsers] ADD [MustChangePassword] bit NOT NULL CONSTRAINT [DF_AppUsers_MustChangePassword] DEFAULT 0;


-- 14. Staff sign-in: when the password was last changed.
IF COL_LENGTH('AppUsers', 'PasswordChangedAt') IS NULL ALTER TABLE [AppUsers] ADD [PasswordChangedAt] datetime2 NULL;


-- 15. Employee portal logins.
IF OBJECT_ID('EmployeePortalUsers', 'U') IS NULL CREATE TABLE [EmployeePortalUsers] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [EmployeeId] int NOT NULL,
    [Username] nvarchar(100) NOT NULL,
    [PasswordHash] nvarchar(500) NOT NULL,
    [IsActive] bit NOT NULL,
    [MustChangePassword] bit NOT NULL,
    [FailedLoginAttempts] int NOT NULL,
    [LockedOutUntil] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    [LastLoginAt] datetime2 NULL,
    CONSTRAINT [PK_EmployeePortalUsers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_EmployeePortalUsers_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_EmployeePortalUsers_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([Id])
);


-- 16. Usernames are unique across the system.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EmployeePortalUsers_Username') CREATE UNIQUE INDEX [IX_EmployeePortalUsers_Username] ON [EmployeePortalUsers] ([Username]);


-- 17. One login per employee.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EmployeePortalUsers_EmployeeId') CREATE UNIQUE INDEX [IX_EmployeePortalUsers_EmployeeId] ON [EmployeePortalUsers] ([EmployeeId]);


-- 18. Employee document requests.
IF OBJECT_ID('EmployeeRequests', 'U') IS NULL CREATE TABLE [EmployeeRequests] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [EmployeeId] int NOT NULL,
    [RequestType] nvarchar(30) NOT NULL,
    [AddressedTo] nvarchar(200) NULL,
    [Purpose] nvarchar(300) NULL,
    [Status] nvarchar(20) NOT NULL,
    [RequestedAt] datetime2 NOT NULL,
    [DecidedByName] nvarchar(200) NULL,
    [DecidedAt] datetime2 NULL,
    [Comment] nvarchar(500) NULL,
    [ReferenceNumber] nvarchar(30) NULL,
    CONSTRAINT [PK_EmployeeRequests] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_EmployeeRequests_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_EmployeeRequests_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([Id])
);


-- 19. The HR queue of requests.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EmployeeRequests_CompanyId_Status') CREATE INDEX [IX_EmployeeRequests_CompanyId_Status] ON [EmployeeRequests] ([CompanyId], [Status]);


-- 20. Company administrators get owner oversight.
INSERT INTO [AppUserPermissions] ([AppUserId], [PermissionKey], [GrantedAt])
SELECT DISTINCT c.[AppUserId], N'Owner.Oversight', SYSUTCDATETIME()
FROM [AppUserCompanies] c
WHERE c.[IsCompanyAdmin] = 1
  AND NOT EXISTS (SELECT 1 FROM [AppUserPermissions] p WHERE p.[AppUserId] = c.[AppUserId] AND p.[PermissionKey] = N'Owner.Oversight');


-- 21. Company administrators may approve payments.
INSERT INTO [AppUserPermissions] ([AppUserId], [PermissionKey], [GrantedAt])
SELECT DISTINCT c.[AppUserId], N'Payments.Approve', SYSUTCDATETIME()
FROM [AppUserCompanies] c
WHERE c.[IsCompanyAdmin] = 1
  AND NOT EXISTS (SELECT 1 FROM [AppUserPermissions] p WHERE p.[AppUserId] = c.[AppUserId] AND p.[PermissionKey] = N'Payments.Approve');


-- 22. Check: expect one row showing 0 payment approvals.
SELECT COUNT(*) AS [PaymentApprovals] FROM [PaymentApprovals];
