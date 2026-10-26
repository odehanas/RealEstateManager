-- ============================================================================
-- 27_AddPayrollTransactionsAndAttendance.sql
--
-- Payroll transactions - additions (overtime, bonus, commission) and
-- deductions (penalties, late arrival, unauthorised absence) per employee,
-- approved and then picked up by the payroll - and daily attendance, which
-- can generate those transactions.
--
-- Adds:
--   PayrollAdjustments    the transactions
--   AttendanceRecords     one row per employee per day
--   PayrollRunLines       AbsenceDays
--   PayrollSettings       rest-day overtime rate, weekly rest days, start time,
--                         late grace and whether late time is deducted
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. Payroll transactions.
IF OBJECT_ID('PayrollAdjustments', 'U') IS NULL CREATE TABLE [PayrollAdjustments] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [EmployeeId] int NOT NULL,
    [Date] datetime2 NOT NULL,
    [Category] nvarchar(30) NOT NULL,
    [Quantity] decimal(9,2) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Description] nvarchar(300) NULL,
    [Status] nvarchar(20) NOT NULL,
    [Source] nvarchar(20) NOT NULL,
    [CreatedByName] nvarchar(200) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [DecidedByName] nvarchar(200) NULL,
    [DecidedAt] datetime2 NULL,
    [PayrollRunId] int NULL,
    CONSTRAINT [PK_PayrollAdjustments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PayrollAdjustments_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_PayrollAdjustments_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([Id]),
    CONSTRAINT [FK_PayrollAdjustments_PayrollRuns_PayrollRunId] FOREIGN KEY ([PayrollRunId]) REFERENCES [PayrollRuns] ([Id])
);


-- 2. Finding an employee's transactions by date.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PayrollAdjustments_EmployeeId_Date') CREATE INDEX [IX_PayrollAdjustments_EmployeeId_Date] ON [PayrollAdjustments] ([EmployeeId], [Date]);


-- 3. Finding what a payroll paid.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PayrollAdjustments_PayrollRunId') CREATE INDEX [IX_PayrollAdjustments_PayrollRunId] ON [PayrollAdjustments] ([PayrollRunId]);


-- 4. Daily attendance.
IF OBJECT_ID('AttendanceRecords', 'U') IS NULL CREATE TABLE [AttendanceRecords] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [EmployeeId] int NOT NULL,
    [Date] datetime2 NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [CheckIn] time NULL,
    [CheckOut] time NULL,
    [WorkedHours] decimal(9,2) NOT NULL,
    [OvertimeHours] decimal(9,2) NOT NULL,
    [LateMinutes] int NOT NULL,
    [Notes] nvarchar(300) NULL,
    [Source] nvarchar(20) NOT NULL,
    CONSTRAINT [PK_AttendanceRecords] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AttendanceRecords_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_AttendanceRecords_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([Id])
);


-- 5. One record per employee per day.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AttendanceRecords_EmployeeId_Date') CREATE UNIQUE INDEX [IX_AttendanceRecords_EmployeeId_Date] ON [AttendanceRecords] ([EmployeeId], [Date]);


-- 6. Unauthorised absence days on the payroll line.
IF COL_LENGTH('PayrollRunLines', 'AbsenceDays') IS NULL ALTER TABLE [PayrollRunLines] ADD [AbsenceDays] decimal(9,2) NOT NULL CONSTRAINT [DF_PayrollRunLines_AbsenceDays] DEFAULT 0;


-- 7. Overtime rate on rest days and holidays (150%).
IF COL_LENGTH('PayrollSettings', 'RestDayOvertimeMultiplier') IS NULL ALTER TABLE [PayrollSettings] ADD [RestDayOvertimeMultiplier] decimal(9,4) NOT NULL CONSTRAINT [DF_PayrollSettings_RestDayOvertimeMultiplier] DEFAULT 1.5;


-- 8. The weekly rest days.
IF COL_LENGTH('PayrollSettings', 'WeeklyOffDays') IS NULL ALTER TABLE [PayrollSettings] ADD [WeeklyOffDays] nvarchar(100) NOT NULL CONSTRAINT [DF_PayrollSettings_WeeklyOffDays] DEFAULT 'Sunday';


-- 9. Minutes of grace before an arrival counts as late.
IF COL_LENGTH('PayrollSettings', 'LateGraceMinutes') IS NULL ALTER TABLE [PayrollSettings] ADD [LateGraceMinutes] int NOT NULL CONSTRAINT [DF_PayrollSettings_LateGraceMinutes] DEFAULT 10;


-- 10. Whether late time is deducted.
IF COL_LENGTH('PayrollSettings', 'DeductLateTime') IS NULL ALTER TABLE [PayrollSettings] ADD [DeductLateTime] bit NOT NULL CONSTRAINT [DF_PayrollSettings_DeductLateTime] DEFAULT 0;


-- 11. The working day's start time.
IF COL_LENGTH('PayrollSettings', 'WorkStartTime') IS NULL ALTER TABLE [PayrollSettings] ADD [WorkStartTime] time NOT NULL CONSTRAINT [DF_PayrollSettings_WorkStartTime] DEFAULT '08:00';


-- 12. Check: expect one row showing 0 transactions.
SELECT COUNT(*) AS [PayrollAdjustments] FROM [PayrollAdjustments];
