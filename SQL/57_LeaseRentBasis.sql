-- ============================================================================
-- 57_LeaseRentBasis.sql
--
-- Short-term letting: a contract may be priced at a rate per day, week or month
-- instead of a yearly rent, and is then billed for exactly the days of the stay.
--
--   * Leases.RentBasis - Annual (every existing contract), Monthly, Weekly, Daily.
--   * Leases.RentRate  - the rate per day, week or month; 0 for an annual contract.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. How the contract's rent is agreed.
IF COL_LENGTH('Leases', 'RentBasis') IS NULL ALTER TABLE [Leases] ADD [RentBasis] nvarchar(10) NOT NULL CONSTRAINT [DF_Leases_RentBasis] DEFAULT 'Annual';


-- 2. The rate per day, week or month of a stay.
IF COL_LENGTH('Leases', 'RentRate') IS NULL ALTER TABLE [Leases] ADD [RentRate] decimal(18,2) NOT NULL CONSTRAINT [DF_Leases_RentRate] DEFAULT 0;
