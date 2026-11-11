-- ============================================================================
-- 54_Parking.sql
--
-- Car parking: the numbered bays of a property, and the assignment of a bay to
-- a contract (and so to its unit and tenant) for a charge billed on the terms
-- the company chooses - in one payment or in instalments, as rent is.
--
--   * ParkingBays, ParkingAssignments.
--   * A parking invoice is a rent invoice of the contract that says what it is
--     for (CustomerInvoices.LineDescription) and which assignment raised it; its
--     income is spread over the months like rent (RevenueRecognitionSchedules).
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The numbered bays of a property.
IF OBJECT_ID('ParkingBays', 'U') IS NULL CREATE TABLE [ParkingBays] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [PropertyId] int NOT NULL,
    [BayNumber] nvarchar(30) NOT NULL,
    [BayType] nvarchar(30) NULL,
    [Notes] nvarchar(300) NULL,
    [IsActive] bit NOT NULL CONSTRAINT [DF_ParkingBays_IsActive] DEFAULT 1,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_ParkingBays] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ParkingBays_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_ParkingBays_Properties] FOREIGN KEY ([PropertyId]) REFERENCES [Properties] ([Id])
);


-- 2. A property does not have the same bay number twice.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingBays_PropertyId_BayNumber' AND object_id = OBJECT_ID('ParkingBays')) CREATE UNIQUE INDEX [IX_ParkingBays_PropertyId_BayNumber] ON [ParkingBays] ([PropertyId], [BayNumber]);


-- 3. A bay given to a contract.
IF OBJECT_ID('ParkingAssignments', 'U') IS NULL CREATE TABLE [ParkingAssignments] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [ParkingBayId] int NOT NULL,
    [LeaseId] int NOT NULL,
    [StartDate] datetime2 NOT NULL,
    [EndDate] datetime2 NOT NULL,
    [AnnualCharge] decimal(18,2) NOT NULL CONSTRAINT [DF_ParkingAssignments_AnnualCharge] DEFAULT 0,
    [BillingFrequencyMonths] int NOT NULL CONSTRAINT [DF_ParkingAssignments_Billing] DEFAULT 0,
    [VatCategory] nvarchar(20) NOT NULL,
    [VehiclePlate] nvarchar(30) NULL,
    [Notes] nvarchar(300) NULL,
    [Status] nvarchar(20) NOT NULL,
    [ReleasedOn] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_ParkingAssignments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ParkingAssignments_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_ParkingAssignments_ParkingBays] FOREIGN KEY ([ParkingBayId]) REFERENCES [ParkingBays] ([Id]),
    CONSTRAINT [FK_ParkingAssignments_Leases] FOREIGN KEY ([LeaseId]) REFERENCES [Leases] ([Id])
);


-- 4. A bay's assignments, and a contract's.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingAssignments_ParkingBayId_Status' AND object_id = OBJECT_ID('ParkingAssignments')) CREATE INDEX [IX_ParkingAssignments_ParkingBayId_Status] ON [ParkingAssignments] ([ParkingBayId], [Status]);


-- 5. Found by contract.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingAssignments_LeaseId' AND object_id = OBJECT_ID('ParkingAssignments')) CREATE INDEX [IX_ParkingAssignments_LeaseId] ON [ParkingAssignments] ([LeaseId]);


-- 6. What an invoice is for, when it is not the rent itself (e.g. "Car parking, bay B1-014").
IF COL_LENGTH('CustomerInvoices', 'LineDescription') IS NULL ALTER TABLE [CustomerInvoices] ADD [LineDescription] nvarchar(200) NULL;


-- 7. The parking assignment an invoice was raised for.
IF COL_LENGTH('CustomerInvoices', 'ParkingAssignmentId') IS NULL ALTER TABLE [CustomerInvoices] ADD [ParkingAssignmentId] int NULL;


-- 8. The parking assignment a month's income belongs to.
IF COL_LENGTH('RevenueRecognitionSchedules', 'ParkingAssignmentId') IS NULL ALTER TABLE [RevenueRecognitionSchedules] ADD [ParkingAssignmentId] int NULL;
