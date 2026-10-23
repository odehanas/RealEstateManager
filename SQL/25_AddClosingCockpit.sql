-- ============================================================================
-- 25_AddClosingCockpit.sql
--
-- Closing Cockpit: monthly closing against a checklist, reopening a closed
-- period only on an approved request, and the year-end close that clears
-- revenue and expense into retained earnings and records the balance sheet
-- carried into the next year.
--
-- Adds:
--   AccountingPeriods     who closed it, when and by whom it was last reopened
--   FiscalYearClosings    revenue / expense totals, who closed it, status
--   FiscalYearCarryForwards  balance sheet carried into the next year
--   PeriodReopenRequests  requests to reopen a closed period, and decisions
--   AccountingPeriodEvents   history of each period (closed, reopened, ...)
--   AppUserPermissions    Closing.ApproveReopen for company administrators
--
-- Nothing existing is removed or changed.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. Who closed the period.
IF COL_LENGTH('AccountingPeriods', 'ClosedByName') IS NULL ALTER TABLE [AccountingPeriods] ADD [ClosedByName] nvarchar(200) NULL;


-- 2. When the period was last reopened.
IF COL_LENGTH('AccountingPeriods', 'ReopenedAt') IS NULL ALTER TABLE [AccountingPeriods] ADD [ReopenedAt] datetime2 NULL;


-- 3. Who reopened it.
IF COL_LENGTH('AccountingPeriods', 'ReopenedByName') IS NULL ALTER TABLE [AccountingPeriods] ADD [ReopenedByName] nvarchar(200) NULL;


-- 4. How many times it has been reopened.
IF COL_LENGTH('AccountingPeriods', 'ReopenCount') IS NULL ALTER TABLE [AccountingPeriods] ADD [ReopenCount] int NOT NULL CONSTRAINT [DF_AccountingPeriods_ReopenCount] DEFAULT 0;


-- 5. Year-end close: total revenue cleared.
IF COL_LENGTH('FiscalYearClosings', 'TotalRevenue') IS NULL ALTER TABLE [FiscalYearClosings] ADD [TotalRevenue] decimal(18,2) NOT NULL CONSTRAINT [DF_FiscalYearClosings_TotalRevenue] DEFAULT 0;


-- 6. Year-end close: total expenses cleared.
IF COL_LENGTH('FiscalYearClosings', 'TotalExpenses') IS NULL ALTER TABLE [FiscalYearClosings] ADD [TotalExpenses] decimal(18,2) NOT NULL CONSTRAINT [DF_FiscalYearClosings_TotalExpenses] DEFAULT 0;


-- 7. Who closed the year.
IF COL_LENGTH('FiscalYearClosings', 'ClosedByName') IS NULL ALTER TABLE [FiscalYearClosings] ADD [ClosedByName] nvarchar(200) NULL;


-- 8. Closed, or Reopened while a period of the year is open again.
IF COL_LENGTH('FiscalYearClosings', 'Status') IS NULL ALTER TABLE [FiscalYearClosings] ADD [Status] nvarchar(20) NOT NULL CONSTRAINT [DF_FiscalYearClosings_Status] DEFAULT 'Closed';


-- 9. When the year close was undone.
IF COL_LENGTH('FiscalYearClosings', 'ReopenedAt') IS NULL ALTER TABLE [FiscalYearClosings] ADD [ReopenedAt] datetime2 NULL;


-- 10. Who undid it.
IF COL_LENGTH('FiscalYearClosings', 'ReopenedByName') IS NULL ALTER TABLE [FiscalYearClosings] ADD [ReopenedByName] nvarchar(200) NULL;


