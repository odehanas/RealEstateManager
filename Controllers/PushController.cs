using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Domain;
using RealEstateManager.Services;
using RealEstateManager.Services.Localization;
using RealEstateManager.Services.Push;

namespace RealEstateManager.Controllers
{
    /// <summary>Staff turning notifications on or off for the device they are using.</summary>
    [Route("push")]
    public class PushController : Controller
    {
        private readonly PushRegistrationService _registrations;

        public PushController(PushRegistrationService registrations)
        {
            _registrations = registrations;
        }

        [HttpPost("subscribe")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Subscribe(string? endpoint, string? p256dh, string? auth)
        {
            var userId = User.GetAppUserId();
            if (userId == null)
                return Unauthorized();

            return await _registrations.SaveAsync(PushAudiences.Staff, userId.Value, User.GetCurrentCompanyId(),
                    endpoint, p256dh, auth, UiLanguages.Current, Request.Headers.UserAgent.ToString())
                ? NoContent()
                : BadRequest();
        }

        [HttpPost("unsubscribe")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unsubscribe(string? endpoint)
        {
            var userId = User.GetAppUserId();
            if (userId != null)
                await _registrations.RemoveAsync(PushAudiences.Staff, userId.Value, endpoint);
            return NoContent();
        }
    }
}
