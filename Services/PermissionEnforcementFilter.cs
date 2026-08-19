using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RealEstateManager.Services
{
    public class PermissionEnforcementFilter : IAsyncAuthorizationFilter
    {
        private readonly PermissionService _permissionService;

        public PermissionEnforcementFilter(PermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var endpoint = context.HttpContext.GetEndpoint();
            if (endpoint?.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() != null)
                return;

            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true)
                return;

            var controllerName = context.RouteData.Values["controller"]?.ToString();
            var actionName = context.RouteData.Values["action"]?.ToString();
            var requiredPermissions = PermissionCatalog.ResolvePermissions(controllerName, actionName);

            if (requiredPermissions.Count == 0)
                return;

            if (await _permissionService.HasAnyPermissionAsync(user, requiredPermissions.ToArray()))
                return;

            context.Result = new ForbidResult();
        }
    }
}
