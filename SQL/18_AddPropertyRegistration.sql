-- ============================================================================
-- 18_AddPropertyRegistration.sql
--
-- Property registration details and documents (development phase 1).
--
-- Adds:
--   Properties   location, title deed and land, building and completion,
--                utilities and insurance columns - all optional
--   Units        unit title deed number, parking bays, electricity / water account
--   PropertyDocuments   scanned title deeds, permits, policies, with expiry dates
--
-- Nothing existing is changed. Every new column starts empty.
--
-- HOW TO RUN in site4now SQL Studio: run ONE numbered statement at a time, top
-- to bottom. The console executes only the first batch, so pasting the whole
-- file runs statement 1 and nothing else.
--
-- Run statements 1 to 5 BEFORE deploying the new build, together with
-- 19_AddDevelopmentProjects.sql.
-- ============================================================================


-- 1. Registration details on the property.
ALTER TABLE [Properties] ADD
    [Emirate] nvarchar(30) NULL,
    [AreaName] nvarchar(100) NULL,
    [PlotNumber] nvarchar(50) NULL,
    [MunicipalityNumber] nvarchar(50) NULL,
    [LocationReference] nvarchar(50) NULL,
    [Latitude] decimal(9,6) NULL,
    [Longitude] decimal(9,6) NULL,
    [TitleDeedNumber] nvarchar(50) NULL,
    [TitleDeedDate] datetime2 NULL,
    [TitleDeedAuthority] nvarchar(100) NULL,
    [TenureType] nvarchar(30) NULL,
    [LandUse] nvarchar(30) NULL,
    [PlotAreaSqFt] decimal(18,2) NULL,
    [OwnershipPercentage] decimal(9,4) NULL,
    [IsMortgaged] bit NOT NULL CONSTRAINT [DF_Properties_IsMortgaged] DEFAULT (0),
    [MortgageDetails] nvarchar(300) NULL,
    [BuildingPermitNumber] nvarchar(50) NULL,
    [BuildingPermitDate] datetime2 NULL,
    [CompletionCertificateNumber] nvarchar(50) NULL,
    [CompletionDate] datetime2 NULL,
    [BuiltUpAreaSqFt] decimal(18,2) NULL,
    [NumberOfFloors] int NULL,
    [ParkingBays] int NULL,
    [DeveloperName] nvarchar(150) NULL,
    [MainContractor] nvarchar(150) NULL,
    [ConsultantName] nvarchar(150) NULL,
    [UtilityAccountNumber] nvarchar(100) NULL,
    [Insurer] nvarchar(150) NULL,
    [InsurancePolicyNumber] nvarchar(50) NULL,
    [InsuranceExpiryDate] datetime2 NULL,
    [RegistrationNotes] nvarchar(1000) NULL;


-- 2. Unit-level registration details.
ALTER TABLE [Units] ADD
    [TitleDeedNumber] nvarchar(50) NULL,
    [ParkingBays] int NULL,
    [UtilityAccountNumber] nvarchar(50) NULL;


-- 3. Documents kept against a property or one of its units.
CREATE TABLE [PropertyDocuments] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [PropertyId] int NOT NULL,
    [UnitId] int NULL,
    [DocumentType] nvarchar(50) NOT NULL,
    [DocumentNumber] nvarchar(100) NULL,
    [Description] nvarchar(300) NULL,
    [IssueDate] datetime2 NULL,
    [ExpiryDate] datetime2 NULL,
    [FileName] nvarchar(260) NOT NULL,
    [ContentType] nvarchar(100) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [Content] varbinary(max) NOT NULL,
    [UploadedAt] datetime2 NOT NULL,
    [UploadedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_PropertyDocuments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PropertyDocuments_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_PropertyDocuments_Properties_PropertyId] FOREIGN KEY ([PropertyId]) REFERENCES [Properties] ([Id]),
    CONSTRAINT [FK_PropertyDocuments_Units_UnitId] FOREIGN KEY ([UnitId]) REFERENCES [Units] ([Id])
);


-- 4. Finding a property's documents.
CREATE INDEX [IX_PropertyDocuments_CompanyId_PropertyId] ON [PropertyDocuments] ([CompanyId], [PropertyId]);


-- 5. Check: expect one row - the number of properties, and 0 documents.
SELECT (SELECT COUNT(*) FROM [Properties]) AS [Properties], (SELECT COUNT(*) FROM [PropertyDocuments]) AS [Documents];
