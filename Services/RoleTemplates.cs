namespace RealEstateManager.Services
{
    /// <summary>
    /// The standard kinds of user. A role is a ready-made set of permissions that the
    /// Users screen ticks in one click; the permissions themselves stay the source of
    /// truth, so a role can still be adjusted for one person.
    /// </summary>
    public static class RoleTemplates
    {
        public const string SystemAdmin = "SystemAdmin";
        public const string CompanyAdmin = "CompanyAdmin";
        public const string Owner = "Owner";
        public const string Manager = "Manager";
        public const string DataEntry = "DataEntry";
        public const string MaintenanceEngineer = "MaintenanceEngineer";
        public const string Custom = "Custom";

        public sealed record Role(string Key, string Name, string Summary, string[] Permissions);

        /// <summary>The roles a company administrator assigns. System Admin is a flag, not a set of permissions.</summary>
        public static readonly IReadOnlyList<Role> Assignable = new[]
        {
            new Role(CompanyAdmin, "Company Administrator",
                "Everything in this company: all records, all approvals, users and settings. Tick \"Administrator of this company\" too.",
                PermissionCatalog.All.Select(x => x.Key).ToArray()),

            new Role(Owner, "Owner",
                "Sees how the business is doing and approves what reaches the owner level. Enters no records and does not see the activity logs.",
                new[]
                {
                    AppPermissions.PerformanceView, AppPermissions.OwnerOversight, AppPermissions.ReportsView,
                    AppPermissions.MasterDataView, AppPermissions.LeasesApprove, AppPermissions.PaymentsApprove,
                    AppPermissions.PayrollApprove, AppPermissions.PeriodReopenApprove, AppPermissions.AssistantUse
                }),

            new Role(Manager, "Manager (Approver)",
                "Views everything and approves contracts, payments and payroll up to their approval limit. Enters no records.",
                new[]
                {
                    AppPermissions.PerformanceView, AppPermissions.ReportsView, AppPermissions.MasterDataView,
                    AppPermissions.LeasesApprove, AppPermissions.PaymentsApprove, AppPermissions.PayrollApprove,
                    AppPermissions.AssistantUse
                }),

            new Role(DataEntry, "Data Entry (Normal User)",
                "Does the day-to-day work: tenants, contracts, invoices, receipts, cheques, bills, payments, HR and payroll preparation. Approves nothing.",
                new[]
                {
                    AppPermissions.MasterDataView, AppPermissions.MasterDataManage, AppPermissions.LeasesManage,
                    AppPermissions.AccountingManage, AppPermissions.ServiceRequestsManage, AppPermissions.HrManage,
                    AppPermissions.PayrollManage, AppPermissions.AssistantUse
                }),

            new Role(MaintenanceEngineer, "Maintenance Engineer / Technician",
                "Carries out maintenance jobs only: new tenant maintenance requests are assigned to them, and they see just their own jobs, record the work and materials, and mark them done.",
                new[] { AppPermissions.MaintenanceExecute })
        };

        /// <summary>Which role a user's actual permissions amount to, for the Users list and the Authorization Matrix.</summary>
        public static string Detect(bool isSystemAdmin, bool isCompanyAdmin, IReadOnlyCollection<string> permissions)
        {
            if (isSystemAdmin) return SystemAdmin;
            if (isCompanyAdmin) return CompanyAdmin;

            var set = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
            foreach (var role in Assignable.Where(r => r.Key != CompanyAdmin))
            {
                if (set.SetEquals(role.Permissions))
                    return role.Key;
            }

            return Custom;
        }

        public static string Name(string key) => key switch
        {
            SystemAdmin => "System Administrator",
            Custom => "Custom",
            _ => Assignable.FirstOrDefault(r => r.Key == key)?.Name ?? key
        };
    }
}
