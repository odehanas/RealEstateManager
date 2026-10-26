-- ============================================================================
-- 32_LinkBillsToDevelopmentProjects.sql
--
-- A vendor or subcontractor bill can be linked as a whole to a development
-- project (its lines already carry the project); and a project can fix how
-- input VAT on its costs is treated, so recovery is never left to chance.
--
-- Adds:
--   VendorBills          DevelopmentProjectId (optional, with index)
--   DevelopmentProjects  InputTaxAttribution (optional)
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The project a whole bill belongs to.
IF COL_LENGTH('VendorBills', 'DevelopmentProjectId') IS NULL ALTER TABLE [VendorBills] ADD [DevelopmentProjectId] int NULL;


-- 2. It must be a real project.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_VendorBills_DevelopmentProjects_DevelopmentProjectId') ALTER TABLE [VendorBills] ADD CONSTRAINT [FK_VendorBills_DevelopmentProjects_DevelopmentProjectId] FOREIGN KEY ([DevelopmentProjectId]) REFERENCES [DevelopmentProjects] ([Id]);


-- 3. Bills are looked up by project.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorBills_DevelopmentProjectId') CREATE INDEX [IX_VendorBills_DevelopmentProjectId] ON [VendorBills] ([DevelopmentProjectId]);


-- 4. How input VAT on the project's costs is treated (Taxable, Exempt or Residual).
IF COL_LENGTH('DevelopmentProjects', 'InputTaxAttribution') IS NULL ALTER TABLE [DevelopmentProjects] ADD [InputTaxAttribution] nvarchar(20) NULL;


-- 5. Check: expect one row showing how many bills are linked to a project.
SELECT COUNT(*) AS [BillsLinkedToProjects] FROM [VendorBills] WHERE [DevelopmentProjectId] IS NOT NULL;
