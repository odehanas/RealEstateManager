-- ============================================================================
-- 48_ManagementAndPlannedMaintenance.sql
--
--   * Maintenance jobs that no tenant asked for: a vacant unit made ready for the
--     next tenant, an inspection, a repair found on a visit. The tenant on a job
--     becomes optional, and a job says what it is for.
--   * The planned maintenance programme: work due on a schedule (the AC every three
--     months, the fire system every six), each entry opening its own job a few days
--     before it is due.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
--
-- Statements 1 to 3 make TenantServiceRequests.TenantId nullable. SQL Server will
-- not change a column that an index or a foreign key uses, so these drop that index
-- and that key first (whatever they were named when the table was created), and
-- statements 4 and 5 put them back. They run only while the column is still NOT NULL.
-- @allow-risky-statements
-- ============================================================================


-- 1. Take off the foreign key from the request to its tenant.
IF COLUMNPROPERTY(OBJECT_ID('TenantServiceRequests'), 'TenantId', 'AllowsNull') = 0
BEGIN
    DECLARE @dropKeys nvarchar(max) = N'';
    SELECT @dropKeys = @dropKeys + N'ALTER TABLE [TenantServiceRequests] DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
    FROM sys.foreign_keys fk
    JOIN sys.foreign_key_columns fc ON fc.constraint_object_id = fk.object_id
    WHERE fk.parent_object_id = OBJECT_ID('TenantServiceRequests')
      AND fc.parent_column_id = COLUMNPROPERTY(OBJECT_ID('TenantServiceRequests'), 'TenantId', 'ColumnId');
    IF @dropKeys <> N'' EXEC sp_executesql @dropKeys;
END;


-- 2. Take off the indexes that include the tenant.
IF COLUMNPROPERTY(OBJECT_ID('TenantServiceRequests'), 'TenantId', 'AllowsNull') = 0
BEGIN
    DECLARE @dropIndexes nvarchar(max) = N'';
    SELECT @dropIndexes = @dropIndexes + N'DROP INDEX ' + QUOTENAME(i.name) + N' ON [TenantServiceRequests];'
    FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID('TenantServiceRequests') AND i.is_primary_key = 0 AND i.name IS NOT NULL
      AND EXISTS (SELECT 1 FROM sys.index_columns ic
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                    AND ic.column_id = COLUMNPROPERTY(OBJECT_ID('TenantServiceRequests'), 'TenantId', 'ColumnId'));
    IF @dropIndexes <> N'' EXEC sp_executesql @dropIndexes;
END;


-- 3. A job need not have a tenant.
IF COLUMNPROPERTY(OBJECT_ID('TenantServiceRequests'), 'TenantId', 'AllowsNull') = 0 ALTER TABLE [TenantServiceRequests] ALTER COLUMN [TenantId] int NULL;


-- 4. The tenant's own list is found by tenant, newest first, as before.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TSR_TenantId_SubmittedAt' AND object_id = OBJECT_ID('TenantServiceRequests')) CREATE INDEX [IX_TSR_TenantId_SubmittedAt] ON [TenantServiceRequests] ([TenantId], [SubmittedAt]);


-- 5. When there is a tenant, it is a real one.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('TenantServiceRequests') AND referenced_object_id = OBJECT_ID('Tenants')) ALTER TABLE [TenantServiceRequests] ADD CONSTRAINT [FK_TSR_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]);


-- 6. What a job raised by management is for: make ready, repair, inspection...
IF COL_LENGTH('TenantServiceRequests', 'Purpose') IS NULL ALTER TABLE [TenantServiceRequests] ADD [Purpose] nvarchar(30) NULL;


-- 7. The planned maintenance programme.
IF OBJECT_ID('MaintenancePlans', 'U') IS NULL CREATE TABLE [MaintenancePlans] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Description] nvarchar(2000) NULL,
    [PropertyId] int NOT NULL,
    [UnitId] int NULL,
    [Category] nvarchar(50) NULL,
    [Priority] nvarchar(20) NOT NULL,
    [IntervalMonths] int NOT NULL,
    [NextDueDate] datetime2 NOT NULL,
    [LeadDays] int NOT NULL,
    [AssignToAppUserId] int NULL,
    [IsActive] bit NOT NULL,
    [LastJobOpenedAt] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_MaintenancePlans] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_MaintenancePlans_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_MaintenancePlans_Properties_PropertyId] FOREIGN KEY ([PropertyId]) REFERENCES [Properties] ([Id]),
    CONSTRAINT [FK_MaintenancePlans_Units_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Units] ([Id]),
    CONSTRAINT [CK_MaintenancePlans_IntervalMonths] CHECK ([IntervalMonths] BETWEEN 1 AND 24),
    CONSTRAINT [CK_MaintenancePlans_LeadDays] CHECK ([LeadDays] BETWEEN 0 AND 60)
);


-- 8. Plans that are due are found by date.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MaintenancePlans_CompanyId_IsActive_NextDueDate' AND object_id = OBJECT_ID('MaintenancePlans')) CREATE INDEX [IX_MaintenancePlans_CompanyId_IsActive_NextDueDate] ON [MaintenancePlans] ([CompanyId], [IsActive], [NextDueDate]);


-- 9. The plan a job came from.
IF COL_LENGTH('TenantServiceRequests', 'MaintenancePlanId') IS NULL ALTER TABLE [TenantServiceRequests] ADD [MaintenancePlanId] int NULL;


-- 10. It must be a real plan.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TenantServiceRequests_MaintenancePlans_MaintenancePlanId') ALTER TABLE [TenantServiceRequests] ADD CONSTRAINT [FK_TenantServiceRequests_MaintenancePlans_MaintenancePlanId] FOREIGN KEY ([MaintenancePlanId]) REFERENCES [MaintenancePlans] ([Id]);


-- 11. A plan's jobs are found from the plan.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TenantServiceRequests_MaintenancePlanId' AND object_id = OBJECT_ID('TenantServiceRequests')) CREATE INDEX [IX_TenantServiceRequests_MaintenancePlanId] ON [TenantServiceRequests] ([MaintenancePlanId]);
