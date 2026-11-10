-- ============================================================================
-- 46_WhatsAppIntake.sql
--
--   * Each company's link to its WhatsApp Business number (Meta Cloud API). The
--     credentials are stored encrypted by the application.
--   * Every message received. The unique message id makes a repeated delivery
--     harmless, and messages that cannot go on one tenant's record wait here for
--     a member of staff.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The connections.
IF OBJECT_ID('WhatsAppConnections', 'U') IS NULL CREATE TABLE [WhatsAppConnections] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [PublicKey] nvarchar(64) NOT NULL,
    [PhoneNumberId] nvarchar(50) NOT NULL,
    [DisplayPhoneNumber] nvarchar(30) NULL,
    [ProtectedAppSecret] nvarchar(2000) NOT NULL,
    [ProtectedVerifyToken] nvarchar(2000) NOT NULL,
    [DefaultCallingCode] nvarchar(5) NOT NULL,
    [IsEnabled] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ChangedAt] datetime2 NOT NULL,
    [ChangedByAppUserId] int NULL,
    CONSTRAINT [PK_WhatsAppConnections] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_WhatsAppConnections_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


-- 2. A company has one connection.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WhatsAppConnections_CompanyId' AND object_id = OBJECT_ID('WhatsAppConnections')) CREATE UNIQUE INDEX [IX_WhatsAppConnections_CompanyId] ON [WhatsAppConnections] ([CompanyId]);


-- 3. The webhook address finds its connection by this key.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WhatsAppConnections_PublicKey' AND object_id = OBJECT_ID('WhatsAppConnections')) CREATE UNIQUE INDEX [IX_WhatsAppConnections_PublicKey] ON [WhatsAppConnections] ([PublicKey]);


-- 4. A WhatsApp number belongs to one company.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WhatsAppConnections_PhoneNumberId' AND object_id = OBJECT_ID('WhatsAppConnections')) CREATE UNIQUE INDEX [IX_WhatsAppConnections_PhoneNumberId] ON [WhatsAppConnections] ([PhoneNumberId]);


-- 5. The messages received.
IF OBJECT_ID('WhatsAppInboundMessages', 'U') IS NULL CREATE TABLE [WhatsAppInboundMessages] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [MessageId] nvarchar(200) NOT NULL,
    [Sender] nvarchar(128) NOT NULL,
    [ProfileName] nvarchar(200) NULL,
    [MessageType] nvarchar(30) NOT NULL,
    [Body] nvarchar(max) NULL,
    [SentAt] datetime2 NOT NULL,
    [ReceivedAt] datetime2 NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [ReviewReason] nvarchar(30) NULL,
    [TenantId] int NULL,
    [TenantServiceRequestId] int NULL,
    [HandledByAppUserId] int NULL,
    [HandledAt] datetime2 NULL,
    CONSTRAINT [PK_WhatsAppInboundMessages] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_WhatsAppInboundMessages_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_WhatsAppInboundMessages_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_WhatsAppInboundMessages_TenantServiceRequests_TenantServiceRequestId] FOREIGN KEY ([TenantServiceRequestId]) REFERENCES [TenantServiceRequests] ([Id])
);


-- 6. The same message is only ever taken once.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WhatsAppInboundMessages_CompanyId_MessageId' AND object_id = OBJECT_ID('WhatsAppInboundMessages')) CREATE UNIQUE INDEX [IX_WhatsAppInboundMessages_CompanyId_MessageId] ON [WhatsAppInboundMessages] ([CompanyId], [MessageId]);


-- 7. The review list.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WhatsAppInboundMessages_CompanyId_Status_ReceivedAt' AND object_id = OBJECT_ID('WhatsAppInboundMessages')) CREATE INDEX [IX_WhatsAppInboundMessages_CompanyId_Status_ReceivedAt] ON [WhatsAppInboundMessages] ([CompanyId], [Status], [ReceivedAt]);


-- 8. A further message from the same person finds the request they just opened.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WhatsAppInboundMessages_CompanyId_TenantId_ReceivedAt' AND object_id = OBJECT_ID('WhatsAppInboundMessages')) CREATE INDEX [IX_WhatsAppInboundMessages_CompanyId_TenantId_ReceivedAt] ON [WhatsAppInboundMessages] ([CompanyId], [TenantId], [ReceivedAt]);


-- 9. Messages are found from their request.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WhatsAppInboundMessages_TenantServiceRequestId' AND object_id = OBJECT_ID('WhatsAppInboundMessages')) CREATE INDEX [IX_WhatsAppInboundMessages_TenantServiceRequestId] ON [WhatsAppInboundMessages] ([TenantServiceRequestId]);
