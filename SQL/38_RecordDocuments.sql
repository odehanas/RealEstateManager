-- ============================================================================
-- 38_RecordDocuments.sql
--
-- Documents kept with tenants and contracts (Emirates ID, passport, the contract
-- attested by the municipality), and the expiry dates of the tenant's Emirates ID
-- and passport so the system can warn before they lapse.
--
-- Adds:
--   RecordDocuments            the scanned documents
--   Tenants.IdExpiryDate       Emirates ID expiry
--   Tenants.PassportExpiryDate passport expiry
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. Emirates ID expiry.
IF COL_LENGTH('Tenants', 'IdExpiryDate') IS NULL ALTER TABLE [Tenants] ADD [IdExpiryDate] datetime2 NULL;


-- 2. Passport expiry.
IF COL_LENGTH('Tenants', 'PassportExpiryDate') IS NULL ALTER TABLE [Tenants] ADD [PassportExpiryDate] datetime2 NULL;


-- 3. The documents.
IF OBJECT_ID('RecordDocuments', 'U') IS NULL CREATE TABLE [RecordDocuments] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [EntityType] nvarchar(20) NOT NULL,
    [EntityId] int NOT NULL,
    [DocumentType] nvarchar(60) NOT NULL,
    [DocumentNumber] nvarchar(100) NULL,
    [IssueDate] datetime2 NULL,
    [ExpiryDate] datetime2 NULL,
    [Notes] nvarchar(500) NULL,
    [FileName] nvarchar(255) NOT NULL,
    [ContentType] nvarchar(100) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [Content] varbinary(max) NOT NULL,
    [UploadedAt] datetime2 NOT NULL,
    [UploadedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_RecordDocuments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RecordDocuments_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


-- 4. Documents are listed by their record.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RecordDocuments_CompanyId_EntityType_EntityId' AND object_id = OBJECT_ID('RecordDocuments')) CREATE INDEX [IX_RecordDocuments_CompanyId_EntityType_EntityId] ON [RecordDocuments] ([CompanyId], [EntityType], [EntityId]);
