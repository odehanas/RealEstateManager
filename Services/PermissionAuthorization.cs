using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace RealEstateManager.Services
{
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public PermissionRequirement(string permissionKey)
        {
            PermissionKey = permissionKey;
        }

        public string PermissionKey { get; }
    }

    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly PermissionService _permissionService;

        public PermissionAuthorizationHandler(PermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            if (await _permissionService.HasPermissionAsync(context.User, requirement.PermissionKey))
            {
                context.Succeed(requirement);
            }
        }
    }

    public class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
    {
        public const string PolicyPrefix = "Permission:";

        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
            : base(options)
        {
        }

        public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (policyName.StartsWith(PolicyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var permissionKey = policyName[PolicyPrefix.Length..];
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(permissionKey))
                    .Build();

                return Task.FromResult<AuthorizationPolicy?>(policy);
            }

            return base.GetPolicyAsync(policyName);
        }
    }
}
