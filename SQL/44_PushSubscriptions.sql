-- ============================================================================
-- 44_PushSubscriptions.sql
--
-- Phone and browser notifications. Each row is one device that agreed to receive
-- notifications for one person: a staff user, a tenant's portal login or an
-- employee's self-service login. The endpoint and keys are what the browser
-- gave; they let the server send to that device and to nothing else.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. One device's notification subscription.
IF OBJECT_ID('PushSubscriptions', 'U') IS NULL CREATE TABLE [PushSubscriptions] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [Audience] nvarchar(10) NOT NULL,
    [AppUserId] int NULL,
    [TenantPortalUserId] int NULL,
    [EmployeePortalUserId] int NULL,
    [CompanyId] int NULL,
    [Endpoint] nvarchar(1000) NOT NULL,
    [EndpointHash] nvarchar(64) NOT NULL,
    [P256dh] nvarchar(200) NOT NULL,
    [Auth] nvarchar(100) NOT NULL,
    [Language] nvarchar(5) NOT NULL,
    [UserAgent] nvarchar(300) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [LastSentAt] datetime2 NULL,
    [FailureCount] int NOT NULL CONSTRAINT [DF_PushSubscriptions_FailureCount] DEFAULT (0),
    CONSTRAINT [PK_PushSubscriptions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PushSubscriptions_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_PushSubscriptions_TenantPortalUsers_TenantPortalUserId] FOREIGN KEY ([TenantPortalUserId]) REFERENCES [TenantPortalUsers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_PushSubscriptions_EmployeePortalUsers_EmployeePortalUserId] FOREIGN KEY ([EmployeePortalUserId]) REFERENCES [EmployeePortalUsers] ([Id]) ON DELETE CASCADE
);


-- 2. A device is registered once; signing in as someone else on it moves it to them.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PushSubscriptions_EndpointHash' AND object_id = OBJECT_ID('PushSubscriptions')) CREATE UNIQUE INDEX [IX_PushSubscriptions_EndpointHash] ON [PushSubscriptions] ([EndpointHash]);


-- 3. Finding a staff user's devices.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PushSubscriptions_AppUserId' AND object_id = OBJECT_ID('PushSubscriptions')) CREATE INDEX [IX_PushSubscriptions_AppUserId] ON [PushSubscriptions] ([AppUserId]);


-- 4. Finding a tenant's devices.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PushSubscriptions_TenantPortalUserId' AND object_id = OBJECT_ID('PushSubscriptions')) CREATE INDEX [IX_PushSubscriptions_TenantPortalUserId] ON [PushSubscriptions] ([TenantPortalUserId]);


-- 5. Finding an employee's devices.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PushSubscriptions_EmployeePortalUserId' AND object_id = OBJECT_ID('PushSubscriptions')) CREATE INDEX [IX_PushSubscriptions_EmployeePortalUserId] ON [PushSubscriptions] ([EmployeePortalUserId]);
