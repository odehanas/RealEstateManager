using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The Authorization Matrix: what each role may do, how approvals and limits work in
    /// this company, and who holds which role and limit. Read only; changes are made in
    /// Users &amp; Permissions and Approval Limits.
    /// </summary>
    public class AuthorizationMatrixController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ApprovalAuthorityService _authority;

        public AuthorizationMatrixController(ApplicationDbContext db, ApprovalAuthorityService authority)
        {
            _db = db;
            _authority = authority;
        }

        /// <summary>One line of the matrix: a thing people do, and the permissions that allow it.</summary>
        public sealed record Capability(string Area, string What, string[] Permissions, bool Limited = false, bool AdminsOnly = false, bool SystemAdminOnly = false);

        public static readonly IReadOnlyList<Capability> Capabilities = new[]
        {
            new Capability("Master data", "View properties, units, tenants and vendors", new[] { AppPermissions.MasterDataView, AppPermissions.MasterDataManage }),
            new Capability("Master data", "Create and edit properties, units, tenants and vendors", new[] { AppPermissions.MasterDataManage }),
            new Capability("Leasing", "Prepare tenancy contracts and send them for approval", new[] { AppPermissions.LeasesManage }),
            new Capability("Leasing", "Approve tenancy contracts (never their own)", new[] { AppPermissions.LeasesApprove }, Limited: true),
            new Capability("Receivables", "Invoices, receipts, post-dated cheques, deposits, credit notes", new[] { AppPermissions.AccountingManage }),
            new Capability("Payables", "Vendor bills, vendor payments, petty cash, owner settlements", new[] { AppPermissions.AccountingManage }),
            new Capability("Payables", "Release payments waiting for approval (never their own)", new[] { AppPermissions.PaymentsApprove }, Limited: true),
            new Capability("Accounting", "Journals, fixed assets, revenue recognition, month-end closing", new[] { AppPermissions.AccountingManage }),
            new Capability("Accounting", "Approve reopening a closed month", new[] { AppPermissions.PeriodReopenApprove }),
            new Capability("HR & Payroll", "Employees, documents, leave, attendance, salary advances", new[] { AppPermissions.HrManage }),
            new Capability("HR & Payroll", "Prepare and pay the payroll (WPS, transfer, cheques)", new[] { AppPermissions.PayrollManage }),
            new Capability("HR & Payroll", "Approve the payroll into the accounts", new[] { AppPermissions.PayrollApprove }, Limited: true),
            new Capability("Tenants", "Answer tenant service requests from the portal", new[] { AppPermissions.ServiceRequestsManage }),
            new Capability("Viewing", "Financial reports, statements and Build Your Own Report", new[] { AppPermissions.ReportsView, AppPermissions.OwnerOversight }),
            new Capability("Viewing", "Business Performance page", new[] { AppPermissions.PerformanceView, AppPermissions.OwnerOversight }),
            new Capability("Viewing", "Owner Monitor: postings and payment releases day by day", new[] { AppPermissions.OwnerOversight }),
            new Capability("Viewing", "Activity log: what staff changed, refused attempts, locked accounts", Array.Empty<string>(), AdminsOnly: true),
            new Capability("Control", "Set approval limits and delegate authority while away", new[] { AppPermissions.OwnerOversight }),
            new Capability("Control", "Users, roles and permissions of this company", Array.Empty<string>(), AdminsOnly: true),
            new Capability("Control", "All companies, subscriptions, database updates", Array.Empty<string>(), SystemAdminOnly: true),
            new Capability("Assistant", "Use the AI assistant (only within the person's own rights)", new[] { AppPermissions.AssistantUse })
        };

        public sealed class PersonRow
        {
            public string Name { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
            public string Level { get; set; } = string.Empty;
            public string Authority { get; set; } = string.Empty;
            public bool IsSystemAdmin { get; set; }
            public HashSet<string> Permissions { get; set; } = new();
        }

        public sealed class MatrixViewModel
        {
            public Company Company { get; set; } = new();
            public List<ApprovalLevel> Levels { get; set; } = new();
            public List<ApprovalDelegation> Delegations { get; set; } = new();
            public List<PersonRow> People { get; set; } = new();
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var links = await _db.AppUserCompanies.IgnoreQueryFilters().AsNoTracking()
                .Include(x => x.AppUser).ThenInclude(x => x!.Permissions)
                .Include(x => x.ApprovalLevel)
                .Where(x => x.CompanyId == companyId && x.AppUser != null && x.AppUser.IsActive)
                .OrderBy(x => x.AppUser!.DisplayName)
                .ToListAsync();

            var people = new List<PersonRow>();
            foreach (var link in links)
            {
                var user = link.AppUser!;
                var permissions = user.Permissions.Select(p => p.PermissionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var authority = await _authority.GetAuthorityAsync(user.Id, companyId, user.IsSystemAdmin);
                people.Add(new PersonRow
                {
                    Name = user.DisplayName,
                    Username = user.Username,
                    IsSystemAdmin = user.IsSystemAdmin,
                    Role = RoleTemplates.Name(RoleTemplates.Detect(user.IsSystemAdmin, link.IsCompanyAdmin, permissions)),
                    Level = user.IsSystemAdmin ? "-" : link.ApprovalLevel?.Name ?? "None",
                    Authority = authority == null ? "None" : authority.Describe() + (authority.Source.Contains("delegat", StringComparison.OrdinalIgnoreCase) ? " (delegated)" : string.Empty),
                    Permissions = permissions
                });
            }

            return View(new MatrixViewModel
            {
                Company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId),
                Levels = await _authority.GetLevelsAsync(companyId),
                Delegations = (await _db.ApprovalDelegations.AsNoTracking()
                        .Where(x => x.CompanyId == companyId && x.RevokedAt == null && x.EndDate >= DateTime.Today)
                        .ToListAsync())
                    .Where(x => x.IsActiveOn(DateTime.Today)).ToList(),
                People = people
            });
        }
    }
}
