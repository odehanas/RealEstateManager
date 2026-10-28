-- ============================================================================
-- 39_AccountStatementLine.sql
--
-- The income statement line of each revenue and expense account (rental income,
-- commission, cost of revenue, fixed costs, recurring costs, finance costs...),
-- a classification of its own beside the account type. Empty means: take the
-- parent group's, or the standard default for the account.
--
-- Adds:
--   Accounts.StatementLine
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The income statement line.
IF COL_LENGTH('Accounts', 'StatementLine') IS NULL ALTER TABLE [Accounts] ADD [StatementLine] nvarchar(30) NULL;
