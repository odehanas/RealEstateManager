-- =============================================================================
-- Opening Balances Upgrade
-- =============================================================================
-- Purpose: Introduce sub-ledger opening balance fields so that the
-- Accounts Receivable, Accounts Payable and Fixed Asset GL control accounts
-- have a verifiable detailed sub-ledger source for their opening balance.
--
-- The existing Account.OpeningBalance column remains the canonical GL value
-- for all "non-control" accounts. For control accounts, the opening balance
-- is computed at runtime by summing the per-entity opening balance below
-- and is therefore guaranteed to reconcile to the sub-ledger detail.
--
-- Idempotent: safe to re-run; checks each column before adding it.
-- =============================================================================

SET XACT_ABORT ON;
SET NOCOUNT ON;

------------------------------------------------------------------------------
-- 1) Tenants.OpeningBalance  (Accounts Receivable sub-ledger detail)
------------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1
      FROM sys.columns
     WHERE object_id = OBJECT_ID(N'dbo.Tenants')
       AND name = N'OpeningBalance')
BEGIN
    ALTER TABLE dbo.Tenants
        ADD OpeningBalance DECIMAL(18, 2) NOT NULL
            CONSTRAINT DF_Tenants_OpeningBalance DEFAULT (0);

    PRINT 'Added Tenants.OpeningBalance';
END
ELSE
BEGIN
    PRINT 'Tenants.OpeningBalance already exists - skipped.';
END

------------------------------------------------------------------------------
-- 2) Vendors.OpeningBalance  (Accounts Payable sub-ledger detail)
------------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1
      FROM sys.columns
     WHERE object_id = OBJECT_ID(N'dbo.Vendors')
       AND name = N'OpeningBalance')
BEGIN
    ALTER TABLE dbo.Vendors
        ADD OpeningBalance DECIMAL(18, 2) NOT NULL
            CONSTRAINT DF_Vendors_OpeningBalance DEFAULT (0);

    PRINT 'Added Vendors.OpeningBalance';
END
ELSE
BEGIN
    PRINT 'Vendors.OpeningBalance already exists - skipped.';
END

------------------------------------------------------------------------------
-- 3) FixedAssets.OpeningCost & OpeningAccumulatedDepreciation
--    (Fixed Asset sub-ledger detail, both cost and accumulated depreciation)
------------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1
      FROM sys.columns
     WHERE object_id = OBJECT_ID(N'dbo.FixedAssets')
       AND name = N'OpeningCost')
BEGIN
    ALTER TABLE dbo.FixedAssets
        ADD OpeningCost DECIMAL(18, 2) NOT NULL
            CONSTRAINT DF_FixedAssets_OpeningCost DEFAULT (0);

    PRINT 'Added FixedAssets.OpeningCost';
END
ELSE
BEGIN
    PRINT 'FixedAssets.OpeningCost already exists - skipped.';
END

IF NOT EXISTS (
    SELECT 1
      FROM sys.columns
     WHERE object_id = OBJECT_ID(N'dbo.FixedAssets')
       AND name = N'OpeningAccumulatedDepreciation')
BEGIN
    ALTER TABLE dbo.FixedAssets
        ADD OpeningAccumulatedDepreciation DECIMAL(18, 2) NOT NULL
            CONSTRAINT DF_FixedAssets_OpeningAccumDep DEFAULT (0);

    PRINT 'Added FixedAssets.OpeningAccumulatedDepreciation';
END
ELSE
BEGIN
    PRINT 'FixedAssets.OpeningAccumulatedDepreciation already exists - skipped.';
END

PRINT 'Opening Balances upgrade completed successfully.';
