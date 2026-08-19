-- ============================================================================
-- 10_AddMasterDataUniqueIdentifiers.sql
--
-- Centralised master data, part 2: force the legal identifiers of a party to be
-- unique so the same tenant or supplier cannot be opened twice under two codes.
--
-- Adds:
--   Tenants.TradeLicenseNumber   nvarchar(60) null
--   Vendors.TradeLicenseNumber   nvarchar(60) null
--
-- Then creates FILTERED unique indexes. Filtered, because most records have not
-- captured every identifier: a null or blank value must never collide with
-- another null or blank one. Only supplied values have to be distinct.
--
-- HOW TO RUN in site4now SQL Studio: run ONE numbered statement at a time,
-- top to bottom. The console executes only the first batch, so pasting the whole
-- file silently runs statement 1 and nothing else.
--
-- IMPORTANT - statements 3 to 8 will FAIL if the data already contains
-- duplicates. That failure is the point: it is telling you two records share an
-- identifier. Run statement 9 first to see them, clean them up, then continue.
-- ============================================================================


-- 1. Add the trade licence column to Tenants.
ALTER TABLE [Tenants] ADD [TradeLicenseNumber] nvarchar(60) NULL;


-- 2. Add the trade licence column to Vendors.
ALTER TABLE [Vendors] ADD [TradeLicenseNumber] nvarchar(60) NULL;


-- 3. Tenant trade licence must be unique within a company.
CREATE UNIQUE INDEX [IX_Tenants_CompanyId_TradeLicenseNumber]
    ON [Tenants] ([CompanyId], [TradeLicenseNumber])
    WHERE [TradeLicenseNumber] IS NOT NULL AND [TradeLicenseNumber] <> '';


-- 4. Tenant Emirates ID / ID number must be unique within a company.
CREATE UNIQUE INDEX [IX_Tenants_CompanyId_IdNumber]
    ON [Tenants] ([CompanyId], [IdNumber])
    WHERE [IdNumber] IS NOT NULL AND [IdNumber] <> '';


-- 5. Tenant passport number must be unique within a company.
CREATE UNIQUE INDEX [IX_Tenants_CompanyId_PassportNumber]
    ON [Tenants] ([CompanyId], [PassportNumber])
    WHERE [PassportNumber] IS NOT NULL AND [PassportNumber] <> '';


-- 6. Tenant TRN must be unique within a company.
CREATE UNIQUE INDEX [IX_Tenants_CompanyId_TrnNumber]
    ON [Tenants] ([CompanyId], [TrnNumber])
    WHERE [TrnNumber] IS NOT NULL AND [TrnNumber] <> '';


-- 7. Vendor trade licence must be unique within a company.
CREATE UNIQUE INDEX [IX_Vendors_CompanyId_TradeLicenseNumber]
    ON [Vendors] ([CompanyId], [TradeLicenseNumber])
    WHERE [TradeLicenseNumber] IS NOT NULL AND [TradeLicenseNumber] <> '';


-- 8. Vendor TRN must be unique within a company.
CREATE UNIQUE INDEX [IX_Vendors_CompanyId_TrnNumber]
    ON [Vendors] ([CompanyId], [TrnNumber])
    WHERE [TrnNumber] IS NOT NULL AND [TrnNumber] <> '';


-- 9. DIAGNOSTIC - run this FIRST if any of statements 4 to 8 failed. It lists
--    every identifier already held by more than one record, so you can merge or
--    correct them before the index is created. An empty result means the data is
--    clean and statements 3 to 8 will succeed.
SELECT 'Tenant IdNumber' AS Identifier, CompanyId, IdNumber AS Value, COUNT(*) AS Records
FROM [Tenants] WHERE IdNumber IS NOT NULL AND IdNumber <> ''
GROUP BY CompanyId, IdNumber HAVING COUNT(*) > 1
UNION ALL
SELECT 'Tenant PassportNumber', CompanyId, PassportNumber, COUNT(*)
FROM [Tenants] WHERE PassportNumber IS NOT NULL AND PassportNumber <> ''
GROUP BY CompanyId, PassportNumber HAVING COUNT(*) > 1
UNION ALL
SELECT 'Tenant TrnNumber', CompanyId, TrnNumber, COUNT(*)
FROM [Tenants] WHERE TrnNumber IS NOT NULL AND TrnNumber <> ''
GROUP BY CompanyId, TrnNumber HAVING COUNT(*) > 1
UNION ALL
SELECT 'Vendor TrnNumber', CompanyId, TrnNumber, COUNT(*)
FROM [Vendors] WHERE TrnNumber IS NOT NULL AND TrnNumber <> ''
GROUP BY CompanyId, TrnNumber HAVING COUNT(*) > 1;


-- 10. VERIFY - confirms the columns and all six indexes are in place. Expect six
--     rows back once statements 1 to 8 have all run.
SELECT i.name AS IndexName, t.name AS TableName, i.is_unique AS IsUnique, i.has_filter AS IsFiltered
FROM sys.indexes i
INNER JOIN sys.tables t ON t.object_id = i.object_id
WHERE i.name IN (
    'IX_Tenants_CompanyId_TradeLicenseNumber',
    'IX_Tenants_CompanyId_IdNumber',
    'IX_Tenants_CompanyId_PassportNumber',
    'IX_Tenants_CompanyId_TrnNumber',
    'IX_Vendors_CompanyId_TradeLicenseNumber',
    'IX_Vendors_CompanyId_TrnNumber'
)
ORDER BY t.name, i.name;
