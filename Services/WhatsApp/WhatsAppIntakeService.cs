using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services.WhatsApp
{
    /// <summary>
    /// Turns messages from WhatsApp into tenant requests.
    ///
    /// THREE RULES HOLD THIS TOGETHER.
    ///
    /// 1. A MESSAGE GOES ON A TENANT'S RECORD ONLY WHEN ITS NUMBER MATCHES EXACTLY ONE
    ///    ACTIVE TENANT OF THIS COMPANY. Anything else waits for a person. A wrong guess
    ///    would show one tenant's words on another tenant's portal.
    ///
    /// 2. THE MESSAGE IS SAVED FIRST, under a unique id. That is what makes a repeated
    ///    delivery harmless, and it means a failure afterwards leaves the message in the
    ///    review list rather than losing it.
    ///
    /// 3. NOBODY IS SIGNED IN. A delivery runs in the company of the connection it came in
    ///    on (CompanyContextOverride), so every query is filtered to that company. The
    ///    queries here also name the company themselves.
    /// </summary>
    public class WhatsAppIntakeService
    {
        /// <summary>
        /// People send "Hi", then the problem, then the flat number. Messages this close to
        /// the request they opened join it rather than each becoming a request of its own.
        /// </summary>
        private static readonly TimeSpan FollowUpWindow = TimeSpan.FromMinutes(30);

        /// <summary>A message still marked Received after this long was interrupted, and goes to a person.</summary>
        private static readonly TimeSpan InterruptedAfter = TimeSpan.FromMinutes(5);

        private const string AutomaticAuthor = "WhatsApp (automatic)";
        private const int MaxBodyLength = 4096;
        private const int MaxDescriptionLength = 4000;
        private const int MaxSubjectLength = 80;
        private const int MaxAttempts = 3;

        private readonly ApplicationDbContext _db;
        private readonly ServiceRequestService _requests;
        private readonly SubscriptionService _subscriptions;
        private readonly Push.PushNotifier _push;
        private readonly CompanyContextOverride _companyContext;
        private readonly ILogger<WhatsAppIntakeService> _logger;

        public WhatsAppIntakeService(
            ApplicationDbContext db,
            ServiceRequestService requests,
            SubscriptionService subscriptions,
            Push.PushNotifier push,
            CompanyContextOverride companyContext,
            ILogger<WhatsAppIntakeService> logger)
        {
            _db = db;
            _requests = requests;
            _subscriptions = subscriptions;
            _push = push;
            _companyContext = companyContext;
            _logger = logger;
        }

        // =====================================================================
        //  A DELIVERY FROM META
        // =====================================================================

        /// <summary>Takes the messages of one delivery, whose signature has already been checked.</summary>
        public async Task ReceiveAsync(WhatsAppConnection connection, IEnumerable<WhatsAppDeliveredMessage> messages)
        {
            _companyContext.CompanyId = connection.CompanyId;

            foreach (var message in messages)
            {
                // One app can receive for several numbers, and holding one company's secret
                // must not let anyone write into another company.
                if (message.PhoneNumberId != connection.PhoneNumberId)
                {
                    _logger.LogWarning("WhatsApp delivery for number id {PhoneNumberId} ignored on the connection of company {CompanyId}.",
                        message.PhoneNumberId, connection.CompanyId);
                    continue;
                }

                var saved = await SaveOnceAsync(connection.CompanyId, message);
                if (saved != null)
                    await RouteAsync(connection, saved);
            }
        }

        /// <summary>Saves the message, or returns null if it has been delivered before.</summary>
        private async Task<WhatsAppInboundMessage?> SaveOnceAsync(int companyId, WhatsAppDeliveredMessage message)
        {
            if (await _db.WhatsAppInboundMessages.AsNoTracking()
                    .AnyAsync(x => x.CompanyId == companyId && x.MessageId == message.MessageId))
                return null;

            var row = new WhatsAppInboundMessage
            {
                CompanyId = companyId,
                MessageId = Cut(message.MessageId, 200)!,
                Sender = Cut(message.Sender, 128)!,
                ProfileName = Cut(message.ProfileName, 200),
                MessageType = Cut(message.Type, 30)!,
                Body = Cut(message.Text?.Trim(), MaxBodyLength),
                SentAt = message.SentAtUtc ?? DateTime.UtcNow,
                ReceivedAt = DateTime.UtcNow,
                Status = WhatsAppMessageStatuses.Received
            };

            _db.WhatsAppInboundMessages.Add(row);
            try
            {
                await _db.SaveChangesAsync();
                return row;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // The same message arriving twice at the same moment: the other delivery has it.
                _db.ChangeTracker.Clear();
                return null;
            }
        }

        private async Task RouteAsync(WhatsAppConnection connection, WhatsAppInboundMessage message)
        {
            try
            {
                var reason = await TryPutOnTenantAsync(connection, message);
                if (reason != null)
                    await SendToReviewAsync(message, reason);
            }
            catch (Exception ex)
            {
                // Saved, so never lost: Meta gets its answer and a person gets the message.
                _logger.LogError(ex, "WhatsApp message {MessageRowId} of company {CompanyId} could not be processed.", message.Id, message.CompanyId);
                _db.ChangeTracker.Clear();
                await SendToReviewAsync(message, WhatsAppReviewReasons.CouldNotCreate);
            }
        }

        /// <summary>Returns null when the message reached a tenant's record, otherwise why it could not.</summary>
        private async Task<string?> TryPutOnTenantAsync(WhatsAppConnection connection, WhatsAppInboundMessage message)
        {
            // A company whose subscription has lapsed cannot work requests or open its portal.
            // What arrives meanwhile is kept for them to see when they renew.
            if (!await _subscriptions.IsCompanyAccessibleAsync(message.CompanyId))
                return WhatsAppReviewReasons.SubscriptionInactive;

            var number = WhatsAppPhoneNumbers.SenderNumber(message.Sender);
            if (number == null)
                return WhatsAppReviewReasons.NumberHidden;

            var tenants = await ActiveTenantPhonesAsync(message.CompanyId);
            var matches = Matching(tenants, number, connection.DefaultCallingCode);

            if (matches.Count == 0)
                return WhatsAppReviewReasons.NoMatch;
            if (matches.Count > 1)
                return WhatsAppReviewReasons.SeveralTenants;
            if (string.IsNullOrWhiteSpace(message.Body))
                return WhatsAppReviewReasons.NoText;

            var tenantId = matches[0].Id;

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync();
                    await TakeCompanyTurnAsync(message.CompanyId);

                    if (!await TryAddToRecentRequestAsync(message, tenantId) && !await CreateRequestAsync(message, tenantId))
                        return WhatsAppReviewReasons.CouldNotCreate;

                    await transaction.CommitAsync();
                    return null;
                }
                catch (DbUpdateException ex) when (attempt < MaxAttempts && IsUniqueViolation(ex))
                {
                    // Someone on the portal took the same request number: the next try gets the next one.
                    _db.ChangeTracker.Clear();
                }
            }
        }

        /// <summary>
        /// One message per company at a time, held until the transaction ends. Otherwise several
        /// tenants writing at once all reach for the same request number, and one person's two
        /// quick messages would each open a request because neither can see the other.
        /// </summary>
        private Task TakeCompanyTurnAsync(int companyId)
        {
            var resource = $"WhatsAppIntake-{companyId}";
            return _db.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000");
        }

        private async Task<bool> TryAddToRecentRequestAsync(WhatsAppInboundMessage message, int tenantId)
        {
            var since = DateTime.UtcNow - FollowUpWindow;
            var requestId = await _db.WhatsAppInboundMessages.AsNoTracking()
                .Where(x => x.CompanyId == message.CompanyId && x.TenantId == tenantId && x.Sender == message.Sender &&
                            x.Id != message.Id && x.ReceivedAt >= since && x.TenantServiceRequestId != null &&
                            (x.Status == WhatsAppMessageStatuses.RequestCreated || x.Status == WhatsAppMessageStatuses.AddedToRequest) &&
                            (x.TenantServiceRequest!.Status == ServiceRequestStatuses.Submitted ||
                             x.TenantServiceRequest.Status == ServiceRequestStatuses.Acknowledged ||
                             x.TenantServiceRequest.Status == ServiceRequestStatuses.InProgress))
                .OrderByDescending(x => x.ReceivedAt)
                .Select(x => x.TenantServiceRequestId)
                .FirstOrDefaultAsync();

            if (requestId == null)
                return false;

            var added = await _requests.AddTenantCommentAsync(
                tenantId, requestId.Value, null, Cut(message.Body, MaxDescriptionLength)!);
            if (!added)
                return false;

            await MarkAsync(message.Id, message.CompanyId, WhatsAppMessageStatuses.AddedToRequest, tenantId, requestId);
            return true;
        }

        private async Task<bool> CreateRequestAsync(WhatsAppInboundMessage message, int tenantId)
        {
            // Nothing in a free-form message says what kind of request it is, so a person
            // sorts it: see ClassifyWhatsAppRequestAsync. Calling everything maintenance would
            // send rent questions to the technicians, and would let a tenant whose maintenance
            // is on hold for non-payment get round the hold by sending a message.
            var model = await _requests.BuildCreateModelAsync(tenantId);
            FillFromMessage(model, message);
            model.RequestType = ServiceRequestTypes.Other;

            var modelState = new ModelStateDictionary();
            var request = await _requests.CreateAsync(modelState, tenantId, null, model,
                loggedByStaff: true, loggedByAppUserId: null, loggedByName: AutomaticAuthor, notifyStaff: true);

            if (request == null)
            {
                _logger.LogWarning("WhatsApp message {MessageRowId} failed validation: {Errors}", message.Id,
                    string.Join("; ", modelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                return false;
            }

            await MarkAsync(message.Id, message.CompanyId, WhatsAppMessageStatuses.RequestCreated, tenantId, request.Id);
            return true;
        }

        private async Task SendToReviewAsync(WhatsAppInboundMessage message, string reason)
        {
            await _db.WhatsAppInboundMessages
                .Where(x => x.Id == message.Id && x.CompanyId == message.CompanyId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, WhatsAppMessageStatuses.NeedsReview)
                    .SetProperty(x => x.ReviewReason, reason));

            await _push.ToPermissionAsync(message.CompanyId, AppPermissions.ServiceRequestsManage, null,
                "WhatsApp message to review", Array.Empty<object?>(),
                "{0}", new object?[] { message.ProfileName ?? WhatsAppPhoneNumbers.Display(message.Sender) },
                "/WhatsAppInbox", "whatsapp-review");
        }

        private Task<int> MarkAsync(int id, int companyId, string status, int tenantId, int? requestId) =>
            _db.WhatsAppInboundMessages
                .Where(x => x.Id == id && x.CompanyId == companyId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, status)
                    .SetProperty(x => x.ReviewReason, (string?)null)
                    .SetProperty(x => x.TenantId, tenantId)
                    .SetProperty(x => x.TenantServiceRequestId, requestId));

        // =====================================================================
        //  THE REVIEW LIST
        // =====================================================================

        private static IQueryable<WhatsAppInboundMessage> Waiting(IQueryable<WhatsAppInboundMessage> query)
        {
            var interrupted = DateTime.UtcNow - InterruptedAfter;
            return query.Where(x => x.Status == WhatsAppMessageStatuses.NeedsReview ||
                                    (x.Status == WhatsAppMessageStatuses.Received && x.ReceivedAt < interrupted));
        }

        public Task<int> CountWaitingAsync(int companyId) =>
            Waiting(_db.WhatsAppInboundMessages.AsNoTracking().Where(x => x.CompanyId == companyId)).CountAsync();

        public async Task<WhatsAppInboxViewModel> GetInboxAsync(int companyId, bool showHandled)
        {
            const int pageSize = 200;
            var mine = _db.WhatsAppInboundMessages.AsNoTracking().Where(x => x.CompanyId == companyId);
            var waiting = Waiting(mine);

            var query = showHandled
                ? mine.Where(x => x.Status != WhatsAppMessageStatuses.NeedsReview && x.Status != WhatsAppMessageStatuses.Received)
                : waiting;

            var rows = await query
                .OrderByDescending(x => x.ReceivedAt)
                .Take(pageSize)
                .Select(x => new WhatsAppInboxItemViewModel
                {
                    Id = x.Id,
                    Sender = x.Sender,
                    ProfileName = x.ProfileName,
                    MessageType = x.MessageType,
                    Body = x.Body,
                    SentAt = x.SentAt,
                    Status = x.Status,
                    ReviewReason = x.ReviewReason,
                    TenantName = x.Tenant != null ? x.Tenant.FullName : null,
                    RequestId = x.TenantServiceRequestId,
                    RequestNumber = x.TenantServiceRequest != null ? x.TenantServiceRequest.RequestNumber : null
                })
                .ToListAsync();

            if (!showHandled && rows.Count > 0)
            {
                var callingCode = await CallingCodeAsync(companyId);
                var tenants = await ActiveTenantPhonesAsync(companyId);
                foreach (var row in rows)
                {
                    if (WhatsAppPhoneNumbers.SenderNumber(row.Sender) is { } number)
                        row.Candidates = Matching(tenants, number, callingCode);
                }
            }

            return new WhatsAppInboxViewModel
            {
                ShowHandled = showHandled,
                Items = rows,
                WaitingCount = await waiting.CountAsync(),
                IsConnected = await _db.WhatsAppConnections.AsNoTracking()
                    .AnyAsync(x => x.CompanyId == companyId && x.IsEnabled)
            };
        }

        /// <summary>A message still waiting for a person, or null.</summary>
        public Task<WhatsAppInboundMessage?> GetWaitingAsync(int companyId, int id) =>
            Waiting(_db.WhatsAppInboundMessages.AsNoTracking().Where(x => x.CompanyId == companyId && x.Id == id))
                .FirstOrDefaultAsync();

        /// <summary>Puts the message's text and time on the Log a Request form.</summary>
        public static void FillFromMessage(ServiceRequestCreateViewModel model, WhatsAppInboundMessage message)
        {
            model.Source = ServiceRequestSources.WhatsApp;
            // A clock ahead of ours must not stop the request: its time just becomes now.
            model.ReceivedAt = message.SentAt > DateTime.UtcNow ? null : DateTime.SpecifyKind(message.SentAt, DateTimeKind.Utc).ToLocalTime();
            model.Subject = SubjectFrom(message.Body);
            model.Description = Cut(message.Body, MaxDescriptionLength) ?? string.Empty;
        }

        /// <summary>
        /// A member of staff logging a waiting message for the tenant they chose. The message
        /// is claimed first and everything happens together, so two people working the list
        /// cannot both turn it into a request. Returns null with modelState saying why.
        /// </summary>
        public async Task<TenantServiceRequest?> LogForTenantAsync(
            ModelStateDictionary modelState, int companyId, int messageId, int tenantId,
            ServiceRequestCreateViewModel model, int? appUserId, string? userName)
        {
            model.Source = ServiceRequestSources.WhatsApp;

            await using var transaction = await _db.Database.BeginTransactionAsync();

            var claimed = await Waiting(_db.WhatsAppInboundMessages.Where(x => x.CompanyId == companyId && x.Id == messageId))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, WhatsAppMessageStatuses.LoggedByStaff)
                    .SetProperty(x => x.TenantId, tenantId)
                    .SetProperty(x => x.HandledByAppUserId, appUserId)
                    .SetProperty(x => x.HandledAt, DateTime.UtcNow));

            if (claimed == 0)
            {
                modelState.AddModelError(string.Empty, "Someone has already dealt with this WhatsApp message.");
                return null;
            }

            var request = await _requests.CreateAsync(modelState, tenantId, null, model, true, appUserId, userName);
            if (request == null)
                return null;

            await _db.WhatsAppInboundMessages
                .Where(x => x.CompanyId == companyId && x.Id == messageId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.TenantServiceRequestId, request.Id));

            await transaction.CommitAsync();
            return request;
        }

        public async Task<bool> DismissAsync(int companyId, int id, int? appUserId) =>
            await Waiting(_db.WhatsAppInboundMessages.Where(x => x.CompanyId == companyId && x.Id == id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, WhatsAppMessageStatuses.Dismissed)
                    .SetProperty(x => x.HandledByAppUserId, appUserId)
                    .SetProperty(x => x.HandledAt, DateTime.UtcNow)) > 0;

        // =====================================================================
        //  SHARED
        // =====================================================================

        private async Task<string> CallingCodeAsync(int companyId) =>
            await _db.WhatsAppConnections.AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .Select(x => x.DefaultCallingCode)
                .FirstOrDefaultAsync() ?? "971";

        private Task<List<WhatsAppTenantOption>> ActiveTenantPhonesAsync(int companyId) =>
            _db.Tenants.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.IsActive && x.Phone != null && x.Phone != "")
                .OrderBy(x => x.FullName)
                .Select(x => new WhatsAppTenantOption { Id = x.Id, Code = x.Code, Name = x.FullName, Phone = x.Phone })
                .ToListAsync();

        private static List<WhatsAppTenantOption> Matching(List<WhatsAppTenantOption> tenants, string number, string callingCode) =>
            tenants.Where(t => WhatsAppPhoneNumbers.RecordedNumbers(t.Phone, callingCode).Contains(number)).ToList();

        private static string SubjectFrom(string? body)
        {
            var firstLine = body?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? string.Empty;

            if (firstLine.Length <= MaxSubjectLength)
                return firstLine;

            var cut = firstLine[..MaxSubjectLength];
            var space = cut.LastIndexOf(' ');
            return (space > MaxSubjectLength / 2 ? cut[..space] : cut) + "...";
        }

        private static string? Cut(string? value, int length) =>
            value == null || value.Length <= length ? value : value[..length];

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is SqlException { Number: 2601 or 2627 };
    }
}
