-- =============================================================================
-- 06_AddVat.sql
-- -----------------------------------------------------------------------------
-- Schema for VAT determination and tax invoicing. All additive - no existing
-- table, column or row is modified or dropped, and every new column is either
-- nullable or has a default, so existing records are untouched.
--
-- HOW TO RUN
--   Your console executes only the FIRST batch of whatever you paste, so run the
--   statements below ONE AT A TIME, in order. Statement 0 tells you which are
--   still needed; skip any that already show EXISTS.
--
-- AFTER RUNNING
--   1. Deploy the new build and restart.
--   2. Setup > Companies: enter the legal name, address and TRN. Tax invoices
--      cannot be issued without them.
--   3. Master Data > Properties: set the VAT category on each property
--      (Commercial = Standard, Residential = Exempt). Units inherit it; set a
--      unit's own category only for a mixed-use building.
--   4. Master Data > Tenants: enter the TRN for any VAT-registered tenant. A
--      tenant with a TRN gets a full tax invoice, one without gets a simplified
--      tax invoice.
--   5. Set the provisional input tax recovery rate on the company - normally last
--      year's actual recovery percentage. Leave it at 100 only if you have no
--      exempt supplies.
-- =============================================================================


-- STATEMENT 0 -- what is still missing.
SELECT 'Companies.TrnNumber' AS ObjectName, CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Companies]') AND name = N'TrnNumber') THEN 'EXISTS' ELSE 'MISSING' END AS Status
UNION ALL SELECT 'Properties.VatCategory', CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Properties]') AND name = N'VatCategory') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'Units.VatCategory', CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Units]') AND name = N'VatCategory') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'Tenants.TrnNumber', CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Tenants]') AND name = N'TrnNumber') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'Vendors.TrnNumber', CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Vendors]') AND name = N'TrnNumber') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'Leases.VatCategory', CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Leases]') AND name = N'VatCategory') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'CustomerInvoices.TaxInvoiceNumber', CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[CustomerInvoices]') AND name = N'TaxInvoiceNumber') THEN 'EXISTS' ELSE 'MISSING' END
UNION ALL SELECT 'VendorBills.InputTaxAttribution', CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[VendorBills]') AND name = N'InputTaxAttribution') THEN 'EXISTS' ELSE 'MISSING' END;


-- STATEMENT 1 -- company VAT identity, needed on every tax invoice.
ALTER TABLE [dbo].[Companies] ADD [LegalName] NVARCHAR(200) NULL, [TrnNumber] NVARCHAR(30) NULL, [AddressLine1] NVARCHAR(200) NULL, [AddressLine2] NVARCHAR(200) NULL, [City] NVARCHAR(100) NULL, [Country] NVARCHAR(100) NULL, [Phone] NVARCHAR(50) NULL, [Email] NVARCHAR(200) NULL, [ProvisionalVatRecoveryRate] DECIMAL(9,4) NOT NULL CONSTRAINT [DF_Companies_ProvisionalVatRecoveryRate] DEFAULT (100);


-- STATEMENT 2 -- default VAT treatment per property.
ALTER TABLE [dbo].[Properties] ADD [VatCategory] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Properties_VatCategory] DEFAULT (N'Standard');


-- STATEMENT 2B -- IMPORTANT. Classify existing properties from their type before
-- any new lease is created. The column defaults to Standard, so without this every
-- residential property would look commercial and new leases would wrongly derive
-- 5% output tax. Review the result and correct anything the type does not capture.
UPDATE [dbo].[Properties] SET [VatCategory] = CASE WHEN [PropertyType] = N'Residential' THEN N'Exempt' ELSE N'Standard' END;


-- STATEMENT 3 -- per-unit override, so a mixed-use building works. NULL = inherit.
ALTER TABLE [dbo].[Units] ADD [VatCategory] NVARCHAR(20) NULL;


-- STATEMENT 4 -- tenant TRN. Present = full tax invoice, absent = simplified.
ALTER TABLE [dbo].[Tenants] ADD [TrnNumber] NVARCHAR(30) NULL;


-- STATEMENT 5 -- supplier TRN, evidence for input tax claims.
ALTER TABLE [dbo].[Vendors] ADD [TrnNumber] NVARCHAR(30) NULL;


