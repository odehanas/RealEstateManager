-- ============================================================================
-- 26_AddHrPayroll.sql
--
-- HR & Payroll: employees with their UAE documents, departments, leave,
-- salary advances, monthly payroll runs and their payments (WPS salary file,
-- bank transfer, cheques), and the company's payroll settings.
--
-- Adds:
--   Departments, Employees, LeaveRequests, EmployeeAdvances,
--   PayrollRuns, PayrollPayments, PayrollRunLines, PayrollSettings
--   AppUserPermissions   HR and payroll permissions for company administrators
--
-- Nothing existing is changed. The payroll accounts (11600, 21700, 21800,
-- 64100-64400) are added to every company's chart by the application on start.
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. Departments.
IF OBJECT_ID('Departments', 'U') IS NULL CREATE TABLE [Departments] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Departments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Departments_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


-- 2. One department name per company.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Departments_CompanyId_Name') CREATE UNIQUE INDEX [IX_Departments_CompanyId_Name] ON [Departments] ([CompanyId], [Name]);


-- 3. Employees.
IF OBJECT_ID('Employees', 'U') IS NULL CREATE TABLE [Employees] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [EmployeeCode] nvarchar(20) NOT NULL,
    [FirstName] nvarchar(100) NOT NULL,
    [LastName] nvarchar(100) NOT NULL,
    [NameArabic] nvarchar(200) NULL,
    [Gender] nvarchar(10) NOT NULL,
    [DateOfBirth] datetime2 NULL,
    [Nationality] nvarchar(100) NULL,
    [MaritalStatus] nvarchar(20) NULL,
    [Email] nvarchar(200) NULL,
    [Mobile] nvarchar(50) NULL,
    [Address] nvarchar(300) NULL,
    [EmergencyContactName] nvarchar(200) NULL,
    [EmergencyContactPhone] nvarchar(50) NULL,
    [DepartmentId] int NULL,
    [JobTitle] nvarchar(100) NOT NULL,
    [PropertyId] int NULL,
    [JoiningDate] datetime2 NOT NULL,
    [ProbationEndDate] datetime2 NULL,
    [ContractType] nvarchar(20) NOT NULL,
    [ContractEndDate] datetime2 NULL,
    [Status] nvarchar(20) NOT NULL,
    [LeavingDate] datetime2 NULL,
    [LeavingReason] nvarchar(300) NULL,
    [PassportNumber] nvarchar(30) NULL,
    [PassportExpiry] datetime2 NULL,
    [EmiratesIdNumber] nvarchar(30) NULL,
    [EmiratesIdExpiry] datetime2 NULL,
    [ResidenceVisaNumber] nvarchar(30) NULL,
    [ResidenceVisaExpiry] datetime2 NULL,
    [LabourCardNumber] nvarchar(30) NULL,
    [LabourCardExpiry] datetime2 NULL,
    [MohrePersonCode] nvarchar(20) NULL,
    [HealthInsuranceNumber] nvarchar(50) NULL,
    [HealthInsuranceExpiry] datetime2 NULL,
    [BasicSalary] decimal(18,2) NOT NULL,
    [HousingAllowance] decimal(18,2) NOT NULL,
    [TransportAllowance] decimal(18,2) NOT NULL,
    [OtherAllowance] decimal(18,2) NOT NULL,
    [PaymentMethod] nvarchar(20) NOT NULL,
    [BankName] nvarchar(100) NULL,
    [Iban] nvarchar(34) NULL,
    [BankRoutingCode] nvarchar(9) NULL,
    [AnnualLeaveDaysPerYear] decimal(9,2) NOT NULL,
    [OpeningLeaveBalance] decimal(9,2) NOT NULL,
    [OpeningGratuityProvision] decimal(18,2) NOT NULL,
    [OpeningBalanceDate] datetime2 NULL,
    [Notes] nvarchar(1000) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Employees] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Employees_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_Employees_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id]),
    CONSTRAINT [FK_Employees_Properties_PropertyId] FOREIGN KEY ([PropertyId]) REFERENCES [Properties] ([Id])
);


