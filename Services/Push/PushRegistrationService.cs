using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services.Push
{
    /// <summary>
    /// Records and forgets the devices people turn notifications on for. The browser hands
    /// over an address at its push service and two keys; only addresses at the known push
    /// services are accepted, so the server can never be steered into calling anywhere else.
    /// </summary>
    public class PushRegistrationService
    {
        // Google (Chrome, Edge on Android, Samsung), Apple (Safari, iPhone), Mozilla (Firefox), Microsoft (Edge on Windows).
        private static readonly string[] PushHosts =
        {
            "fcm.googleapis.com", "android.googleapis.com", "push.apple.com",
            "push.services.mozilla.com", "notify.windows.com"
        };

        private readonly ApplicationDbContext _db;

        public PushRegistrationService(ApplicationDbContext db)
        {
            _db = db;
        }

        public static bool IsPushServiceAddress(string? endpoint) =>
            Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            endpoint!.Length <= 1000 &&
            PushHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                               uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Saves the device for this person. A device already registered to someone else -
        /// a shared phone, signed in by another person - moves to whoever signed in now.
        /// </summary>
        public async Task<bool> SaveAsync(string audience, int userId, int? companyId,
            string? endpoint, string? p256dh, string? auth, string language, string? userAgent)
        {
            if (!IsPushServiceAddress(endpoint) || string.IsNullOrWhiteSpace(p256dh) || string.IsNullOrWhiteSpace(auth) ||
                p256dh.Length > 200 || auth.Length > 100)
                return false;

            var hash = Hash(endpoint!);
            var device = await _db.PushSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.EndpointHash == hash);
            if (device == null)
            {
                device = new PushSubscription { Endpoint = endpoint!, EndpointHash = hash, CreatedAt = DateTime.UtcNow };
                _db.PushSubscriptions.Add(device);
            }

            device.Audience = audience;
            device.AppUserId = audience == PushAudiences.Staff ? userId : null;
            device.TenantPortalUserId = audience == PushAudiences.Tenant ? userId : null;
            device.EmployeePortalUserId = audience == PushAudiences.Employee ? userId : null;
            device.CompanyId = companyId;
            device.P256dh = p256dh.Trim();
            device.Auth = auth.Trim();
            device.Language = Localization.UiLanguages.Clean(language);
            device.UserAgent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent;
            device.FailureCount = 0;

            await _db.SaveChangesAsync();
            return true;
        }

        /// <summary>Forgets the device, if it is this person's.</summary>
        public async Task RemoveAsync(string audience, int userId, string? endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                return;

            var hash = Hash(endpoint);
            var device = await _db.PushSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.EndpointHash == hash && x.Audience == audience);
            if (device == null)
                return;

            var owner = audience switch
            {
                PushAudiences.Tenant => device.TenantPortalUserId,
                PushAudiences.Employee => device.EmployeePortalUserId,
                _ => device.AppUserId
            };

            if (owner == userId)
            {
                _db.PushSubscriptions.Remove(device);
                await _db.SaveChangesAsync();
            }
        }

        private static string Hash(string endpoint) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint))).ToLowerInvariant();
    }
}
