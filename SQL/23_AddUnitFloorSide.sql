-- ============================================================================
-- 23_AddUnitFloorSide.sql
--
-- The side of the building each unit faces, for the 3D building view.
--
-- Adds:
--   Units.FloorSide   Front, Back, Left or Right
--
-- Starts empty: until it is filled in, the 3D view spreads each floor's units
-- between the front and the back.
--
-- Applied automatically when the application starts (see SQL/README.md).
-- ============================================================================


-- 1. The side of the building. Empty for every existing unit.
--    Skipped when the column is already there (a database built from the current model).
IF COL_LENGTH('Units', 'FloorSide') IS NULL ALTER TABLE [Units] ADD [FloorSide] nvarchar(10) NULL;


-- 2. Check: expect one row showing the number of units.
SELECT COUNT(*) AS [Units] FROM [Units];