-- 4. One employee code per company.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Employees_CompanyId_EmployeeCode') CREATE UNIQUE INDEX [IX_Employees_CompanyId_EmployeeCode] ON [Employees] ([CompanyId], [EmployeeCode]);


-- 5. Leave requests.
IF OBJECT_ID('LeaveRequests', 'U') IS NULL CREATE TABLE [LeaveRequests] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [EmployeeId] int NOT NULL,
    [LeaveType] nvarchar(20) NOT NULL,
    [StartDate] datetime2 NOT NULL,
    [EndDate] datetime2 NOT NULL,
    [Days] decimal(9,2) NOT NULL,
    [Reason] nvarchar(500) NULL,
    [Status] nvarchar(20) NOT NULL,
    [RequestedByName] nvarchar(200) NULL,
    [RequestedAt] datetime2 NOT NULL,
    [DecidedByName] nvarchar(200) NULL,
    [DecidedAt] datetime2 NULL,
    [DecisionComment] nvarchar(500) NULL,
    CONSTRAINT [PK_LeaveRequests] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LeaveRequests_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_LeaveRequests_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([Id])
);


-- 6. Finding an employee's leave.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LeaveRequests_EmployeeId_StartDate') CREATE INDEX [IX_LeaveRequests_EmployeeId_StartDate] ON [LeaveRequests] ([EmployeeId], [StartDate]);


-- 7. Salary advances.
IF OBJECT_ID('EmployeeAdvances', 'U') IS NULL CREATE TABLE [EmployeeAdvances] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [EmployeeId] int NOT NULL,
    [AdvanceDate] datetime2 NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [MonthlyInstalment] decimal(18,2) NOT NULL,
    [RecoveredAmount] decimal(18,2) NOT NULL,
    [PaidFromAccountId] int NOT NULL,
    [Reference] nvarchar(100) NULL,
    [Notes] nvarchar(500) NULL,
    [JournalEntryId] int NULL,
    CONSTRAINT [PK_EmployeeAdvances] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_EmployeeAdvances_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_EmployeeAdvances_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([Id]),
    CONSTRAINT [FK_EmployeeAdvances_Accounts_PaidFromAccountId] FOREIGN KEY ([PaidFromAccountId]) REFERENCES [Accounts] ([Id]),
    CONSTRAINT [FK_EmployeeAdvances_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries] ([Id])
);


-- 8. Payroll runs, one per company and month.
IF OBJECT_ID('PayrollRuns', 'U') IS NULL CREATE TABLE [PayrollRuns] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [RunNumber] nvarchar(20) NOT NULL,
    [Year] int NOT NULL,
    [Month] int NOT NULL,
    [PeriodStart] datetime2 NOT NULL,
    [PeriodEnd] datetime2 NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [PreparedByAppUserId] int NULL,
    [PreparedByName] nvarchar(200) NULL,
    [PreparedAt] datetime2 NOT NULL,
    [ApprovedByName] nvarchar(200) NULL,
    [ApprovedAt] datetime2 NULL,
    [JournalEntryId] int NULL,
    [TotalGross] decimal(18,2) NOT NULL,
    [TotalDeductions] decimal(18,2) NOT NULL,
    [TotalNet] decimal(18,2) NOT NULL,
    [TotalGratuityAccrual] decimal(18,2) NOT NULL,
    [Notes] nvarchar(500) NULL,
    CONSTRAINT [PK_PayrollRuns] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PayrollRuns_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_PayrollRuns_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries] ([Id])
);


-- 9. One run per month.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PayrollRuns_CompanyId_Year_Month') CREATE UNIQUE INDEX [IX_PayrollRuns_CompanyId_Year_Month] ON [PayrollRuns] ([CompanyId], [Year], [Month]);


