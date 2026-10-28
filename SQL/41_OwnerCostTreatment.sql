-- ============================================================================
-- 41_OwnerCostTreatment.sql
--
-- How costs incurred for owners' managed properties are passed on, chosen per
-- company: Recharge (the company's own supply, 5% VAT) or Disbursement (paid on
-- the owner's behalf, no VAT, the supplier's VAT is the owner's). And the kind of
-- each such cost (maintenance, utilities, insurance...), kept on the journal line
-- so the owners' profitability can be broken down by it.
--
-- Adds:
--   Companies.OwnerCostTreatment       Recharge or Disbursement
--   VendorBillLines.IsDisbursement     the line was passed on as a disbursement
--   VendorBillLines.OwnerCostCategory  the kind of cost
--   JournalEntryLines.OwnerCostCategory
--   OwnerRecharges.Treatment, SupplierVatAmount, CostCategory
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The company's choice.
IF COL_LENGTH('Companies', 'OwnerCostTreatment') IS NULL ALTER TABLE [Companies] ADD [OwnerCostTreatment] nvarchar(20) NOT NULL CONSTRAINT [DF_Companies_OwnerCostTreatment] DEFAULT (N'Recharge');


-- 2. A bill line passed on as a disbursement.
IF COL_LENGTH('VendorBillLines', 'IsDisbursement') IS NULL ALTER TABLE [VendorBillLines] ADD [IsDisbursement] bit NOT NULL CONSTRAINT [DF_VendorBillLines_IsDisbursement] DEFAULT (0);


-- 3. The kind of cost on a bill line.
IF COL_LENGTH('VendorBillLines', 'OwnerCostCategory') IS NULL ALTER TABLE [VendorBillLines] ADD [OwnerCostCategory] nvarchar(40) NULL;


-- 4. The kind of cost on the journal line charging it to the owner.
IF COL_LENGTH('JournalEntryLines', 'OwnerCostCategory') IS NULL ALTER TABLE [JournalEntryLines] ADD [OwnerCostCategory] nvarchar(40) NULL;


-- 5. How the cost was passed on.
IF COL_LENGTH('OwnerRecharges', 'Treatment') IS NULL ALTER TABLE [OwnerRecharges] ADD [Treatment] nvarchar(20) NOT NULL CONSTRAINT [DF_OwnerRecharges_Treatment] DEFAULT (N'Recharge');


-- 6. The supplier's VAT on a disbursement (the owner's to reclaim).
IF COL_LENGTH('OwnerRecharges', 'SupplierVatAmount') IS NULL ALTER TABLE [OwnerRecharges] ADD [SupplierVatAmount] decimal(18,2) NOT NULL CONSTRAINT [DF_OwnerRecharges_SupplierVatAmount] DEFAULT (0);


-- 7. The kind of cost.
IF COL_LENGTH('OwnerRecharges', 'CostCategory') IS NULL ALTER TABLE [OwnerRecharges] ADD [CostCategory] nvarchar(40) NULL;
