-- ============================================================================
-- 30_AddRolesAndContractLimits.sql
--
-- Standard user roles (Owner, Manager, Data Entry) and the Business Performance
-- page; approval limits can now also apply to tenancy contracts.
--
-- Adds:
--   Companies          ApplyLimitsToContracts (off by default)
--   AppUserPermissions Performance.View for company administrators and owners
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. Approval limits on contracts, off until switched on in Approval Limits.
IF COL_LENGTH('Companies', 'ApplyLimitsToContracts') IS NULL ALTER TABLE [Companies] ADD [ApplyLimitsToContracts] bit NOT NULL CONSTRAINT [DF_Companies_ApplyLimitsToContracts] DEFAULT 0;


-- 2. Company administrators see Business Performance.
INSERT INTO [AppUserPermissions] ([AppUserId], [PermissionKey], [GrantedAt])
SELECT DISTINCT c.[AppUserId], N'Performance.View', SYSUTCDATETIME()
FROM [AppUserCompanies] c
WHERE c.[IsCompanyAdmin] = 1
  AND NOT EXISTS (SELECT 1 FROM [AppUserPermissions] p WHERE p.[AppUserId] = c.[AppUserId] AND p.[PermissionKey] = N'Performance.View');


-- 3. So does everyone who already has owner oversight.
INSERT INTO [AppUserPermissions] ([AppUserId], [PermissionKey], [GrantedAt])
SELECT DISTINCT o.[AppUserId], N'Performance.View', SYSUTCDATETIME()
FROM [AppUserPermissions] o
WHERE o.[PermissionKey] = N'Owner.Oversight'
  AND NOT EXISTS (SELECT 1 FROM [AppUserPermissions] p WHERE p.[AppUserId] = o.[AppUserId] AND p.[PermissionKey] = N'Performance.View');


-- 4. Check: expect one row showing how many users can see Business Performance.
SELECT COUNT(*) AS [PerformanceViewers] FROM [AppUserPermissions] WHERE [PermissionKey] = N'Performance.View';