-- 10. Payment batches of a run.
IF OBJECT_ID('PayrollPayments', 'U') IS NULL CREATE TABLE [PayrollPayments] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [PayrollRunId] int NOT NULL,
    [Method] nvarchar(20) NOT NULL,
    [PaymentDate] datetime2 NOT NULL,
    [BankAccountId] int NOT NULL,
    [Reference] nvarchar(100) NULL,
    [TotalAmount] decimal(18,2) NOT NULL,
    [EmployeeCount] int NOT NULL,
    [FileName] nvarchar(100) NULL,
    [FileContent] nvarchar(max) NULL,
    [JournalEntryId] int NULL,
    [CreatedByName] nvarchar(200) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_PayrollPayments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PayrollPayments_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]),
    CONSTRAINT [FK_PayrollPayments_PayrollRuns_PayrollRunId] FOREIGN KEY ([PayrollRunId]) REFERENCES [PayrollRuns] ([Id]),
    CONSTRAINT [FK_PayrollPayments_Accounts_BankAccountId] FOREIGN KEY ([BankAccountId]) REFERENCES [Accounts] ([Id]),
    CONSTRAINT [FK_PayrollPayments_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries] ([Id])
);


-- 11. Each employee's pay in a run.
IF OBJECT_ID('PayrollRunLines', 'U') IS NULL CREATE TABLE [PayrollRunLines] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [PayrollRunId] int NOT NULL,
    [EmployeeId] int NOT NULL,
    [EmployeeCode] nvarchar(20) NOT NULL,
    [EmployeeName] nvarchar(200) NOT NULL,
    [JobTitle] nvarchar(100) NULL,
    [DepartmentName] nvarchar(100) NULL,
    [PropertyId] int NULL,
    [PaymentMethod] nvarchar(20) NOT NULL,
    [BankName] nvarchar(100) NULL,
    [Iban] nvarchar(34) NULL,
    [BankRoutingCode] nvarchar(9) NULL,
    [MohrePersonCode] nvarchar(20) NULL,
    [DaysInMonth] int NOT NULL,
    [DaysEmployed] int NOT NULL,
    [UnpaidLeaveDays] decimal(9,2) NOT NULL,
    [FullBasic] decimal(18,2) NOT NULL,
    [FullAllowances] decimal(18,2) NOT NULL,
    [Basic] decimal(18,2) NOT NULL,
    [Housing] decimal(18,2) NOT NULL,
    [Transport] decimal(18,2) NOT NULL,
    [OtherAllowance] decimal(18,2) NOT NULL,
    [OvertimeHours] decimal(9,2) NOT NULL,
    [OvertimeAmount] decimal(18,2) NOT NULL,
    [Bonus] decimal(18,2) NOT NULL,
    [AdvanceRecovery] decimal(18,2) NOT NULL,
    [OtherDeductions] decimal(18,2) NOT NULL,
    [DeductionNote] nvarchar(200) NULL,
    [GratuityAccrual] decimal(18,2) NOT NULL,
    [PayrollPaymentId] int NULL,
    [ChequeNumber] nvarchar(30) NULL,
    CONSTRAINT [PK_PayrollRunLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PayrollRunLines_PayrollRuns_PayrollRunId] FOREIGN KEY ([PayrollRunId]) REFERENCES [PayrollRuns] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_PayrollRunLines_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([Id]),
    CONSTRAINT [FK_PayrollRunLines_PayrollPayments_PayrollPaymentId] FOREIGN KEY ([PayrollPaymentId]) REFERENCES [PayrollPayments] ([Id])
);


-- 12. One line per employee per run.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PayrollRunLines_PayrollRunId_EmployeeId') CREATE UNIQUE INDEX [IX_PayrollRunLines_PayrollRunId_EmployeeId] ON [PayrollRunLines] ([PayrollRunId], [EmployeeId]);


