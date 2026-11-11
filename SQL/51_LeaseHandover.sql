-- ============================================================================
-- 51_LeaseHandover.sql
--
-- The handover of a unit when its contract ends: the inspection against the
-- company's checklist, the Maintenance Manager's charges for damage, the
-- accounts check of what the tenant still owes, and the settlement of the
-- security deposit - applied to what is owed, the rest refunded.
--
--   * HandoverChecklistItems: the company's own checklist, copied onto each
--     handover when it is opened.
--   * LeaseHandovers, with their checklist results (LeaseHandoverItems) and the
--     charges for damage (LeaseHandoverCharges).
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The company's handover checklist.
IF OBJECT_ID('HandoverChecklistItems', 'U') IS NULL CREATE TABLE [HandoverChecklistItems] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [Name] nvarchar(150) NOT NULL,
    [SortOrder] int NOT NULL CONSTRAINT [DF_HandoverChecklistItems_SortOrder] DEFAULT 0,
    [IsActive] bit NOT NULL CONSTRAINT [DF_HandoverChecklistItems_IsActive] DEFAULT 1,
    CONSTRAINT [PK_HandoverChecklistItems] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_HandoverChecklistItems_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


-- 2. Found by company.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HandoverChecklistItems_CompanyId' AND object_id = OBJECT_ID('HandoverChecklistItems')) CREATE INDEX [IX_HandoverChecklistItems_CompanyId] ON [HandoverChecklistItems] ([CompanyId], [SortOrder]);


-- 3. A handover: one contract ending.
IF OBJECT_ID('LeaseHandovers', 'U') IS NULL CREATE TABLE [LeaseHandovers] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [LeaseId] int NOT NULL,
    [HandoverNumber] nvarchar(30) NOT NULL,
    [Reason] nvarchar(30) NOT NULL,
    [HandoverDate] datetime2 NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [KeysReturned] int NULL,
    [ElectricityReading] nvarchar(50) NULL,
    [WaterReading] nvarchar(50) NULL,
    [InspectionNotes] nvarchar(2000) NULL,
    [InspectedAt] datetime2 NULL,
    [InspectedByName] nvarchar(200) NULL,
    [MaintenanceNotes] nvarchar(2000) NULL,
    [MaintenanceSignedAt] datetime2 NULL,
    [MaintenanceSignedByName] nvarchar(200) NULL,
    [ChargesInvoiceId] int NULL,
    [AccountsNotes] nvarchar(2000) NULL,
    [DepositHeld] decimal(18,2) NOT NULL CONSTRAINT [DF_LeaseHandovers_DepositHeld] DEFAULT 0,
    [DepositApplied] decimal(18,2) NOT NULL CONSTRAINT [DF_LeaseHandovers_DepositApplied] DEFAULT 0,
    [DepositRefunded] decimal(18,2) NOT NULL CONSTRAINT [DF_LeaseHandovers_DepositRefunded] DEFAULT 0,
    [BalanceStillDue] decimal(18,2) NOT NULL CONSTRAINT [DF_LeaseHandovers_BalanceStillDue] DEFAULT 0,
    [RefundJournalEntryId] int NULL,
    [SettledAt] datetime2 NULL,
    [SettledByName] nvarchar(200) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_LeaseHandovers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LeaseHandovers_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_LeaseHandovers_Leases] FOREIGN KEY ([LeaseId]) REFERENCES [Leases] ([Id])
);


-- 4. A contract's handovers, and a company's by number.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LeaseHandovers_CompanyId_LeaseId' AND object_id = OBJECT_ID('LeaseHandovers')) CREATE INDEX [IX_LeaseHandovers_CompanyId_LeaseId] ON [LeaseHandovers] ([CompanyId], [LeaseId]);


-- 5. The checklist as answered on one handover.
IF OBJECT_ID('LeaseHandoverItems', 'U') IS NULL CREATE TABLE [LeaseHandoverItems] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [LeaseHandoverId] int NOT NULL,
    [SortOrder] int NOT NULL,
    [Name] nvarchar(150) NOT NULL,
    [Result] nvarchar(20) NOT NULL,
    [Notes] nvarchar(500) NULL,
    CONSTRAINT [PK_LeaseHandoverItems] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LeaseHandoverItems_LeaseHandovers] FOREIGN KEY ([LeaseHandoverId]) REFERENCES [LeaseHandovers] ([Id]) ON DELETE CASCADE
);


-- 6. The checklist lines of a handover.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LeaseHandoverItems_LeaseHandoverId' AND object_id = OBJECT_ID('LeaseHandoverItems')) CREATE INDEX [IX_LeaseHandoverItems_LeaseHandoverId] ON [LeaseHandoverItems] ([LeaseHandoverId]);


-- 7. What the tenant is charged for damage found at the handover.
IF OBJECT_ID('LeaseHandoverCharges', 'U') IS NULL CREATE TABLE [LeaseHandoverCharges] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [LeaseHandoverId] int NOT NULL,
    [Description] nvarchar(300) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [CreatedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_LeaseHandoverCharges] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LeaseHandoverCharges_LeaseHandovers] FOREIGN KEY ([LeaseHandoverId]) REFERENCES [LeaseHandovers] ([Id]) ON DELETE CASCADE
);


-- 8. The charges of a handover.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LeaseHandoverCharges_LeaseHandoverId' AND object_id = OBJECT_ID('LeaseHandoverCharges')) CREATE INDEX [IX_LeaseHandoverCharges_LeaseHandoverId] ON [LeaseHandoverCharges] ([LeaseHandoverId]);
