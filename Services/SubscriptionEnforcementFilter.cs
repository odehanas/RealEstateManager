using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RealEstateManager.Services
{
    public class SubscriptionEnforcementFilter : IAsyncAuthorizationFilter
    {
        private static readonly HashSet<string> AllowedControllers =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Auth",
                "Home",
                "Feedback",
                "Help",
                "Profile",
                "Subscription",
                // Portal sign-in, sign-out, password change and the service-unavailable
                // notice must keep working whatever the subscription state, or a tenant
                // cannot even be told why the portal is closed.
                "PortalAccount"
            };

        private readonly SubscriptionService _subscriptionService;

        public SubscriptionEnforcementFilter(SubscriptionService subscriptionService)
        {
            _subscriptionService = subscriptionService;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var endpoint = context.HttpContext.GetEndpoint();
            if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() != null)
                return;

            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true)
                return;

            if (user.IsInRole("SystemAdmin"))
                return;

            var companyId = user.GetCurrentCompanyId();
            if (!companyId.HasValue)
                return;

            var controllerName = context.RouteData.Values["controller"]?.ToString();
            if (!string.IsNullOrWhiteSpace(controllerName) && AllowedControllers.Contains(controllerName))
                return;

            if (await _subscriptionService.IsCompanyAccessibleAsync(companyId.Value))
                return;

            // A tenant must not be sent to the staff Subscription page: PortalBoundaryFilter
            // would send them straight back here, giving an infinite redirect. They also
            // cannot renew it - it is the landlord's subscription, not theirs.
            if (user.IsTenantPortalUser())
            {
                context.Result = new RedirectToActionResult("Unavailable", "PortalAccount", null);
                return;
            }

            if (user.IsEmployeePortalUser())
            {
                context.Result = new RedirectToActionResult("Unavailable", "SelfServiceAccount", null);
                return;
            }

            var currentPath = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString;
            context.Result = new RedirectToActionResult(
                "Index",
                "Subscription",
                new { expired = true, returnUrl = currentPath.ToString() });
        }
    }
}
