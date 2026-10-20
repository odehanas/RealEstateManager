-- ============================================================================
-- 15_AddCompanyAdministrators.sql
--
-- Each company can have its own administrators, who manage that company's users
-- and their permissions. System Admins stay above them and reach every company.
--
-- Adds one column: AppUserCompanies.IsCompanyAdmin - "this user administers
-- this company". It starts OFF for everyone, so nothing changes until a System
-- Admin ticks "Administrator of this company" for someone in Users & Permissions.
-- From the new build on, whoever registers a new company becomes its administrator
-- automatically.
--
-- HOW TO RUN in site4now SQL Studio: run ONE numbered statement at a time, top
-- to bottom. The console executes only the first batch, so pasting the whole
-- file runs statement 1 and nothing else.
--
-- Run statement 1 BEFORE deploying the new build: the application reads this
-- column on every page and will error without it.
-- ============================================================================


-- 1. The administrator flag, off for every existing user.
ALTER TABLE [AppUserCompanies] ADD [IsCompanyAdmin] bit NOT NULL CONSTRAINT [DF_AppUserCompanies_IsCompanyAdmin] DEFAULT (0);


-- 2. CHECK (changes nothing): every company and its ordinary users, to choose who
-- should administer each one. Then, signed in as ADMIN, open Users & Permissions,
-- edit that user and tick "Administrator of this company".
SELECT c.Name AS Company, u.Username, u.DisplayName, u.IsActive,
       uc.IsCompanyAdmin AS IsAdministratorNow
FROM AppUserCompanies uc
JOIN AppUsers u ON u.Id = uc.AppUserId
JOIN Companies c ON c.Id = uc.CompanyId
WHERE u.IsSystemAdmin = 0
ORDER BY c.Name, u.Username;
