-- ============================================================================
-- 12_AddTenantServiceRequests.sql
--
-- Tenant self-service, phase 2: the request side.
--
-- Phase 1 let a tenant LOOK at their account - balance, invoices, payments,
-- statement, contracts. This adds the ability to ASK for something: a repair,
-- more time to pay an invoice, a renewal, a complaint, or anything else.
--
-- Adds two tables:
--   TenantServiceRequests          one request, with its workflow state
--   TenantServiceRequestComments   the conversation between tenant and staff
--
-- Nothing in here touches the ledger. A request is a conversation, not a
-- transaction. The one financial side effect in the whole feature is a member of
-- staff pressing "apply new due date", which moves ONE invoice's DueDate after
-- they have agreed to it - no amount, no VAT and no journal entry is affected.
--
-- HOW TO RUN in site4now SQL Studio: run ONE numbered statement at a time, top
-- to bottom. The console executes only the first batch, so pasting the whole
-- file runs statement 1 and nothing else.
--
-- Run every statement BEFORE deploying the new build. The application reads
-- these tables and the portal will error without them.
-- ============================================================================


-- 1. The request itself.
CREATE TABLE [TenantServiceRequests] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [TenantId] int NOT NULL,
    [LeaseId] int NULL,
    [PropertyId] int NULL,
    [UnitId] int NULL,
    [RequestNumber] nvarchar(30) NOT NULL,
    [RequestType] nvarchar(30) NOT NULL,
    [Category] nvarchar(50) NULL,
    [Subject] nvarchar(200) NOT NULL,
    [Description] nvarchar(4000) NOT NULL,
    [Priority] nvarchar(20) NOT NULL,
    [PreferredVisitDate] datetime2 NULL,
    [RelatedInvoiceId] int NULL,
    [RequestedNewDueDate] datetime2 NULL,
    [PostponementApplied] bit NOT NULL CONSTRAINT [DF_TSR_PostponementApplied] DEFAULT (0),
    [PostponementAppliedAt] datetime2 NULL,
    [PostponementAppliedByAppUserId] int NULL,
    [Status] nvarchar(30) NOT NULL,
    [SubmittedAt] datetime2 NOT NULL,
    [SubmittedByPortalUserId] int NULL,
    [AcknowledgedAt] datetime2 NULL,
    [ResolvedAt] datetime2 NULL,
    [ClosedAt] datetime2 NULL,
    [AssignedToAppUserId] int NULL,
    [ResolutionNotes] nvarchar(4000) NULL,
    CONSTRAINT [PK_TenantServiceRequests] PRIMARY KEY ([Id])
);


-- 2. The company the request belongs to.
ALTER TABLE [TenantServiceRequests] ADD CONSTRAINT [FK_TSR_Companies]
    FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]);


-- 3. The tenant who raised it.
ALTER TABLE [TenantServiceRequests] ADD CONSTRAINT [FK_TSR_Tenants]
    FOREIGN KEY ([TenantId]) REFERENCES [Tenants]([Id]);


-- 4. The contract it concerns, if any.
ALTER TABLE [TenantServiceRequests] ADD CONSTRAINT [FK_TSR_Leases]
    FOREIGN KEY ([LeaseId]) REFERENCES [Leases]([Id]);


-- 5. Property, copied from the contract when the request is raised so the record
--    still says which building it was about after the tenancy ends.
ALTER TABLE [TenantServiceRequests] ADD CONSTRAINT [FK_TSR_Properties]
    FOREIGN KEY ([PropertyId]) REFERENCES [Properties]([Id]);


-- 6. Unit, same reasoning.
ALTER TABLE [TenantServiceRequests] ADD CONSTRAINT [FK_TSR_Units]
    FOREIGN KEY ([UnitId]) REFERENCES [Units]([Id]);


-- 7. The invoice a postponement request refers to.
ALTER TABLE [TenantServiceRequests] ADD CONSTRAINT [FK_TSR_CustomerInvoices]
    FOREIGN KEY ([RelatedInvoiceId]) REFERENCES [CustomerInvoices]([Id]);


-- 8. Request numbers are one sequence per company: REQ-2026-0001.
CREATE UNIQUE INDEX [IX_TSR_CompanyId_RequestNumber]
    ON [TenantServiceRequests] ([CompanyId], [RequestNumber]);


-- 9. The staff queue filters on status.
CREATE INDEX [IX_TSR_CompanyId_Status]
    ON [TenantServiceRequests] ([CompanyId], [Status]);


-- 10. The tenant's own list filters on tenant, newest first.
CREATE INDEX [IX_TSR_TenantId_SubmittedAt]
    ON [TenantServiceRequests] ([TenantId], [SubmittedAt]);


-- 11. The conversation. Both sides write here, and so does the system: every
--     status change leaves a line, which is what makes the thread the audit trail.
--     IsInternal marks a note staff wrote to each other; the portal filters those
--     out in the query, never in the page.
CREATE TABLE [TenantServiceRequestComments] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [TenantServiceRequestId] int NOT NULL,
    [AuthorType] nvarchar(20) NOT NULL,
    [AuthorName] nvarchar(200) NOT NULL,
    [AppUserId] int NULL,
    [TenantPortalUserId] int NULL,
    [Body] nvarchar(4000) NOT NULL,
    [IsInternal] bit NOT NULL CONSTRAINT [DF_TSRC_IsInternal] DEFAULT (0),
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_TenantServiceRequestComments] PRIMARY KEY ([Id])
);


-- 12. A comment has no meaning apart from its request, so it goes with it.
ALTER TABLE [TenantServiceRequestComments] ADD CONSTRAINT [FK_TSRC_Requests]
    FOREIGN KEY ([TenantServiceRequestId]) REFERENCES [TenantServiceRequests]([Id]) ON DELETE CASCADE;


-- 13. Threads are always read in order.
CREATE INDEX [IX_TSRC_RequestId_CreatedAt]
    ON [TenantServiceRequestComments] ([TenantServiceRequestId], [CreatedAt]);


-- 14. VERIFY - both tables and all five indexes should be listed. Expect 5 rows.
SELECT i.name AS IndexName, t.name AS TableName, i.is_unique AS IsUnique
FROM sys.indexes i
INNER JOIN sys.tables t ON t.object_id = i.object_id
WHERE t.name IN ('TenantServiceRequests', 'TenantServiceRequestComments')
  AND i.name IS NOT NULL
  AND i.is_primary_key = 0
ORDER BY t.name, i.name;


-- 15. GRANT THE NEW PERMISSION. Staff who should answer tenant requests need
--     'ServiceRequests.Manage'. You can grant it from System Setup > Users &
--     Permissions instead of running this - but if you prefer SQL, replace the
--     username below with the real one and run it once per person.
--
--     A System Administrator already bypasses all permission checks and does not
--     need this row.
INSERT INTO [AppUserPermissions] ([AppUserId], [PermissionKey], [GrantedAt])
SELECT u.[Id], 'ServiceRequests.Manage', SYSUTCDATETIME()
FROM [AppUsers] u
WHERE u.[Username] = 'REPLACE_WITH_USERNAME'
  AND NOT EXISTS (SELECT 1 FROM [AppUserPermissions] p
                  WHERE p.[AppUserId] = u.[Id] AND p.[PermissionKey] = 'ServiceRequests.Manage');
