using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Releasing payments held back by payment approval. A payment waits as a draft journal
    /// until someone approves it whose authority covers the amount and who did not enter
    /// it; approving posts it. Every decision is kept.
    /// </summary>
    public class PaymentApprovalService
    {
        private readonly ApplicationDbContext _db;
        private readonly ApprovalAuthorityService _authority;
        private readonly JournalWorkflowService _workflow;

        private readonly Push.PushNotifier _push;

        public PaymentApprovalService(ApplicationDbContext db, ApprovalAuthorityService authority, JournalWorkflowService workflow,
            Push.PushNotifier push)
        {
            _db = db;
            _authority = authority;
            _workflow = workflow;
            _push = push;
        }

        /// <summary>The person who entered the payment hears what became of it.</summary>
        private void TellRequester(PaymentApproval approval, int deciderId, string title, string body, object?[] values)
        {
            if (approval.RequestedByAppUserId is int requester && requester != deciderId)
                _push.ToStaff(new[] { requester }, title, Array.Empty<object?>(), body, values,
                    "/PaymentApprovals?all=true", $"payment-{approval.Id}");
        }

        public Task<int> CountPendingAsync() =>
            _db.PaymentApprovals.CountAsync(x => x.Status == PaymentApprovalStatuses.Pending);

        public async Task<List<PaymentApproval>> GetQueueAsync(bool pendingOnly) =>
            await _db.PaymentApprovals.AsNoTracking()
                .Include(x => x.JournalEntry).ThenInclude(x => x!.Lines).ThenInclude(x => x.Account)
                .Where(x => !pendingOnly || x.Status == PaymentApprovalStatuses.Pending)
                .OrderBy(x => x.Status == PaymentApprovalStatuses.Pending ? 0 : 1)
                .ThenByDescending(x => x.RequestedAt)
                .Take(300)
                .ToListAsync();

        /// <summary>Why this person may not release this payment, or null when they may.</summary>
        public async Task<string?> CannotDecideAsync(PaymentApproval approval, int userId, bool isSystemAdmin)
        {
            if (approval.Status != PaymentApprovalStatuses.Pending)
                return "This payment has already been decided.";
            if (approval.RequestedByAppUserId == userId && !isSystemAdmin)
                return "You entered this payment, so someone else has to release it.";

            var authority = await _authority.GetAuthorityAsync(userId, approval.CompanyId, isSystemAdmin);
            if (authority == null)
                return "You are not on an approval level. Ask an owner to place you on one in System Setup ▸ Approval Limits.";
            if (!authority.Covers(approval.Amount))
                return $"{approval.Amount:N2} is above your authority ({authority.Describe()}). It needs {approval.RequiredLevel} level or above.";

            return null;
        }

        public async Task<(bool Success, string Message)> ApproveAsync(int id, int userId, string userName, bool isSystemAdmin, string? comment)
        {
            var approval = await _db.PaymentApprovals.FirstOrDefaultAsync(x => x.Id == id);
            if (approval == null)
                return (false, "The payment was not found.");

            var problem = await CannotDecideAsync(approval, userId, isSystemAdmin);
            if (problem != null)
                return (false, problem);

            var authority = await _authority.GetAuthorityAsync(userId, approval.CompanyId, isSystemAdmin);
            approval.Status = PaymentApprovalStatuses.Approved;
            approval.DecidedByAppUserId = userId;
            approval.DecidedByName = Limit(userName, 200);
            approval.DecidedAt = DateTime.UtcNow;
            approval.Comment = Limit(string.IsNullOrWhiteSpace(comment)
                ? $"Released under {authority!.Source} ({authority.Describe()})."
                : $"{comment.Trim()} — under {authority!.Source}.", 500);
            await _db.SaveChangesAsync();

            var posted = await _workflow.PostAsync(approval.JournalEntryId);
            if (!posted.Success)
            {
                // The approval stands only if the payment really reached the accounts.
                approval.Status = PaymentApprovalStatuses.Pending;
                approval.DecidedByAppUserId = null;
                approval.DecidedByName = null;
                approval.DecidedAt = null;
                approval.Comment = null;
                await _db.SaveChangesAsync();
                return (false, $"Not released: {posted.Message}");
            }

            TellRequester(approval, userId, "Your payment is released", "{0} - released by {1}.", new object?[] { approval.Description, userName });
            return (true, $"Payment of {approval.Amount:N2} released and posted. {approval.Description}");
        }

        public async Task<(bool Success, string Message)> RejectAsync(int id, int userId, string userName, bool isSystemAdmin, string? comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return (false, "Say why the payment is turned down, so the person who entered it knows what to do.");

            var approval = await _db.PaymentApprovals.FirstOrDefaultAsync(x => x.Id == id);
            if (approval == null)
                return (false, "The payment was not found.");

            var problem = await CannotDecideAsync(approval, userId, isSystemAdmin);
            if (problem != null)
                return (false, problem);

            approval.Status = PaymentApprovalStatuses.Rejected;
            approval.DecidedByAppUserId = userId;
            approval.DecidedByName = Limit(userName, 200);
            approval.DecidedAt = DateTime.UtcNow;
            approval.Comment = Limit(comment.Trim(), 500);
            await _db.SaveChangesAsync();
            TellRequester(approval, userId, "Your payment was turned down", "{0}: {1}", new object?[] { approval.Description, comment.Trim() });
            return (true, $"Payment of {approval.Amount:N2} turned down. It stays a draft, out of the accounts.");
        }

        /// <summary>
        /// For screens that post directly rather than through a draft (payroll, salary
        /// advances): whether this person's authority covers an amount, when payment
        /// approval is on. Null means they may go ahead.
        /// </summary>
        public async Task<string?> CheckDirectAuthorityAsync(int companyId, int userId, bool isSystemAdmin, decimal amount, string what)
        {
            if (!await _authority.IsPaymentApprovalOnAsync(companyId))
                return null;

            var authority = await _authority.GetAuthorityAsync(userId, companyId, isSystemAdmin);
            if (authority != null && authority.Covers(amount))
                return null;

            var level = await _authority.RequiredLevelAsync(companyId, amount);
            return $"{what} of {amount:N2} needs {level} level or above to approve" +
                   (authority == null ? "; you are not on an approval level." : $"; your authority is {authority.Describe()}.");
        }

        private static string Limit(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
