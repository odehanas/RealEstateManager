namespace RealEstateManager.Services
{
    public static class AppPermissions
    {
        /// <summary>
        /// Read master data: open the lists and look records up. This is what most
        /// users need - an accountant has to find a vendor to enter its bill, but has
        /// no business creating one.
        /// </summary>
        public const string MasterDataView = "MasterData.View";

        /// <summary>
        /// Create, amend and deactivate master data. Deliberately narrow: master data
        /// is centralised so that properties, units, tenants and vendors are set up once
        /// by the people responsible for them, and not opened twice under two codes by
        /// whoever happened to need them first.
        /// </summary>
        public const string MasterDataManage = "MasterData.Manage";

        public const string LeasesManage = "Leases.Manage";

        /// <summary>
        /// Approve or reject contracts submitted by others, when the company requires
        /// contract approval. Kept apart from Leases.Manage so the person who writes a
        /// contract and the person who signs it off can be different people.
        /// </summary>
        public const string LeasesApprove = "Leases.Approve";
        public const string AccountingManage = "Accounting.Manage";

        /// <summary>
        /// Open a closed accounting period again, on a request that says why. Kept apart
        /// from Accounting.Manage so the people who post and close are not the ones who
        /// unlock a month that has already been reported. Granted to company
        /// administrators.
        /// </summary>
        public const string PeriodReopenApprove = "Closing.ApproveReopen";

        /// <summary>
        /// The owners' seat: the Owner Monitor (everything posted, released and changed),
        /// read access to every report and master data, and control of the approval limits
        /// and delegations. Grants no right to enter or change transactions.
        /// </summary>
        public const string OwnerOversight = "Owner.Oversight";

        /// <summary>Release payments held for approval, up to the person's approval level.</summary>
        public const string PaymentsApprove = "Payments.Approve";

        /// <summary>Employees, their documents, departments, leave and salary advances.</summary>
        public const string HrManage = "HR.Manage";

        /// <summary>Prepare the monthly payroll and pay it: WPS file, bank transfer, cheques.</summary>
        public const string PayrollManage = "Payroll.Manage";

        /// <summary>
        /// Approve a prepared payroll into the accounts. Kept apart from preparing it, so
        /// the person who enters the salaries is not the one who releases them.
        /// </summary>
        public const string PayrollApprove = "Payroll.Approve";
        public const string ReportsView = "Reports.View";

        /// <summary>
        /// Talk or type to the AI assistant. It only ever reads and does what the user's
        /// other permissions allow; this decides whether they see it at all.
        /// </summary>
        public const string AssistantUse = "Assistant.Use";

        /// <summary>The Business Performance page: results, collections, occupancy and what waits for a decision.</summary>
        public const string PerformanceView = "Performance.View";

        /// <summary>
        /// Handle what tenants submit through the self-service portal: maintenance
        /// calls, requests for more time to pay, complaints. Kept separate from the
        /// accounting permissions because the people who answer tenants are usually not
        /// the people who keep the books.
        /// </summary>
        public const string ServiceRequestsManage = "ServiceRequests.Manage";
    }

    public sealed class PermissionDefinition
    {
        public string Key { get; init; } = string.Empty;
        public string Group { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }

    public static class PermissionCatalog
    {
        public static readonly IReadOnlyList<PermissionDefinition> All = new List<PermissionDefinition>
        {
            new()
            {
                Key = AppPermissions.MasterDataView,
                Group = "Master Data",
                Name = "View Master Data",
                Description = "Open and look up properties, units, tenants and vendors. Read only."
            },
            new()
            {
                Key = AppPermissions.MasterDataManage,
                Group = "Master Data",
                Name = "Create & Edit Master Data",
                Description =
                    "Set up and amend properties, units, tenants and vendors. Grant to the small group " +
                    "responsible for master data - it implies View."
            },
            new()
            {
                Key = AppPermissions.LeasesManage,
                Group = "Operations",
                Name = "Manage Leases",
                Description = "Lease contracts and related operations."
            },
            new()
            {
                Key = AppPermissions.LeasesApprove,
                Group = "Operations",
                Name = "Approve Contracts",
                Description =
                    "Approve or return contracts that others have submitted, and switch contract approval " +
                    "on or off for the company. Nobody can approve a contract they created or submitted."
            },
            new()
            {
                Key = AppPermissions.ServiceRequestsManage,
                Group = "Operations",
                Name = "Handle Tenant Requests",
                Description =
                    "Answer maintenance calls, payment postponement requests and complaints raised " +
                    "by tenants through the self-service portal."
            },
            new()
            {
                Key = AppPermissions.AccountingManage,
                Group = "Accounting",
                Name = "Manage Accounting",
                Description = "Invoices, receipts, payables, journals, closing, fixed assets, and prepayments."
            },
            new()
            {
                Key = AppPermissions.PeriodReopenApprove,
                Group = "Accounting",
                Name = "Approve Period Reopening",
                Description =
                    "Open a closed accounting period again when someone has asked for it with a reason, or turn " +
                    "the request down. Nobody can approve their own request unless they are the company's only approver."
            },
            new()
            {
                Key = AppPermissions.OwnerOversight,
                Group = "Owners & Controls",
                Name = "Owner Oversight",
                Description =
                    "The Owner Monitor (every posting, payment release and change, day by day), read access to all reports " +
                    "and master data, and control of approval limits and delegations. Cannot enter or change transactions."
            },
            new()
            {
                Key = AppPermissions.PaymentsApprove,
                Group = "Owners & Controls",
                Name = "Release Payments",
                Description =
                    "Release payments waiting for approval, up to the limit of the person's approval level. " +
                    "Nobody releases a payment they entered."
            },
            new()
            {
                Key = AppPermissions.PerformanceView,
                Group = "Owners & Controls",
                Name = "View Business Performance",
                Description =
                    "The Business Performance page: profit, collections, receivables, occupancy, cheques and contracts " +
                    "coming due, and everything waiting for this person's approval. Read only."
            },
            new()
            {
                Key = AppPermissions.AssistantUse,
                Group = "General",
                Name = "Use the AI Assistant",
                Description =
                    "Ask the assistant by voice or text about the system and the company's figures, and have it prepare " +
                    "entries for the user to confirm. It can only see and do what the user's other permissions allow."
            },
            new()
            {
                Key = AppPermissions.HrManage,
                Group = "HR & Payroll",
                Name = "Manage Employees",
                Description = "Employees, their contracts and UAE documents, departments, leave and salary advances."
            },
            new()
            {
                Key = AppPermissions.PayrollManage,
                Group = "HR & Payroll",
                Name = "Prepare & Pay Payroll",
                Description = "Prepare the monthly payroll, print payslips and pay salaries by WPS file, bank transfer or cheque."
            },
            new()
            {
                Key = AppPermissions.PayrollApprove,
                Group = "HR & Payroll",
                Name = "Approve Payroll",
                Description =
                    "Approve a prepared payroll into the accounts. Nobody can approve a payroll they prepared " +
                    "unless they are the company's only payroll approver."
            },
            new()
            {
                Key = AppPermissions.ReportsView,
                Group = "Reporting",
                Name = "View Reports",
                Description = "Trial balance, P&L, balance sheet, statements, and ledgers."
            }
        };

        /// <summary>
        /// Master data is centralised: everyone who needs to work with a property,
        /// unit, tenant or vendor may look it up, but only the master data owners may
        /// create or change one. The split is by action rather than by controller,
        /// which is why these live outside <see cref="ControllerPermissionMap"/>.
        /// </summary>
        private static readonly Dictionary<string, string[]> MasterDataReadPermissions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Properties"] = new[] { AppPermissions.MasterDataView, AppPermissions.MasterDataManage, AppPermissions.OwnerOversight },
                ["Units"] = new[] { AppPermissions.MasterDataView, AppPermissions.MasterDataManage, AppPermissions.OwnerOversight },
                ["Tenants"] = new[] { AppPermissions.MasterDataView, AppPermissions.MasterDataManage, AppPermissions.OwnerOversight },

                // An accountant entering a bill must be able to find the supplier, so
                // Accounting.Manage carries read access - but not the right to create
                // one, which is the whole point of centralising.
                ["Vendors"] = new[]
                {
                    AppPermissions.MasterDataView,
                    AppPermissions.MasterDataManage,
                    AppPermissions.AccountingManage,
                    AppPermissions.OwnerOversight
                }
            };

        /// <summary>
        /// Actions that only read. Anything not listed is treated as a write, so a new
        /// action added later fails closed rather than quietly becoming available to
        /// view-only users.
        /// </summary>
        private static readonly HashSet<string> MasterDataReadActions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Index",
                "Details",
                "Print",
                "Export",
                "Search",
                "PreviewCode",
                "Document"
            };

        private static readonly Dictionary<string, string[]> ControllerPermissionMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Leases"] = new[] { AppPermissions.LeasesManage },
                ["Approvals"] = new[] { AppPermissions.LeasesApprove },
                ["Oversight"] = new[] { AppPermissions.OwnerOversight },
                ["Assistant"] = new[] { AppPermissions.AssistantUse },
                ["Performance"] = new[] { AppPermissions.PerformanceView, AppPermissions.OwnerOversight },
                ["ReportBuilder"] = new[] { AppPermissions.ReportsView, AppPermissions.OwnerOversight },
                ["ProjectCosts"] = new[] { AppPermissions.AccountingManage, AppPermissions.ReportsView, AppPermissions.OwnerOversight },
                ["AuthorizationMatrix"] = new[] { AppPermissions.OwnerOversight },
                ["ApprovalLimits"] = new[] { AppPermissions.OwnerOversight },
                ["PaymentApprovals"] = new[] { AppPermissions.PaymentsApprove, AppPermissions.OwnerOversight },
                ["EmployeeRequests"] = new[] { AppPermissions.HrManage },
                ["ServiceRequests"] = new[] { AppPermissions.ServiceRequestsManage },
                ["ChartOfAccounts"] = new[] { AppPermissions.AccountingManage },
                ["AccountMappings"] = new[] { AppPermissions.AccountingManage },
                ["OwnerWithdrawalCategories"] = new[] { AppPermissions.AccountingManage },
                ["PettyCash"] = new[] { AppPermissions.AccountingManage },
                ["VatSettings"] = new[] { AppPermissions.AccountingManage },
                ["CustomerInvoices"] = new[] { AppPermissions.AccountingManage },
                ["CustomerCreditNotes"] = new[] { AppPermissions.AccountingManage },
                ["EInvoicing"] = new[] { AppPermissions.AccountingManage },
                ["CustomerReceipts"] = new[] { AppPermissions.AccountingManage },
                ["PdcReceipts"] = new[] { AppPermissions.AccountingManage },
                ["VendorBills"] = new[] { AppPermissions.AccountingManage },
                ["VendorPayments"] = new[] { AppPermissions.AccountingManage },
                ["SecurityDeposits"] = new[] { AppPermissions.AccountingManage },
                ["FixedAssets"] = new[] { AppPermissions.AccountingManage },
                ["Prepayments"] = new[] { AppPermissions.AccountingManage },
                ["RevenueRecognition"] = new[] { AppPermissions.AccountingManage },
                ["ManualJournals"] = new[] { AppPermissions.AccountingManage },
                ["Journal"] = new[] { AppPermissions.AccountingManage },
                ["Closing"] = new[] { AppPermissions.AccountingManage },
                ["Reports"] = new[] { AppPermissions.ReportsView, AppPermissions.OwnerOversight },
                ["TenantStatements"] = new[] { AppPermissions.ReportsView, AppPermissions.OwnerOversight },
                ["VendorStatements"] = new[] { AppPermissions.ReportsView, AppPermissions.AccountingManage, AppPermissions.OwnerOversight },
                ["OwnerSettlements"] = new[] { AppPermissions.AccountingManage },
                ["DevelopmentProjects"] = new[] { AppPermissions.AccountingManage },
                ["InvestmentProperty"] = new[] { AppPermissions.ReportsView, AppPermissions.OwnerOversight },
                ["UnitSales"] = new[] { AppPermissions.AccountingManage },
                ["CapitalAssetVat"] = new[] { AppPermissions.AccountingManage },
                ["BuildingView"] = new[] { AppPermissions.ReportsView, AppPermissions.OwnerOversight },
                ["OwnerWithdrawals"] = new[] { AppPermissions.AccountingManage },

                ["Employees"] = new[] { AppPermissions.HrManage, AppPermissions.PayrollManage },
                ["Departments"] = new[] { AppPermissions.HrManage },
                ["Leave"] = new[] { AppPermissions.HrManage },
                ["EmployeeAdvances"] = new[] { AppPermissions.HrManage, AppPermissions.PayrollManage },
                ["PayrollAdjustments"] = new[] { AppPermissions.HrManage, AppPermissions.PayrollManage },
                ["Attendance"] = new[] { AppPermissions.HrManage },
                ["Payroll"] = new[] { AppPermissions.PayrollManage, AppPermissions.PayrollApprove },

                // Uploading photos and marking units on them changes the property record.
                ["BuildingPhotos"] = new[] { AppPermissions.MasterDataManage }
            };

        private static readonly Dictionary<string, string[]> ActionPermissionMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Journal.GeneralLedger"] = new[] { AppPermissions.ReportsView, AppPermissions.OwnerOversight },

                // The approver's desk for reopening closed periods. An approver need not
                // be an accountant, so these do not require Accounting.Manage.
                ["Closing.ReopenRequests"] = new[] { AppPermissions.PeriodReopenApprove, AppPermissions.AccountingManage },
                ["Closing.ApproveReopen"] = new[] { AppPermissions.PeriodReopenApprove },
                ["Closing.RejectReopen"] = new[] { AppPermissions.PeriodReopenApprove },

                // Approving puts the payroll into the ledger; preparing it does not.
                ["Payroll.Approve"] = new[] { AppPermissions.PayrollApprove },
                ["Payroll.ReturnToDraft"] = new[] { AppPermissions.PayrollApprove },
                ["Payroll.Settings"] = new[] { AppPermissions.PayrollManage },

                // An approver reads payroll runs but does not prepare or pay them.
                ["Payroll.Create"] = new[] { AppPermissions.PayrollManage },
                ["Payroll.UpdateLine"] = new[] { AppPermissions.PayrollManage },
                ["Payroll.Recalculate"] = new[] { AppPermissions.PayrollManage },
                ["Payroll.Delete"] = new[] { AppPermissions.PayrollManage },
                ["Payroll.Pay"] = new[] { AppPermissions.PayrollManage },
                ["Payroll.CancelPayment"] = new[] { AppPermissions.PayrollManage },

                ["Help.Controls"] = new[] { AppPermissions.OwnerOversight },

                // Handing an employee a portal login is HR's call, not payroll's.
                ["Employees.IssuePortalAccess"] = new[] { AppPermissions.HrManage },
                ["Employees.SetPortalAccess"] = new[] { AppPermissions.HrManage },
                ["InvestmentProperty.SetUpBuilding"] = new[] { AppPermissions.AccountingManage }
            };

        /// <summary>
        /// True when the user may create or amend master data, as opposed to merely
        /// reading it. Views use this to decide whether to render Create/Edit controls.
        /// </summary>
        public static bool IsMasterDataController(string? controllerName) =>
            !string.IsNullOrWhiteSpace(controllerName) &&
            MasterDataReadPermissions.ContainsKey(controllerName);

        public static IReadOnlyList<string> ResolvePermissions(string? controllerName, string? actionName)
        {
            if (string.IsNullOrWhiteSpace(controllerName))
                return Array.Empty<string>();

            if (MasterDataReadPermissions.TryGetValue(controllerName, out var readPermissions))
            {
                return !string.IsNullOrWhiteSpace(actionName) &&
                       MasterDataReadActions.Contains(actionName)
                    ? readPermissions
                    : new[] { AppPermissions.MasterDataManage };
            }

            if (!string.IsNullOrWhiteSpace(actionName) &&
                ActionPermissionMap.TryGetValue($"{controllerName}.{actionName}", out var actionPermissions))
            {
                return actionPermissions;
            }

            return ControllerPermissionMap.TryGetValue(controllerName, out var permissions)
                ? permissions
                : Array.Empty<string>();
        }
    }
}
