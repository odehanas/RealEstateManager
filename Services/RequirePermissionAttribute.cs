using Microsoft.AspNetCore.Authorization;

namespace RealEstateManager.Services
{
    public class RequirePermissionAttribute : AuthorizeAttribute
    {
        public RequirePermissionAttribute(string permissionKey)
        {
            Policy = $"{PermissionPolicyProvider.PolicyPrefix}{permissionKey}";
        }
    }
}
