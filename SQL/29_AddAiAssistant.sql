-- ============================================================================
-- 29_AddAiAssistant.sql
--
-- The AI assistant: its sessions (with the usage reported by OpenAI), the
-- conversation log, and the entries it prepared that wait for the user's
-- confirmation. Company administrators get the Assistant.Use permission; the
-- assistant itself stays hidden until it is switched on in the server settings.
--
-- Adds:
--   AssistantSessions, AssistantMessages, AssistantPendingActions
--   AppUserPermissions Assistant.Use for company administrators
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. Assistant sessions.
IF OBJECT_ID('AssistantSessions', 'U') IS NULL CREATE TABLE [AssistantSessions] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [AppUserId] int NOT NULL,
    [UserName] nvarchar(200) NULL,
    [StartedAt] datetime2 NOT NULL,
    [EndedAt] datetime2 NULL,
    [Model] nvarchar(100) NOT NULL,
    [Mode] nvarchar(10) NOT NULL,
    [ToolCalls] int NOT NULL,
    [InputTokens] int NOT NULL,
    [OutputTokens] int NOT NULL,
    [InputAudioTokens] int NOT NULL,
    [OutputAudioTokens] int NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    CONSTRAINT [PK_AssistantSessions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AssistantSessions_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


-- 2. Sessions are counted per company per day.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AssistantSessions_CompanyId_StartedAt') CREATE INDEX [IX_AssistantSessions_CompanyId_StartedAt] ON [AssistantSessions] ([CompanyId], [StartedAt]);


-- 3. The conversation log.
IF OBJECT_ID('AssistantMessages', 'U') IS NULL CREATE TABLE [AssistantMessages] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [SessionId] int NOT NULL,
    [Role] nvarchar(20) NOT NULL,
    [Text] nvarchar(4000) NULL,
    [ToolName] nvarchar(100) NULL,
    [ToolArgsJson] nvarchar(4000) NULL,
    [ToolResultSummary] nvarchar(1000) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_AssistantMessages] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AssistantMessages_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_AssistantMessages_AssistantSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [AssistantSessions] ([Id]) ON DELETE CASCADE
);


-- 4. Messages are read by session.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AssistantMessages_SessionId') CREATE INDEX [IX_AssistantMessages_SessionId] ON [AssistantMessages] ([SessionId]);


-- 5. Entries the assistant prepared, waiting for the user's click.
IF OBJECT_ID('AssistantPendingActions', 'U') IS NULL CREATE TABLE [AssistantPendingActions] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [AppUserId] int NOT NULL,
    [SessionId] int NOT NULL,
    [ToolName] nvarchar(100) NOT NULL,
    [ArgumentsJson] nvarchar(max) NOT NULL,
    [Summary] nvarchar(2000) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ExpiresAt] datetime2 NOT NULL,
    [ExecutedAt] datetime2 NULL,
    [ResultJson] nvarchar(max) NULL,
    [ErrorMessage] nvarchar(1000) NULL,
    CONSTRAINT [PK_AssistantPendingActions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AssistantPendingActions_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_AssistantPendingActions_AssistantSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [AssistantSessions] ([Id])
);


-- 6. Pending actions are looked up by company and status.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AssistantPendingActions_CompanyId_Status') CREATE INDEX [IX_AssistantPendingActions_CompanyId_Status] ON [AssistantPendingActions] ([CompanyId], [Status]);


-- 7. Company administrators may use the assistant.
INSERT INTO [AppUserPermissions] ([AppUserId], [PermissionKey], [GrantedAt])
SELECT DISTINCT c.[AppUserId], N'Assistant.Use', SYSUTCDATETIME()
FROM [AppUserCompanies] c
WHERE c.[IsCompanyAdmin] = 1
  AND NOT EXISTS (SELECT 1 FROM [AppUserPermissions] p WHERE p.[AppUserId] = c.[AppUserId] AND p.[PermissionKey] = N'Assistant.Use');


-- 8. Check: expect one row showing 0 sessions.
SELECT COUNT(*) AS [AssistantSessions] FROM [AssistantSessions];
