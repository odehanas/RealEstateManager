using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// What approval means for one kind of document. The approval service keeps the
    /// requests and enforces the rules that are the same for every document; a handler
    /// says what happens to its own document when it is approved or sent back.
    /// </summary>
    public interface IApprovalHandler
    {
        string DocumentType { get; }

        /// <summary>Why the document cannot be approved right now, or null when it can.</summary>
        Task<string?> CheckCanApproveAsync(int documentId);

        /// <summary>Puts the approved document into effect.</summary>
        Task OnApprovedAsync(int documentId);

        /// <summary>Returns the document to its creator to correct (rejected or withdrawn).</summary>
        Task OnReturnedAsync(int documentId);

        /// <summary>Where staff open the document itself.</summary>
        string DocumentUrl(int documentId);
    }

    /// <summary>
    /// Maker-checker approval. A document is submitted; a second person approves it or
    /// returns it with a reason. The rules that hold for every document live here:
    /// one open request per document, and the four-eyes rule - nobody approves what
    /// they created or submitted. Every request is kept, so the history of who asked,
    /// who decided and why is never lost.
    /// </summary>
    public class ApprovalService
    {
        private readonly ApplicationDbContext _db;
        private readonly IServiceProvider _services;

        private readonly Push.PushNotifier _push;

        public ApprovalService(ApplicationDbContext db, IServiceProvider services, Push.PushNotifier push)
        {
            _db = db;
            _services = services;
            _push = push;
        }

        public async Task<ApprovalRequest> SubmitAsync(
            int companyId, string documentType, int documentId, string documentNumber, string summary,
            int? documentCreatedBy, int userId, string userName)
        {
            if (await GetPendingAsync(documentType, documentId) != null)
                throw new InvalidOperationException("This document is already waiting for approval.");

            var request = new ApprovalRequest
            {
                CompanyId = companyId,
                DocumentType = documentType,
                DocumentId = documentId,
                DocumentNumber = documentNumber,
                Summary = Limit(summary, 300),
                Status = ApprovalStatuses.Pending,
                RequestedByAppUserId = userId,
                RequestedByName = Limit(userName, 200),
                RequestedAt = DateTime.UtcNow,
                DocumentCreatedByAppUserId = documentCreatedBy
            };

            _db.ApprovalRequests.Add(request);
            await _db.SaveChangesAsync();

            await _push.ToPermissionAsync(companyId, AppPermissions.LeasesApprove, userId,
                "{0} is waiting for your approval", new object?[] { documentNumber },
                "{0}", new object?[] { request.Summary },
                "/Approvals", $"approval-{request.Id}");
            return request;
        }

        public Task<ApprovalRequest?> GetPendingAsync(string documentType, int documentId) =>
            _db.ApprovalRequests
                .Where(x => x.DocumentType == documentType && x.DocumentId == documentId && x.Status == ApprovalStatuses.Pending)
                .FirstOrDefaultAsync();

        public Task<ApprovalRequest?> GetAsync(int requestId) =>
            _db.ApprovalRequests.FirstOrDefaultAsync(x => x.Id == requestId);

        /// <summary>Every request ever made for one document, newest first.</summary>
        public Task<List<ApprovalRequest>> GetHistoryAsync(string documentType, int documentId) =>
            _db.ApprovalRequests.AsNoTracking()
                .Where(x => x.DocumentType == documentType && x.DocumentId == documentId)
                .OrderByDescending(x => x.RequestedAt).ThenByDescending(x => x.Id)
                .ToListAsync();

        /// <summary>The latest request for each of the given documents.</summary>
        public async Task<Dictionary<int, ApprovalRequest>> GetLatestAsync(string documentType, IEnumerable<int> documentIds)
        {
            var ids = documentIds.Distinct().ToList();
            var requests = await _db.ApprovalRequests.AsNoTracking()
                .Where(x => x.DocumentType == documentType && ids.Contains(x.DocumentId))
                .ToListAsync();

            return requests
                .GroupBy(x => x.DocumentId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.RequestedAt).ThenByDescending(x => x.Id).First());
        }

        /// <summary>The approvals screen: what is waiting, then what was decided lately.</summary>
        public Task<List<ApprovalRequest>> GetQueueAsync(bool pendingOnly, int take = 200) =>
            _db.ApprovalRequests.AsNoTracking()
                .Where(x => !pendingOnly || x.Status == ApprovalStatuses.Pending)
                .OrderBy(x => x.Status == ApprovalStatuses.Pending ? 0 : 1)
                .ThenByDescending(x => x.Status == ApprovalStatuses.Pending ? x.RequestedAt : x.DecidedAt)
                .Take(take)
                .ToListAsync();

        /// <summary>How many requests this person could approve now (not their own).</summary>
        public Task<int> CountAwaitingAsync(int userId) =>
            _db.ApprovalRequests.CountAsync(x =>
                x.Status == ApprovalStatuses.Pending &&
                x.RequestedByAppUserId != userId &&
                (x.DocumentCreatedByAppUserId == null || x.DocumentCreatedByAppUserId != userId));

        /// <summary>The four-eyes rule: why this person may not decide this request, or null.</summary>
        public static string? FourEyesBlock(ApprovalRequest request, int userId)
        {
            if (request.RequestedByAppUserId == userId)
                return "You submitted this for approval, so someone else has to approve it.";

            if (request.DocumentCreatedByAppUserId == userId)
                return "You created this document, so someone else has to approve it.";

            return null;
        }

        public async Task<(bool Success, string Message)> ApproveAsync(int requestId, int userId, string userName, string? comment)
        {
            var (request, handler, problem) = await LoadForDecisionAsync(requestId, userId);
            if (problem != null)
                return (false, problem);

            var blocked = await handler!.CheckCanApproveAsync(request!.DocumentId);
            if (blocked != null)
                return (false, blocked);

            await using var transaction = await _db.Database.BeginTransactionAsync();

            Decide(request, ApprovalStatuses.Approved, userId, userName, comment);
            await handler.OnApprovedAsync(request.DocumentId);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            if (request.RequestedByAppUserId != userId)
                _push.ToStaff(new[] { request.RequestedByAppUserId }, "{0} is approved", new object?[] { request.DocumentNumber },
                    "Approved by {0}.", new object?[] { userName }, DocumentUrl(request), $"approval-{request.Id}");

            return (true, $"{request.DocumentNumber} is approved and now in effect.");
        }

        /// <summary>Sends the document back to its creator. A reason is required - it is what they correct.</summary>
        public async Task<(bool Success, string Message)> RejectAsync(int requestId, int userId, string userName, string? comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return (false, "Say why it is being returned, so the creator knows what to correct.");

            var (request, handler, problem) = await LoadForDecisionAsync(requestId, userId);
            if (problem != null)
                return (false, problem);

            await using var transaction = await _db.Database.BeginTransactionAsync();

            Decide(request!, ApprovalStatuses.Rejected, userId, userName, comment);
            await handler!.OnReturnedAsync(request!.DocumentId);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            if (request.RequestedByAppUserId != userId)
                _push.ToStaff(new[] { request.RequestedByAppUserId }, "{0} was returned to you", new object?[] { request.DocumentNumber },
                    "{0}", new object?[] { comment!.Trim() }, DocumentUrl(request), $"approval-{request.Id}");

            return (true, $"{request.DocumentNumber} is returned to its creator with your reason.");
        }

        /// <summary>The person who submitted it (or created it) takes it back before anyone decides.</summary>
        public async Task<(bool Success, string Message)> WithdrawAsync(int requestId, int userId, string userName)
        {
            var request = await GetAsync(requestId);
            if (request == null || request.Status != ApprovalStatuses.Pending)
                return (false, "This request is no longer waiting for approval.");

            if (request.RequestedByAppUserId != userId && request.DocumentCreatedByAppUserId != userId)
                return (false, "Only the person who submitted or created it can withdraw it.");

            var handler = HandlerFor(request.DocumentType);
            if (handler == null)
                return (false, $"Documents of type {request.DocumentType} cannot be handled here.");

            await using var transaction = await _db.Database.BeginTransactionAsync();

            Decide(request, ApprovalStatuses.Withdrawn, userId, userName, "Withdrawn by the submitter.");
            await handler.OnReturnedAsync(request.DocumentId);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return (true, $"{request.DocumentNumber} is withdrawn and back in draft.");
        }

        public string DocumentUrl(ApprovalRequest request) =>
            HandlerFor(request.DocumentType)?.DocumentUrl(request.DocumentId) ?? "#";

        private async Task<(ApprovalRequest? Request, IApprovalHandler? Handler, string? Problem)> LoadForDecisionAsync(
            int requestId, int userId)
        {
            var request = await GetAsync(requestId);
            if (request == null)
                return (null, null, "The approval request was not found.");

            if (request.Status != ApprovalStatuses.Pending)
                return (null, null, $"{request.DocumentNumber} has already been {request.Status.ToLowerInvariant()}.");

            var fourEyes = FourEyesBlock(request, userId);
            if (fourEyes != null)
                return (null, null, fourEyes);

            var handler = HandlerFor(request.DocumentType);
            if (handler == null)
                return (null, null, $"Documents of type {request.DocumentType} cannot be approved here.");

            return (request, handler, null);
        }

        private static void Decide(ApprovalRequest request, string status, int userId, string userName, string? comment)
        {
            request.Status = status;
            request.DecidedByAppUserId = userId;
            request.DecidedByName = Limit(userName, 200);
            request.DecidedAt = DateTime.UtcNow;
            request.DecisionComment = string.IsNullOrWhiteSpace(comment) ? null : Limit(comment.Trim(), 1000);
        }

        // Handlers are looked up when needed, not injected: a handler may itself use this
        // service to submit its documents.
        private IApprovalHandler? HandlerFor(string documentType) =>
            _services.GetServices<IApprovalHandler>().FirstOrDefault(x => x.DocumentType == documentType);

        private static string Limit(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
