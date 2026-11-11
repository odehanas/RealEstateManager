-- ============================================================================
-- 52_RequestTranslation.sql
--
-- A tenant may write a request, or a message on it, in any language. The office
-- and the maintenance team read it in English beside the tenant's own words:
-- the English is kept next to the original, never in place of it.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The language the tenant wrote the request in, as detected.
IF COL_LENGTH('TenantServiceRequests', 'OriginalLanguage') IS NULL ALTER TABLE [TenantServiceRequests] ADD [OriginalLanguage] nvarchar(40) NULL;


-- 2. The request's title in English.
IF COL_LENGTH('TenantServiceRequests', 'SubjectEnglish') IS NULL ALTER TABLE [TenantServiceRequests] ADD [SubjectEnglish] nvarchar(400) NULL;


-- 3. The request's text in English.
IF COL_LENGTH('TenantServiceRequests', 'DescriptionEnglish') IS NULL ALTER TABLE [TenantServiceRequests] ADD [DescriptionEnglish] nvarchar(max) NULL;


-- 4. Where the translation stands: Pending, Done, NotNeeded (already English) or Failed.
IF COL_LENGTH('TenantServiceRequests', 'TranslationStatus') IS NULL ALTER TABLE [TenantServiceRequests] ADD [TranslationStatus] nvarchar(20) NULL;


-- 5. The language a message on a request was written in.
IF COL_LENGTH('TenantServiceRequestComments', 'OriginalLanguage') IS NULL ALTER TABLE [TenantServiceRequestComments] ADD [OriginalLanguage] nvarchar(40) NULL;


-- 6. The message in English, when it was not written in English.
IF COL_LENGTH('TenantServiceRequestComments', 'BodyEnglish') IS NULL ALTER TABLE [TenantServiceRequestComments] ADD [BodyEnglish] nvarchar(max) NULL;


-- 7. Whether the message has been looked at for translation.
IF COL_LENGTH('TenantServiceRequestComments', 'TranslationStatus') IS NULL ALTER TABLE [TenantServiceRequestComments] ADD [TranslationStatus] nvarchar(20) NULL;
