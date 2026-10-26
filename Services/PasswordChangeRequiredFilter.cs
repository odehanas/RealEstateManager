using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RealEstateManager.Services
{
    /// <summary>
    /// A staff user signed in with the bootstrap ADMIN password, or with a password an
    /// administrator set for them, can do nothing but choose their own (or sign out).
    /// Nobody else then knows the password that opens their account.
    /// </summary>
    public class PasswordChangeRequiredFilter : IAuthorizationFilter
    {
        private static readonly HashSet<string> AllowedControllers =
            new(StringComparer.OrdinalIgnoreCase) { "Auth", "Profile" };

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() != null)
                return;

            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true || !user.HasClaim(x => x.Type == AppClaimTypes.MustChangePassword))
                return;

            var controller = context.RouteData.Values["controller"]?.ToString();
            if (controller != null && AllowedControllers.Contains(controller))
                return;

            context.Result = new RedirectToActionResult("ChangePassword", "Profile", null);
        }
    }
}
