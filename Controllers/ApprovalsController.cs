using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The approver's desk: documents waiting for a second person, the review of each,
    /// and the company's switch for whether contracts need approval at all. Open to
    /// holders of the Approve Contracts permission (see AppPermissions).
    /// </summary>
    public class ApprovalsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ApprovalService _approvals;

        public ApprovalsController(ApplicationDbContext db, ApprovalService approvals)
        {
            _db = db;
            _approvals = approvals;
        }

        [HttpGet]
        public async Task<IActionResult> Index(bool all = false)
        {
            var requests = await _approvals.GetQueueAsync(pendingOnly: !all);
            ViewBag.ShowAll = all;
            ViewBag.UserId = User.GetAppUserId();
            var companyId = User.GetRequiredCurrentCompanyId();
            ViewBag.ApprovalRequired = (await _db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == companyId))?.RequireContractApproval == true;
            return View(requests);
        }

        /// <summary>Everything the approver needs to decide: the terms, the instalments it will create, the history.</summary>
        [HttpGet]
        public async Task<IActionResult> Review(int id)
        {
            var request = await _approvals.GetAsync(id);
            if (request == null)
                return NotFound();

            var model = new ApprovalReviewViewModel
            {
                Request = request,
                History = await _approvals.GetHistoryAsync(request.DocumentType, request.DocumentId),
                FourEyesBlock = ApprovalService.FourEyesBlock(request, User.GetAppUserId() ?? 0),
                DocumentUrl = _approvals.DocumentUrl(request)
            };

            if (request.DocumentType == ApprovalDocumentTypes.Lease)
            {
                model.Lease = await _db.Leases.AsNoTracking()
                    .Include(x => x.Tenant)
                    .Include(x => x.Unit).ThenInclude(x => x!.Property)
                    .FirstOrDefaultAsync(x => x.Id == request.DocumentId);

                if (model.Lease != null)
                    model.Instalments = LeaseScheduleBuilder.PlanInvoices(model.Lease.CompanyId, model.Lease);
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id, string? comment)
        {
            var (success, message) = await _approvals.ApproveAsync(id, User.GetAppUserId() ?? 0, CurrentUserName(), comment);
            TempData[success ? "ApprovalSuccess" : "ApprovalError"] = message;
            return success ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Review), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string? comment)
        {
            var (success, message) = await _approvals.RejectAsync(id, User.GetAppUserId() ?? 0, CurrentUserName(), comment);
            TempData[success ? "ApprovalSuccess" : "ApprovalError"] = message;
            return success ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Review), new { id });
        }

        /// <summary>
        /// Switches contract approval on or off for the current company. Only approvers can
        /// reach it, so the people being checked cannot switch the check off.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Settings(bool requireContractApproval)
        {
            // Companies carry no company filter: without the id this would switch whichever company is first.
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return NotFound();

            if (!requireContractApproval &&
                await _db.Leases.AnyAsync(x => x.Status == LeaseStatuses.PendingApproval))
            {
                TempData["ApprovalError"] = "Some contracts are still waiting for approval. Approve, return or withdraw them " +
                                            "before switching approval off.";
                return RedirectToAction(nameof(Index));
            }

            company.RequireContractApproval = requireContractApproval;
            await _db.SaveChangesAsync();

            TempData["ApprovalSuccess"] = requireContractApproval
                ? "Contract approval is on: every new contract must be approved by a second person before it takes effect."
                : "Contract approval is off: a contract takes effect when its creator activates it.";
            return RedirectToAction(nameof(Index));
        }

        private string CurrentUserName() =>
            User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value ?? "User";
    }
}
