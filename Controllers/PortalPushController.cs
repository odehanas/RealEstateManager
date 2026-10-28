using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Domain;
using RealEstateManager.Services;
using RealEstateManager.Services.Localization;
using RealEstateManager.Services.Push;

namespace RealEstateManager.Controllers
{
    /// <summary>A tenant turning notifications on or off for their phone. Under /portal, where the tenant cookie is sent.</summary>
    [Route("portal/push")]
    public class PortalPushController : Controller
    {
        private readonly PushRegistrationService _registrations;

        public PortalPushController(PushRegistrationService registrations)
        {
            _registrations = registrations;
        }

        [HttpPost("subscribe")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Subscribe(string? endpoint, string? p256dh, string? auth)
        {
            var userId = User.GetPortalUserId();
            if (userId == null)
                return Unauthorized();

            return await _registrations.SaveAsync(PushAudiences.Tenant, userId.Value, null,
                    endpoint, p256dh, auth, UiLanguages.Current, Request.Headers.UserAgent.ToString())
                ? NoContent()
                : BadRequest();
        }

        [HttpPost("unsubscribe")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unsubscribe(string? endpoint)
        {
            var userId = User.GetPortalUserId();
            if (userId != null)
                await _registrations.RemoveAsync(PushAudiences.Tenant, userId.Value, endpoint);
            return NoContent();
        }
    }
}
