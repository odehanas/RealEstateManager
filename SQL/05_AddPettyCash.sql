-- =============================================================================
-- 05_AddPettyCash.sql
-- -----------------------------------------------------------------------------
-- One schema change for the petty cash module:
--
--   [Accounts].[IsPettyCashAccount]   new bit column, default 0
--
-- That is all. Petty cash vouchers are ordinary journal entries, so no new tables
-- and no new journal columns are needed. A float is simply an asset account with
-- this flag set, and its opening balance is the account's own OpeningBalance.
--
-- Purely additive. Every existing account keeps IsPettyCashAccount = 0 and is
-- otherwise untouched.
--
-- HOW TO RUN
--   Your console executes only the FIRST batch of whatever you paste, so run the
--   two statements below ONE AT A TIME.
--
-- AFTER RUNNING
--   Deploy the new build and restart. ChartOfAccountsSeeder will create asset
--   account 11150 "Petty Cash" with the flag set, for every company. Then open
--   Setup > Chart of Accounts, edit 11150 and enter the cash you started with as
--   its Opening Balance.
-- =============================================================================


-- STATEMENT 1 -- add the flag column.
ALTER TABLE [dbo].[Accounts] ADD [IsPettyCashAccount] BIT NOT NULL CONSTRAINT [DF_Accounts_IsPettyCashAccount] DEFAULT (0);


-- STATEMENT 2 -- confirm. Expected: EXISTS.
SELECT PettyCashColumn = CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Accounts]') AND name = N'IsPettyCashAccount') THEN 'EXISTS' ELSE 'MISSING' END;


-- After deploying and restarting, this should return account 11150:
-- SELECT Code, Name, IsPettyCashAccount, OpeningBalance FROM Accounts WHERE IsPettyCashAccount = 1;