-- 13. Payroll settings, one row per company.
IF OBJECT_ID('PayrollSettings', 'U') IS NULL CREATE TABLE [PayrollSettings] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [EmployerEstablishmentId] nvarchar(13) NULL,
    [EmployerBankRoutingCode] nvarchar(9) NULL,
    [SalaryBankName] nvarchar(100) NULL,
    [BasicSalaryExpenseAccountId] int NULL,
    [AllowancesExpenseAccountId] int NULL,
    [OvertimeBonusExpenseAccountId] int NULL,
    [GratuityExpenseAccountId] int NULL,
    [SalariesPayableAccountId] int NULL,
    [GratuityProvisionAccountId] int NULL,
    [EmployeeAdvancesAccountId] int NULL,
    [DefaultBankAccountId] int NULL,
    [OvertimeMultiplier] decimal(9,4) NOT NULL,
    [WorkingHoursPerDay] decimal(9,2) NOT NULL,
    [AccrueGratuity] bit NOT NULL,
    [ChequeWidthMm] decimal(9,2) NOT NULL,
    [ChequeHeightMm] decimal(9,2) NOT NULL,
    [DateXMm] decimal(9,2) NOT NULL,
    [DateYMm] decimal(9,2) NOT NULL,
    [PayeeXMm] decimal(9,2) NOT NULL,
    [PayeeYMm] decimal(9,2) NOT NULL,
    [AmountWordsXMm] decimal(9,2) NOT NULL,
    [AmountWordsYMm] decimal(9,2) NOT NULL,
    [AmountWordsWidthMm] decimal(9,2) NOT NULL,
    [AmountXMm] decimal(9,2) NOT NULL,
    [AmountYMm] decimal(9,2) NOT NULL,
    [ChequeFontSizePt] decimal(9,2) NOT NULL,
    [OffsetXMm] decimal(9,2) NOT NULL,
    [OffsetYMm] decimal(9,2) NOT NULL,
    [PrintAccountPayeeOnly] bit NOT NULL,
    [DateFormat] nvarchar(20) NOT NULL,
    [DotMatrixCpi] int NOT NULL,
    [DotMatrixLpi] int NOT NULL,
    CONSTRAINT [PK_PayrollSettings] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PayrollSettings_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


-- 14. One settings row per company.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PayrollSettings_CompanyId') CREATE UNIQUE INDEX [IX_PayrollSettings_CompanyId] ON [PayrollSettings] ([CompanyId]);


-- 15. Company administrators manage HR.
INSERT INTO [AppUserPermissions] ([AppUserId], [PermissionKey], [GrantedAt])
SELECT DISTINCT c.[AppUserId], N'HR.Manage', SYSUTCDATETIME()
FROM [AppUserCompanies] c
WHERE c.[IsCompanyAdmin] = 1
  AND NOT EXISTS (SELECT 1 FROM [AppUserPermissions] p WHERE p.[AppUserId] = c.[AppUserId] AND p.[PermissionKey] = N'HR.Manage');


-- 16. Company administrators prepare payroll.
INSERT INTO [AppUserPermissions] ([AppUserId], [PermissionKey], [GrantedAt])
SELECT DISTINCT c.[AppUserId], N'Payroll.Manage', SYSUTCDATETIME()
FROM [AppUserCompanies] c
WHERE c.[IsCompanyAdmin] = 1
  AND NOT EXISTS (SELECT 1 FROM [AppUserPermissions] p WHERE p.[AppUserId] = c.[AppUserId] AND p.[PermissionKey] = N'Payroll.Manage');


-- 17. Company administrators approve payroll.
INSERT INTO [AppUserPermissions] ([AppUserId], [PermissionKey], [GrantedAt])
SELECT DISTINCT c.[AppUserId], N'Payroll.Approve', SYSUTCDATETIME()
FROM [AppUserCompanies] c
WHERE c.[IsCompanyAdmin] = 1
  AND NOT EXISTS (SELECT 1 FROM [AppUserPermissions] p WHERE p.[AppUserId] = c.[AppUserId] AND p.[PermissionKey] = N'Payroll.Approve');


-- 18. Check: expect one row showing 0 employees.
SELECT COUNT(*) AS [Employees] FROM [Employees];
