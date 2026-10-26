using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Payments waiting for release: supplier payments, petty cash, owner settlements and
    /// withdrawals above the person's own limit. Each approver sees what they may release.
    /// </summary>
    public class PaymentApprovalsController : Controller
    {
        private readonly PaymentApprovalService _approvals;

        public PaymentApprovalsController(PaymentApprovalService approvals)
        {
            _approvals = approvals;
        }

        [HttpGet]
        public async Task<IActionResult> Index(bool all = false)
        {
            var queue = await _approvals.GetQueueAsync(pendingOnly: !all);
            var userId = User.GetAppUserId() ?? 0;
            var blocks = new Dictionary<int, string?>();
            foreach (var item in queue.Where(x => x.Status == PaymentApprovalStatuses.Pending))
                blocks[item.Id] = await _approvals.CannotDecideAsync(item, userId, User.IsInRole("SystemAdmin"));

            ViewBag.All = all;
            ViewBag.Blocks = blocks;
            return View(queue);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id, string? comment)
        {
            var (success, message) = await _approvals.ApproveAsync(id, User.GetAppUserId() ?? 0, CurrentUserName(), User.IsInRole("SystemAdmin"), comment);
            TempData[success ? "ApprovalSuccess" : "ApprovalError"] = message;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string? comment)
        {
            var (success, message) = await _approvals.RejectAsync(id, User.GetAppUserId() ?? 0, CurrentUserName(), User.IsInRole("SystemAdmin"), comment);
            TempData[success ? "ApprovalSuccess" : "ApprovalError"] = message;
            return RedirectToAction(nameof(Index));
        }

        private string CurrentUserName() =>
            User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value ?? "User";
    }
}
