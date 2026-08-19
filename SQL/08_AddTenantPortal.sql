-- =============================================================================
-- 08_AddTenantPortal.sql
-- -----------------------------------------------------------------------------
-- Phase 1 of the tenant self-service portal. One new table, nothing else changed.
--
--   [TenantPortalUsers]   tenant logins, entirely separate from [AppUsers]
--
-- Why a separate table rather than a flag on AppUsers: staff permissions are rows
-- in AppUserPermissions, enforced by a controller-to-permission map, and any
-- controller missing from that map is open to every authenticated staff user. If
-- tenants lived in AppUsers, one forgotten map entry would expose the company's
-- financial screens to a tenant. With a separate table there is nowhere to put a
-- staff permission, so it cannot happen by accident.
--
-- Purely additive. No existing table, column or row is touched.
--
-- HOW TO RUN
--   Your console executes only the FIRST batch, so run each statement separately.
--
-- AFTER RUNNING
--   Deploy and restart, then open Master Data > Tenants > Portal for any tenant
--   and choose "Issue portal access". The temporary password is shown once - copy
--   it before leaving the page. The tenant signs in at /portal and is required to
--   change it immediately.
-- =============================================================================


-- STATEMENT 1 -- the table.
CREATE TABLE [dbo].[TenantPortalUsers]
(
    [Id]                  INT            NOT NULL IDENTITY(1,1),
    [CompanyId]           INT            NOT NULL,
    [TenantId]            INT            NOT NULL,
    [Username]            NVARCHAR(200)  NOT NULL,
    [PasswordHash]        NVARCHAR(500)  NOT NULL,
    [IsActive]            BIT            NOT NULL CONSTRAINT [DF_TenantPortalUsers_IsActive] DEFAULT (1),
    [MustChangePassword]  BIT            NOT NULL CONSTRAINT [DF_TenantPortalUsers_MustChange] DEFAULT (1),
    [FailedLoginAttempts] INT            NOT NULL CONSTRAINT [DF_TenantPortalUsers_Failed] DEFAULT (0),
    [LockedOutUntil]      DATETIME2      NULL,
    [CreatedAt]           DATETIME2      NOT NULL CONSTRAINT [DF_TenantPortalUsers_Created] DEFAULT (SYSUTCDATETIME()),
    [LastLoginAt]         DATETIME2      NULL,
    [CreatedByAppUserId]  INT            NULL,
    CONSTRAINT [PK_TenantPortalUsers] PRIMARY KEY CLUSTERED ([Id] ASC)
);


-- STATEMENT 2 -- scope to a company.
ALTER TABLE [dbo].[TenantPortalUsers] WITH CHECK
    ADD CONSTRAINT [FK_TenantPortalUsers_Companies_CompanyId]
    FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id]);


-- STATEMENT 3 -- link to the tenant.
ALTER TABLE [dbo].[TenantPortalUsers] WITH CHECK
    ADD CONSTRAINT [FK_TenantPortalUsers_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);


-- STATEMENT 4 -- usernames are unique across the whole system. A tenant types only
-- a username at sign-in, with no company to disambiguate it.
CREATE UNIQUE INDEX [IX_TenantPortalUsers_Username]
    ON [dbo].[TenantPortalUsers] ([Username] ASC);


-- STATEMENT 5 -- one portal login per tenant.
CREATE UNIQUE INDEX [IX_TenantPortalUsers_TenantId]
    ON [dbo].[TenantPortalUsers] ([TenantId] ASC);


-- STATEMENT 6 -- verify. Expected: TableExists YES, ColumnCount 12, IndexCount 2,
-- ForeignKeyCount 2, RowCount_ 0.
SELECT
    TableExists     = CASE WHEN OBJECT_ID(N'[dbo].[TenantPortalUsers]', N'U') IS NULL THEN 'NO' ELSE 'YES' END,
    ColumnCount     = (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[TenantPortalUsers]')),
    IndexCount      = (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[TenantPortalUsers]') AND is_primary_key = 0),
    ForeignKeyCount = (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'[dbo].[TenantPortalUsers]')),
    RowCount_       = (SELECT COUNT(*) FROM [dbo].[TenantPortalUsers]);
