-- ============================================================================
-- 60_CompanyBranches.sql
--
-- Branches: a company's administrator opens a branch under the mother company.
-- The branch keeps its own books; the group reports add the mother company and
-- its branches together (combined) and remove what they did with one another
-- (consolidated). An account is marked intra-group to say its balance is with
-- another company of the group.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The mother company of a branch. Empty for a company that is not a branch.
IF COL_LENGTH('Companies', 'ParentCompanyId') IS NULL ALTER TABLE [Companies] ADD [ParentCompanyId] int NULL;

-- 2. A branch always points at a company that exists.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Companies_ParentCompany') ALTER TABLE [Companies] ADD CONSTRAINT [FK_Companies_ParentCompany] FOREIGN KEY ([ParentCompanyId]) REFERENCES [Companies] ([Id]);

-- 3. The branches of a company, found quickly.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Companies_ParentCompanyId' AND object_id = OBJECT_ID('Companies')) CREATE INDEX [IX_Companies_ParentCompanyId] ON [Companies] ([ParentCompanyId]);

-- 4. Marks an account whose balance is with another company of the group; it is removed on consolidation.
IF COL_LENGTH('Accounts', 'IsIntraGroup') IS NULL ALTER TABLE [Accounts] ADD [IsIntraGroup] bit NOT NULL CONSTRAINT [DF_Accounts_IsIntraGroup] DEFAULT 0;
