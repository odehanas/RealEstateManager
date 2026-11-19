-- ============================================================================
-- 59_VacancyWebsite.sql
--
-- The company's public vacancy website: what a unit is advertised with (rooms,
-- asking rent, description, photos), the company's own public page, and the
-- enquiries visitors leave on it.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. Bedrooms of the unit, as advertised.
IF COL_LENGTH('Units', 'Bedrooms') IS NULL ALTER TABLE [Units] ADD [Bedrooms] int NULL;

-- 2. Bathrooms of the unit.
IF COL_LENGTH('Units', 'Bathrooms') IS NULL ALTER TABLE [Units] ADD [Bathrooms] int NULL;

-- 3. Whether the unit is let furnished.
IF COL_LENGTH('Units', 'IsFurnished') IS NULL ALTER TABLE [Units] ADD [IsFurnished] bit NOT NULL CONSTRAINT [DF_Units_IsFurnished] DEFAULT 0;

-- 4. The rent asked on the website, when it differs from the standard rent.
IF COL_LENGTH('Units', 'AskingRent') IS NULL ALTER TABLE [Units] ADD [AskingRent] decimal(18,2) NULL;

-- 5. What the unit offers, one item per line.
IF COL_LENGTH('Units', 'Amenities') IS NULL ALTER TABLE [Units] ADD [Amenities] nvarchar(600) NULL;

-- 6. The advertisement text, in English.
IF COL_LENGTH('Units', 'ListingDescription') IS NULL ALTER TABLE [Units] ADD [ListingDescription] nvarchar(2000) NULL;

-- 7. The advertisement text, in Arabic.
IF COL_LENGTH('Units', 'ListingDescriptionAr') IS NULL ALTER TABLE [Units] ADD [ListingDescriptionAr] nvarchar(2000) NULL;

-- 8. The switch: only a unit that is ticked here is ever shown to the public.
IF COL_LENGTH('Units', 'IsListed') IS NULL ALTER TABLE [Units] ADD [IsListed] bit NOT NULL CONSTRAINT [DF_Units_IsListed] DEFAULT 0;

-- 9. When the unit was last put on the website.
IF COL_LENGTH('Units', 'ListedAt') IS NULL ALTER TABLE [Units] ADD [ListedAt] datetime2 NULL;


-- 10. The photos of a unit, for its advertisement.
IF OBJECT_ID('UnitPhotos', 'U') IS NULL CREATE TABLE [UnitPhotos] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [UnitId] int NOT NULL,
    [SortOrder] int NOT NULL CONSTRAINT [DF_UnitPhotos_SortOrder] DEFAULT 0,
    [FileName] nvarchar(260) NOT NULL,
    [ContentType] nvarchar(100) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [Content] varbinary(max) NOT NULL,
    [UploadedAt] datetime2 NOT NULL,
    [UploadedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_UnitPhotos] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UnitPhotos_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_UnitPhotos_Units] FOREIGN KEY ([UnitId]) REFERENCES [Units] ([Id]) ON DELETE CASCADE
);

-- 11. A unit's photos, in the order they are shown.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UnitPhotos_UnitId' AND object_id = OBJECT_ID('UnitPhotos')) CREATE INDEX [IX_UnitPhotos_UnitId] ON [UnitPhotos] ([UnitId], [SortOrder], [Id]);


-- 12. The company's public page: its address, whether it is open, and how to reach the office.
IF OBJECT_ID('ListingSites', 'U') IS NULL CREATE TABLE [ListingSites] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [Slug] nvarchar(60) NOT NULL,
    [IsEnabled] bit NOT NULL CONSTRAINT [DF_ListingSites_IsEnabled] DEFAULT 0,
    [DisplayName] nvarchar(200) NULL,
    [DisplayNameAr] nvarchar(200) NULL,
    [Intro] nvarchar(600) NULL,
    [IntroAr] nvarchar(600) NULL,
    [ContactPhone] nvarchar(30) NULL,
    [WhatsAppNumber] nvarchar(30) NULL,
    [ContactEmail] nvarchar(200) NULL,
    [ShowRent] bit NOT NULL CONSTRAINT [DF_ListingSites_ShowRent] DEFAULT 1,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_ListingSites] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ListingSites_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);

-- 13. One public page per company.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ListingSites_CompanyId' AND object_id = OBJECT_ID('ListingSites')) CREATE UNIQUE INDEX [UX_ListingSites_CompanyId] ON [ListingSites] ([CompanyId]);

-- 14. The address of a page is its own: no two companies share one.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ListingSites_Slug' AND object_id = OBJECT_ID('ListingSites')) CREATE UNIQUE INDEX [UX_ListingSites_Slug] ON [ListingSites] ([Slug]);


-- 15. What a visitor of the website asks: about one unit, or the office in general.
IF OBJECT_ID('UnitEnquiries', 'U') IS NULL CREATE TABLE [UnitEnquiries] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [UnitId] int NULL,
    [Name] nvarchar(150) NOT NULL,
    [Phone] nvarchar(30) NOT NULL,
    [Email] nvarchar(200) NULL,
    [Message] nvarchar(1000) NULL,
    [Language] nvarchar(5) NOT NULL CONSTRAINT [DF_UnitEnquiries_Language] DEFAULT 'en',
    [Status] nvarchar(20) NOT NULL CONSTRAINT [DF_UnitEnquiries_Status] DEFAULT 'New',
    [Notes] nvarchar(1000) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [HandledAt] datetime2 NULL,
    [HandledByName] nvarchar(200) NULL,
    CONSTRAINT [PK_UnitEnquiries] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UnitEnquiries_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_UnitEnquiries_Units] FOREIGN KEY ([UnitId]) REFERENCES [Units] ([Id]) ON DELETE SET NULL
);

-- 16. A company's enquiries, newest first, and the new ones counted quickly.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UnitEnquiries_Company_Status' AND object_id = OBJECT_ID('UnitEnquiries')) CREATE INDEX [IX_UnitEnquiries_Company_Status] ON [UnitEnquiries] ([CompanyId], [Status], [CreatedAt]);
