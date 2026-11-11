-- ============================================================================
-- 53_RequestPhotos.sql
--
-- Photos a tenant attaches to a request, so the office and the technician see
-- the problem before the visit. A few per request, kept with the request.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The photos on a request.
IF OBJECT_ID('ServiceRequestPhotos', 'U') IS NULL CREATE TABLE [ServiceRequestPhotos] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [TenantServiceRequestId] int NOT NULL,
    [FileName] nvarchar(260) NOT NULL,
    [ContentType] nvarchar(100) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [Content] varbinary(max) NOT NULL,
    [UploadedAt] datetime2 NOT NULL,
    [UploadedByName] nvarchar(200) NULL,
    [UploadedByTenant] bit NOT NULL CONSTRAINT [DF_ServiceRequestPhotos_UploadedByTenant] DEFAULT 1,
    CONSTRAINT [PK_ServiceRequestPhotos] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ServiceRequestPhotos_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_ServiceRequestPhotos_Requests] FOREIGN KEY ([TenantServiceRequestId]) REFERENCES [TenantServiceRequests] ([Id])
);


-- 2. A request's photos, in the order they were added.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ServiceRequestPhotos_RequestId' AND object_id = OBJECT_ID('ServiceRequestPhotos')) CREATE INDEX [IX_ServiceRequestPhotos_RequestId] ON [ServiceRequestPhotos] ([TenantServiceRequestId], [Id]);
