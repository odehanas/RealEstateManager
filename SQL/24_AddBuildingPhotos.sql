-- ============================================================================
-- 24_AddBuildingPhotos.sql
--
-- Photo mode of the building view: photographs of each building's facade, and
-- each unit's outline marked on them.
--
-- Adds:
--   PropertyPhotos     the photos (the image is stored with the record)
--   UnitPhotoAreas     a unit's outline on a photo, as fractions of its size
--
-- Nothing existing is changed.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there (a database built from
-- the current model).
-- ============================================================================


-- 1. The building photos.
IF OBJECT_ID('PropertyPhotos', 'U') IS NULL CREATE TABLE [PropertyPhotos] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [PropertyId] int NOT NULL,
    [Title] nvarchar(100) NOT NULL,
    [SortOrder] int NOT NULL,
    [FileName] nvarchar(260) NOT NULL,
    [ContentType] nvarchar(100) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [Content] varbinary(max) NOT NULL,
    [UploadedAt] datetime2 NOT NULL,
    [UploadedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_PropertyPhotos] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PropertyPhotos_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_PropertyPhotos_Properties_PropertyId] FOREIGN KEY ([PropertyId]) REFERENCES [Properties] ([Id])
);


-- 2. Finding a building's photos.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PropertyPhotos_CompanyId_PropertyId') CREATE INDEX [IX_PropertyPhotos_CompanyId_PropertyId] ON [PropertyPhotos] ([CompanyId], [PropertyId]);


-- 3. Unit outlines on the photos.
IF OBJECT_ID('UnitPhotoAreas', 'U') IS NULL CREATE TABLE [UnitPhotoAreas] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [PropertyPhotoId] int NOT NULL,
    [UnitId] int NOT NULL,
    [PointsJson] nvarchar(4000) NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_UnitPhotoAreas] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UnitPhotoAreas_PropertyPhotos_PropertyPhotoId] FOREIGN KEY ([PropertyPhotoId]) REFERENCES [PropertyPhotos] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_UnitPhotoAreas_Units_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Units] ([Id])
);


-- 4. One outline per unit per photo.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UnitPhotoAreas_PropertyPhotoId_UnitId') CREATE UNIQUE INDEX [IX_UnitPhotoAreas_PropertyPhotoId_UnitId] ON [UnitPhotoAreas] ([PropertyPhotoId], [UnitId]);


-- 5. Check: expect one row showing 0 photos.
SELECT COUNT(*) AS [PropertyPhotos] FROM [PropertyPhotos];
