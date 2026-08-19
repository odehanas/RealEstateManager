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
        public const string AccountingManage = "Accounting.Manage";
        public const string ReportsView = "Reports.View";
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
                Key = AppPermissions.AccountingManage,
                Group = "Accounting",
                Name = "Manage Accounting",
                Description = "Invoices, receipts, payables, journals, closing, fixed assets, and prepayments."
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
                ["Properties"] = new[] { AppPermissions.MasterDataView, AppPermissions.MasterDataManage },
                ["Units"] = new[] { AppPermissions.MasterDataView, AppPermissions.MasterDataManage },
                ["Tenants"] = new[] { AppPermissions.MasterDataView, AppPermissions.MasterDataManage },

                // An accountant entering a bill must be able to find the supplier, so
                // Accounting.Manage carries read access - but not the right to create
                // one, which is the whole point of centralising.
                ["Vendors"] = new[]
                {
                    AppPermissions.MasterDataView,
                    AppPermissions.MasterDataManage,
                    AppPermissions.AccountingManage
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
                "PreviewCode"
            };

        private static readonly Dictionary<string, string[]> ControllerPermissionMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Leases"] = new[] { AppPermissions.LeasesManage },
                ["ChartOfAccounts"] = new[] { AppPermissions.AccountingManage },
                ["AccountMappings"] = new[] { AppPermissions.AccountingManage },
                ["OwnerWithdrawalCategories"] = new[] { AppPermissions.AccountingManage },
                ["PettyCash"] = new[] { AppPermissions.AccountingManage },
                ["VatSettings"] = new[] { AppPermissions.AccountingManage },
                ["CustomerInvoices"] = new[] { AppPermissions.AccountingManage },
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
                ["Reports"] = new[] { AppPermissions.ReportsView },
                ["TenantStatements"] = new[] { AppPermissions.ReportsView }
            };

        private static readonly Dictionary<string, string[]> ActionPermissionMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Journal.GeneralLedger"] = new[] { AppPermissions.ReportsView }
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
