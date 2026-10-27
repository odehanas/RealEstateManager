-- ============================================================================
-- 37_OwnerBilledRent.sql
--
-- An option for managed properties: tenants are invoiced in the OWNER's name,
-- the company acting as the owner's agent. The owner is then the supplier: the
-- invoice carries the owner's TRN (or no VAT when the owner is not registered),
-- the VAT on the rent belongs to the owner, and it stays off the company's own
-- VAT return. The company still issues its own tax invoice to the owner for the
-- commission. Off unless switched on for a property.
--
-- Adds:
--   Properties.RentBilledInOwnerName     the option
--   CustomerInvoices.IssuedForOwnerName  who the invoice was issued for, kept as
--   CustomerInvoices.IssuedForOwnerTrn   it was when posted
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The option on the property.
IF COL_LENGTH('Properties', 'RentBilledInOwnerName') IS NULL ALTER TABLE [Properties] ADD [RentBilledInOwnerName] bit NOT NULL CONSTRAINT [DF_Properties_RentBilledInOwnerName] DEFAULT (0);


-- 2. The owner an invoice was issued for.
IF COL_LENGTH('CustomerInvoices', 'IssuedForOwnerName') IS NULL ALTER TABLE [CustomerInvoices] ADD [IssuedForOwnerName] nvarchar(200) NULL;


-- 3. The owner's TRN at the time.
IF COL_LENGTH('CustomerInvoices', 'IssuedForOwnerTrn') IS NULL ALTER TABLE [CustomerInvoices] ADD [IssuedForOwnerTrn] nvarchar(20) NULL;
