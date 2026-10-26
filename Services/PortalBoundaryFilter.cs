using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Keeps tenants and staff on their own side of the application.
    ///
    /// This exists because of a real gap in the staff authorisation model:
    /// PermissionEnforcementFilter allows any controller that is absent from
    /// PermissionCatalog, and Home is absent - so any authenticated principal can
    /// otherwise open the management dashboard with the company's portfolio,
    /// collections and receivables on it.
    ///
    /// The rule is deliberately a whitelist by prefix rather than a list of exceptions,
    /// so a new staff controller is closed to tenants the moment it is created, without
    /// anyone having to remember to register it.
    ///
    ///   A principal holding a portal tenant claim  -> Portal* controllers only.
    ///   Anyone else                                -> everything except Portal*.
    /// </summary>
    public class PortalBoundaryFilter : IAsyncAuthorizationFilter
    {
        private const string PortalControllerPrefix = "Portal";
        private const string SelfServiceControllerPrefix = "SelfService";

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var endpoint = context.HttpContext.GetEndpoint();
            if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() != null)
                return Task.CompletedTask;

            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true)
                return Task.CompletedTask;

            var controllerName = context.RouteData.Values["controller"]?.ToString() ?? string.Empty;

            // Employees in their self-service portal: SelfService* controllers only, and
            // nobody else may use those.
            var isSelfServiceController = controllerName.StartsWith(SelfServiceControllerPrefix, StringComparison.OrdinalIgnoreCase);
            if (user.IsEmployeePortalUser())
            {
                if (!isSelfServiceController)
                    context.Result = new RedirectToActionResult("Index", "SelfService", null);
                return Task.CompletedTask;
            }

            if (isSelfServiceController)
            {
                context.Result = user.IsTenantPortalUser()
                    ? new RedirectToActionResult("Index", "Portal", null)
                    : new RedirectToActionResult("Index", "Home", null);
                return Task.CompletedTask;
            }

            var isPortalController = controllerName.StartsWith(PortalControllerPrefix, StringComparison.OrdinalIgnoreCase);
            var isPortalUser = user.IsTenantPortalUser();

            if (isPortalUser && !isPortalController)
            {
                // A tenant reaching into the staff application. Send them home rather
                // than to a staff access-denied page they cannot read.
                context.Result = new RedirectToActionResult("Index", "Portal", null);
                return Task.CompletedTask;
            }

            if (!isPortalUser && isPortalController)
            {
                // Staff reaching into the portal. The portal only makes sense in the
                // context of one tenant's own data, which staff do not have.
                context.Result = new RedirectToActionResult("Index", "Home", null);
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }
    }
}
