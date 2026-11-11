-- ============================================================================
-- 50_PaidServices.sql
--
-- Services a tenant asks for and pays for: special maintenance (work that is not
-- the landlord's to do free of charge) and special requests the company may
-- subcontract, such as cleaning.
--
--   * ServiceCatalogItems: the company's price list - what it sells each service
--     for, what it expects it to cost, and the income account it is credited to.
--   * A tenant's request can be an order for one of these. The request keeps the
--     service's name and price as they were when it was ordered, and the invoice
--     raised for it.
--   * A charge invoice can name its own income account, so each service is
--     credited where the company wants it.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The company's price list of paid services.
IF OBJECT_ID('ServiceCatalogItems', 'U') IS NULL CREATE TABLE [ServiceCatalogItems] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [Kind] nvarchar(30) NOT NULL,
    [Category] nvarchar(50) NULL,
    [Description] nvarchar(500) NULL,
    [SellingPrice] decimal(18,2) NOT NULL CONSTRAINT [DF_ServiceCatalogItems_SellingPrice] DEFAULT 0,
    [ExpectedCost] decimal(18,2) NOT NULL CONSTRAINT [DF_ServiceCatalogItems_ExpectedCost] DEFAULT 0,
    [IncomeAccountId] int NULL,
    [Subcontracted] bit NOT NULL CONSTRAINT [DF_ServiceCatalogItems_Subcontracted] DEFAULT 0,
    [PayBeforeWork] bit NOT NULL CONSTRAINT [DF_ServiceCatalogItems_PayBeforeWork] DEFAULT 1,
    [IsActive] bit NOT NULL CONSTRAINT [DF_ServiceCatalogItems_IsActive] DEFAULT 1,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_ServiceCatalogItems] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ServiceCatalogItems_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_ServiceCatalogItems_Accounts] FOREIGN KEY ([IncomeAccountId]) REFERENCES [Accounts] ([Id])
);


-- 2. A company does not list the same service twice.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ServiceCatalogItems_CompanyId_Name' AND object_id = OBJECT_ID('ServiceCatalogItems')) CREATE UNIQUE INDEX [IX_ServiceCatalogItems_CompanyId_Name] ON [ServiceCatalogItems] ([CompanyId], [Name]);


-- 3. The service a request is an order for.
IF COL_LENGTH('TenantServiceRequests', 'ServiceCatalogItemId') IS NULL ALTER TABLE [TenantServiceRequests] ADD [ServiceCatalogItemId] int NULL;


-- 4. The service's name as it was when ordered.
IF COL_LENGTH('TenantServiceRequests', 'ServiceName') IS NULL ALTER TABLE [TenantServiceRequests] ADD [ServiceName] nvarchar(100) NULL;


-- 5. Whether it is special maintenance or a special request.
IF COL_LENGTH('TenantServiceRequests', 'ServiceKind') IS NULL ALTER TABLE [TenantServiceRequests] ADD [ServiceKind] nvarchar(30) NULL;


-- 6. The price before VAT, as quoted when ordered and as invoiced.
IF COL_LENGTH('TenantServiceRequests', 'ServicePrice') IS NULL ALTER TABLE [TenantServiceRequests] ADD [ServicePrice] decimal(18,2) NULL;


-- 7. What the service was expected to cost the company.
IF COL_LENGTH('TenantServiceRequests', 'ServiceExpectedCost') IS NULL ALTER TABLE [TenantServiceRequests] ADD [ServiceExpectedCost] decimal(18,2) NULL;


-- 8. Whether the work waits for the payment.
IF COL_LENGTH('TenantServiceRequests', 'ServicePayFirst') IS NULL ALTER TABLE [TenantServiceRequests] ADD [ServicePayFirst] bit NOT NULL CONSTRAINT [DF_TSR_ServicePayFirst] DEFAULT 0;


-- 9. The invoice raised to the tenant for the service.
IF COL_LENGTH('TenantServiceRequests', 'ServiceInvoiceId') IS NULL ALTER TABLE [TenantServiceRequests] ADD [ServiceInvoiceId] int NULL;


-- 10. A charge invoice can name the income account it is credited to.
IF COL_LENGTH('CustomerInvoices', 'IncomeAccountId') IS NULL ALTER TABLE [CustomerInvoices] ADD [IncomeAccountId] int NULL;