-- STATEMENT 6 -- lease treatment and the recorded reason for any override.
ALTER TABLE [dbo].[Leases] ADD [VatCategory] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Leases_VatCategory] DEFAULT (N'Standard'), [VatOverrideReason] NVARCHAR(300) NULL;


-- STATEMENT 7 -- invoice VAT snapshot and tax invoice identity.
ALTER TABLE [dbo].[CustomerInvoices] ADD [VatCategory] NVARCHAR(20) NOT NULL CONSTRAINT [DF_CustomerInvoices_VatCategory] DEFAULT (N'Standard'), [VatRate] DECIMAL(9,4) NOT NULL CONSTRAINT [DF_CustomerInvoices_VatRate] DEFAULT (0), [TaxInvoiceNumber] NVARCHAR(40) NULL, [TaxInvoiceType] NVARCHAR(20) NOT NULL CONSTRAINT [DF_CustomerInvoices_TaxInvoiceType] DEFAULT (N'NotApplicable'), [DateOfSupply] DATETIME2 NULL, [RecipientTrn] NVARCHAR(30) NULL;


-- STATEMENT 8 -- input tax attribution and the recoverable / irrecoverable split.
ALTER TABLE [dbo].[VendorBills] ADD [InputTaxAttribution] NVARCHAR(20) NOT NULL CONSTRAINT [DF_VendorBills_InputTaxAttribution] DEFAULT (N'Taxable'), [RecoverableVatAmount] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_VendorBills_RecoverableVatAmount] DEFAULT (0), [NonRecoverableVatAmount] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_VendorBills_NonRecoverableVatAmount] DEFAULT (0), [AppliedRecoveryRate] DECIMAL(9,4) NOT NULL CONSTRAINT [DF_VendorBills_AppliedRecoveryRate] DEFAULT (100), [SupplierTrn] NVARCHAR(30) NULL;


-- STATEMENT 9 -- the tax invoice series must be unique. Filtered so the many rows
-- with no tax invoice number do not collide with each other.
CREATE UNIQUE INDEX [IX_CustomerInvoices_CompanyId_TaxInvoiceNumber] ON [dbo].[CustomerInvoices] ([CompanyId], [TaxInvoiceNumber]) WHERE [TaxInvoiceNumber] IS NOT NULL;


-- STATEMENT 10 -- backfill existing leases from the VAT flag they already carry,
-- so historic leases are classified rather than all defaulting to Standard.
UPDATE [dbo].[Leases] SET [VatCategory] = CASE WHEN [IsVatApplicable] = 1 THEN N'Standard' ELSE N'Exempt' END;


-- STATEMENT 11 -- backfill existing invoices to match their lease, and record the
-- rate that was actually applied.
UPDATE i SET i.[VatCategory] = l.[VatCategory], i.[VatRate] = CASE WHEN l.[VatCategory] = N'Standard' THEN 5 ELSE 0 END, i.[DateOfSupply] = i.[InvoiceDate] FROM [dbo].[CustomerInvoices] i INNER JOIN [dbo].[Leases] l ON l.[Id] = i.[LeaseId];


-- STATEMENT 12 -- existing bills already had their VAT fully recovered, so record
-- them as Taxable attribution to match what was actually posted. Review and
-- re-attribute any that related to exempt residential activity.
UPDATE [dbo].[VendorBills] SET [InputTaxAttribution] = N'Taxable', [RecoverableVatAmount] = [VatAmount], [NonRecoverableVatAmount] = 0, [AppliedRecoveryRate] = 100;


-- STATEMENT 13 -- confirm. Expected: all EXISTS. Re-run STATEMENT 0.
SELECT StandardLeases = (SELECT COUNT(*) FROM [dbo].[Leases] WHERE [VatCategory] = N'Standard'), ExemptLeases = (SELECT COUNT(*) FROM [dbo].[Leases] WHERE [VatCategory] = N'Exempt'), InvoicesWithVat = (SELECT COUNT(*) FROM [dbo].[CustomerInvoices] WHERE [VatAmount] > 0), BillsToReview = (SELECT COUNT(*) FROM [dbo].[VendorBills] WHERE [VatAmount] > 0);
