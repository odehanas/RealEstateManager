using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Who may open Users &amp; Permissions: a System Admin, or an administrator of the
    /// company they are working in. What each of them may then change is decided by
    /// AdminManagementService's UserAdminScope - a company administrator only ever sees
    /// the users of the companies they administer.
    /// </summary>
    public class UserManagerRequirement : IAuthorizationRequirement
    {
        public const string PolicyName = "UserManager";
    }

    public class UserManagerAuthorizationHandler : AuthorizationHandler<UserManagerRequirement>
    {
        private readonly ApplicationDbContext _db;

        public UserManagerAuthorizationHandler(ApplicationDbContext db)
        {
            _db = db;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            UserManagerRequirement requirement)
        {
            if (context.User.IsInRole("SystemAdmin"))
            {
                context.Succeed(requirement);
                return;
            }

            var userId = context.User.GetAppUserId();
            var companyId = context.User.GetCurrentCompanyId();
            if (!userId.HasValue || !companyId.HasValue)
                return;

            // Read on every request rather than taken from the sign-in cookie, so removing
            // someone's administrator rights takes effect at once.
            if (await IsCompanyAdminAsync(_db, userId.Value, companyId.Value))
                context.Succeed(requirement);
        }

        public static Task<bool> IsCompanyAdminAsync(ApplicationDbContext db, int userId, int companyId) =>
            db.AppUserCompanies
                .IgnoreQueryFilters()
                .AnyAsync(x => x.AppUserId == userId && x.CompanyId == companyId && x.IsCompanyAdmin);
    }
}