-- 11. Balance sheet carried into the next year.
IF OBJECT_ID('FiscalYearCarryForwards', 'U') IS NULL CREATE TABLE [FiscalYearCarryForwards] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [FiscalYearClosingId] int NOT NULL,
    [OpeningFiscalYear] int NOT NULL,
    [AccountId] int NOT NULL,
    [AccountCode] nvarchar(20) NOT NULL,
    [AccountName] nvarchar(200) NOT NULL,
    [AccountType] nvarchar(50) NOT NULL,
    [DebitBalance] decimal(18,2) NOT NULL,
    [CreditBalance] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_FiscalYearCarryForwards] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_FiscalYearCarryForwards_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_FiscalYearCarryForwards_FiscalYearClosings_FiscalYearClosingId] FOREIGN KEY ([FiscalYearClosingId]) REFERENCES [FiscalYearClosings] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_FiscalYearCarryForwards_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [Accounts] ([Id])
);


-- 12. One carried-forward balance per account per year close.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FiscalYearCarryForwards_FiscalYearClosingId_AccountId') CREATE UNIQUE INDEX [IX_FiscalYearCarryForwards_FiscalYearClosingId_AccountId] ON [FiscalYearCarryForwards] ([FiscalYearClosingId], [AccountId]);


-- 13. Requests to reopen a closed period.
IF OBJECT_ID('PeriodReopenRequests', 'U') IS NULL CREATE TABLE [PeriodReopenRequests] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [AccountingPeriodId] int NOT NULL,
    [Reason] nvarchar(1000) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [RequestedByAppUserId] int NOT NULL,
    [RequestedByName] nvarchar(200) NOT NULL,
    [RequestedAt] datetime2 NOT NULL,
    [DecidedByAppUserId] int NULL,
    [DecidedByName] nvarchar(200) NULL,
    [DecidedAt] datetime2 NULL,
    [DecisionComment] nvarchar(1000) NULL,
    [ReclosedAt] datetime2 NULL,
    [ReclosedByName] nvarchar(200) NULL,
    CONSTRAINT [PK_PeriodReopenRequests] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PeriodReopenRequests_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_PeriodReopenRequests_AccountingPeriods_AccountingPeriodId] FOREIGN KEY ([AccountingPeriodId]) REFERENCES [AccountingPeriods] ([Id])
);


-- 14. Finding the requests waiting for a decision.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PeriodReopenRequests_CompanyId_Status') CREATE INDEX [IX_PeriodReopenRequests_CompanyId_Status] ON [PeriodReopenRequests] ([CompanyId], [Status]);


-- 15. Finding a period's requests.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PeriodReopenRequests_AccountingPeriodId') CREATE INDEX [IX_PeriodReopenRequests_AccountingPeriodId] ON [PeriodReopenRequests] ([AccountingPeriodId]);


-- 16. History of each period.
IF OBJECT_ID('AccountingPeriodEvents', 'U') IS NULL CREATE TABLE [AccountingPeriodEvents] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [AccountingPeriodId] int NOT NULL,
    [Action] nvarchar(50) NOT NULL,
    [Note] nvarchar(1000) NULL,
    [ByName] nvarchar(200) NOT NULL,
    [At] datetime2 NOT NULL,
    CONSTRAINT [PK_AccountingPeriodEvents] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AccountingPeriodEvents_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_AccountingPeriodEvents_AccountingPeriods_AccountingPeriodId] FOREIGN KEY ([AccountingPeriodId]) REFERENCES [AccountingPeriods] ([Id])
);


-- 17. Finding a period's history.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AccountingPeriodEvents_AccountingPeriodId') CREATE INDEX [IX_AccountingPeriodEvents_AccountingPeriodId] ON [AccountingPeriodEvents] ([AccountingPeriodId]);


-- 18. Company administrators may approve reopening a closed period.
INSERT INTO [AppUserPermissions] ([AppUserId], [PermissionKey], [GrantedAt])
SELECT DISTINCT c.[AppUserId], N'Closing.ApproveReopen', SYSUTCDATETIME()
FROM [AppUserCompanies] c
WHERE c.[IsCompanyAdmin] = 1
  AND NOT EXISTS (SELECT 1 FROM [AppUserPermissions] p
                  WHERE p.[AppUserId] = c.[AppUserId] AND p.[PermissionKey] = N'Closing.ApproveReopen');


-- 19. Check: expect one row showing 0 requests.
SELECT COUNT(*) AS [PeriodReopenRequests] FROM [PeriodReopenRequests];
