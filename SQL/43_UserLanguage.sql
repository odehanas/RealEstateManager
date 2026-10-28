-- ============================================================================
-- 43_UserLanguage.sql
--
-- Each user's screen language, English or Arabic, chosen from the switch in the
-- top bar and applied when they sign in on any device.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The user's language: en or ar (null until they choose).
IF COL_LENGTH('AppUsers', 'PreferredLanguage') IS NULL ALTER TABLE [AppUsers] ADD [PreferredLanguage] nvarchar(5) NULL;
