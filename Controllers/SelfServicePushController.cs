using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Domain;
using RealEstateManager.Services;
using RealEstateManager.Services.Localization;
using RealEstateManager.Services.Push;

namespace RealEstateManager.Controllers
{
    /// <summary>An employee turning notifications on or off for their phone. Under /me, where the employee cookie is sent.</summary>
    [Route("me/push")]
    public class SelfServicePushController : Controller
    {
        private readonly PushRegistrationService _registrations;

        public SelfServicePushController(PushRegistrationService registrations)
        {
            _registrations = registrations;
        }

        [HttpPost("subscribe")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Subscribe(string? endpoint, string? p256dh, string? auth)
        {
            var userId = User.GetEmployeePortalUserId();
            if (userId == null)
                return Unauthorized();

            return await _registrations.SaveAsync(PushAudiences.Employee, userId.Value, null,
                    endpoint, p256dh, auth, UiLanguages.Current, Request.Headers.UserAgent.ToString())
                ? NoContent()
                : BadRequest();
        }

        [HttpPost("unsubscribe")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unsubscribe(string? endpoint)
        {
            var userId = User.GetEmployeePortalUserId();
            if (userId != null)
                await _registrations.RemoveAsync(PushAudiences.Employee, userId.Value, endpoint);
            return NoContent();
        }
    }
}
