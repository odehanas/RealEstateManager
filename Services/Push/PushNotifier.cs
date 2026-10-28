using System.Globalization;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services.Localization;

namespace RealEstateManager.Services.Push
{
    /// <summary>
    /// One notification, written in English with {0}-style values. Each device gets it in
    /// its own language: the text is translated through the dictionary when it is sent.
    /// </summary>
    public record PushMessage(
        string Audience,
        IReadOnlyList<int> UserIds,
        string Title,
        object?[] TitleValues,
        string Body,
        object?[] BodyValues,
        string Url,
        string Tag);

    /// <summary>
    /// Says who should hear about something: a staff user, the holders of a permission in a
    /// company, a tenant or an employee. It only decides the recipients; the sending happens
    /// in the background (PushWorker), so a slow push service never holds up a screen.
    ///
    /// Notifications carry a title, one line and a link - never amounts or personal details
    /// beyond what the person would see on the page, because a phone shows them on the lock
    /// screen.
    /// </summary>
    public class PushNotifier
    {
        private readonly ApplicationDbContext _db;
        private readonly PushQueue _queue;
        private readonly WebPushSender _sender;

        public PushNotifier(ApplicationDbContext db, PushQueue queue, WebPushSender sender)
        {
            _db = db;
            _queue = queue;
            _sender = sender;
        }

        public bool IsEnabled => _sender.IsConfigured;

        public void ToStaff(IEnumerable<int> appUserIds, string title, object?[] titleValues, string body, object?[] bodyValues, string url, string tag)
        {
            var ids = appUserIds.Distinct().ToList();
            if (IsEnabled && ids.Count > 0)
                _queue.Enqueue(new PushMessage(PushAudiences.Staff, ids, title, titleValues, body, bodyValues, url, tag));
        }

        /// <summary>Everyone active in the company who holds the permission, except the person who acted.</summary>
        public async Task ToPermissionAsync(int companyId, string permissionKey, int? exceptAppUserId,
            string title, object?[] titleValues, string body, object?[] bodyValues, string url, string tag)
        {
            if (!IsEnabled)
                return;

            var ids = await _db.AppUserCompanies.AsNoTracking().IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.AppUser!.IsActive &&
                            x.AppUser.Permissions.Any(p => p.PermissionKey == permissionKey))
                .Select(x => x.AppUserId)
                .ToListAsync();

            ToStaff(ids.Where(x => x != exceptAppUserId), title, titleValues, body, bodyValues, url, tag);
        }

        /// <summary>Every active portal login of the tenant.</summary>
        public async Task ToTenantAsync(int tenantId, string title, object?[] titleValues, string body, object?[] bodyValues, string url, string tag)
        {
            if (!IsEnabled)
                return;

            var ids = await _db.TenantPortalUsers.AsNoTracking().IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && x.IsActive)
                .Select(x => x.Id)
                .ToListAsync();

            if (ids.Count > 0)
                _queue.Enqueue(new PushMessage(PushAudiences.Tenant, ids, title, titleValues, body, bodyValues, url, tag));
        }

        /// <summary>The employee's self-service login.</summary>
        public async Task ToEmployeeAsync(int employeeId, string title, object?[] titleValues, string body, object?[] bodyValues, string url, string tag)
        {
            if (!IsEnabled)
                return;

            var ids = await _db.EmployeePortalUsers.AsNoTracking().IgnoreQueryFilters()
                .Where(x => x.EmployeeId == employeeId && x.IsActive)
                .Select(x => x.Id)
                .ToListAsync();

            if (ids.Count > 0)
                _queue.Enqueue(new PushMessage(PushAudiences.Employee, ids, title, titleValues, body, bodyValues, url, tag));
        }
    }

    /// <summary>Notifications waiting to be sent. Bounded: if the push services are down, the oldest give way.</summary>
    public class PushQueue
    {
        private readonly Channel<PushMessage> _channel = Channel.CreateBounded<PushMessage>(
            new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

        public void Enqueue(PushMessage message) => _channel.Writer.TryWrite(message);

        public IAsyncEnumerable<PushMessage> ReadAllAsync(CancellationToken cancellation) => _channel.Reader.ReadAllAsync(cancellation);
    }

    /// <summary>Sends the queued notifications to every device of each recipient, in that device's language.</summary>
    public class PushWorker : BackgroundService
    {
        private const int DropAfterFailures = 5;
        private readonly PushQueue _queue;
        private readonly IServiceScopeFactory _scopes;
        private readonly UiText _text;
        private readonly ILogger<PushWorker> _logger;

        public PushWorker(PushQueue queue, IServiceScopeFactory scopes, UiText text, ILogger<PushWorker> logger)
        {
            _queue = queue;
            _scopes = scopes;
            _text = text;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var message in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await SendAsync(message, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "A notification could not be sent.");
                }
            }
        }

        private async Task SendAsync(PushMessage message, CancellationToken cancellation)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sender = scope.ServiceProvider.GetRequiredService<WebPushSender>();

            var query = db.PushSubscriptions.IgnoreQueryFilters().Where(x => x.Audience == message.Audience);
            query = message.Audience switch
            {
                PushAudiences.Tenant => query.Where(x => x.TenantPortalUserId != null && message.UserIds.Contains(x.TenantPortalUserId.Value)),
                PushAudiences.Employee => query.Where(x => x.EmployeePortalUserId != null && message.UserIds.Contains(x.EmployeePortalUserId.Value)),
                _ => query.Where(x => x.AppUserId != null && message.UserIds.Contains(x.AppUserId.Value))
            };

            var devices = await query.ToListAsync(cancellation);
            if (devices.Count == 0)
                return;

            var culture = CultureInfo.CurrentUICulture;
            try
            {
                foreach (var device in devices)
                {
                    // The dictionary follows the UI culture, so each device is written in its own language.
                    CultureInfo.CurrentUICulture = new CultureInfo(device.Language == UiLanguages.Arabic ? "ar-AE" : "en-US");
                    var payload = new
                    {
                        title = _text.Format(message.Title, message.TitleValues),
                        body = _text.Format(message.Body, message.BodyValues),
                        url = message.Url,
                        tag = message.Tag,
                        dir = device.Language == UiLanguages.Arabic ? "rtl" : "ltr",
                        lang = device.Language
                    };

                    var result = await sender.SendAsync(device.Endpoint, device.P256dh, device.Auth, payload, cancellation);
                    if (result == PushResult.Sent)
                    {
                        device.LastSentAt = DateTime.UtcNow;
                        device.FailureCount = 0;
                    }
                    else if (result == PushResult.Gone || ++device.FailureCount >= DropAfterFailures)
                    {
                        db.PushSubscriptions.Remove(device);
                    }
                }
            }
            finally
            {
                CultureInfo.CurrentUICulture = culture;
            }

            await db.SaveChangesAsync(cancellation);
        }
    }
}
