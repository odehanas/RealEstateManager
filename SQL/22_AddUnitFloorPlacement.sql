-- ============================================================================
-- 22_AddUnitFloorPlacement.sql
--
-- Where each unit sits in the building view (Reporting > Building View).
--
-- Adds:
--   Units.FloorNumber     the floor as a number (0 = ground, negative = basement)
--   Units.FloorPosition   left-to-right position along the floor
--
-- Both start empty: until they are filled in, the building view reads the floor
-- from the existing Floor text (G = 0, 3 = 3, B1 = -1) and orders units by number.
--
-- Applied automatically when the application starts (see SQL/README.md).
-- ============================================================================


-- 1. Floor number and position on the floor. Empty for every existing unit.
--    Skipped when the columns are already there (a database built from the current model).
IF COL_LENGTH('Units', 'FloorNumber') IS NULL ALTER TABLE [Units] ADD [FloorNumber] int NULL, [FloorPosition] int NULL;


-- 2. Check: expect one row showing the number of units.
SELECT COUNT(*) AS [Units] FROM [Units];
