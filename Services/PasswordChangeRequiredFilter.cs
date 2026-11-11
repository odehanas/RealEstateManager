using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RealEstateManager.Services
{
    /// <summary>
    /// A staff user signed in with the bootstrap ADMIN password, or with a password an
    /// administrator set for them, can do nothing but choose their own (or sign out).
    /// Nobody else then knows the password that opens their account.
    ///
    /// In the same way, a user of a company that requires two-step sign-in who has not
    /// set it up yet can do nothing but set it up.
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
            if (user.Identity?.IsAuthenticated != true)
                return;

            var mustChangePassword = user.HasClaim(x => x.Type == AppClaimTypes.MustChangePassword);
            var mustSetUpTwoStep = user.HasClaim(x => x.Type == AppClaimTypes.MustSetUpTwoStep);
            if (!mustChangePassword && !mustSetUpTwoStep)
                return;

            var controller = context.RouteData.Values["controller"]?.ToString();
            if (controller != null && AllowedControllers.Contains(controller))
                return;

            // The password first: it is the older of the two secrets.
            context.Result = new RedirectToActionResult(mustChangePassword ? "ChangePassword" : "TwoStep", "Profile", null);
        }
    }
}
