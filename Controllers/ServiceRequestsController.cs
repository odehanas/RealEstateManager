using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The staff queue for everything tenants have asked for through the portal.
    ///
    /// Mirror image of the portal side: these actions are scoped by COMPANY rather than
    /// by tenant, because a member of staff legitimately sees every tenant's requests
    /// within their own company and nothing outside it.
    /// </summary>
    public class ServiceRequestsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ServiceRequestService _serviceRequestService;

        public ServiceRequestsController(
            ApplicationDbContext db,
            ServiceRequestService serviceRequestService)
        {
            _db = db;
            _serviceRequestService = serviceRequestService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? status, string? type)
        {
            var companyId = User.GetRequiredCurrentCompanyId();

            // Default to the work still outstanding - a queue that opens on everything
            // ever submitted is a queue nobody uses.
            status ??= "Open";

            var model = await _serviceRequestService.GetQueueAsync(companyId, status, type);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var model = await _serviceRequestService.GetStaffRequestAsync(companyId, id);

            if (model == null)
                return NotFound();

            await LoadListsAsync();
            return View(model);
        }

        /// <summary>
        /// Logs a request that reached the office another way (WhatsApp, a call, a visit)
        /// for a tenant. Choosing the tenant reloads the form with their own contracts and
        /// unpaid invoices. From here on it is an ordinary request on the tenant's record.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Create(int? tenantId)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var tenants = await _serviceRequestService.GetTenantOptionsAsync(companyId);

            var model = tenantId.HasValue && tenants.Any(x => x.Id == tenantId.Value)
                ? await _serviceRequestService.BuildCreateModelAsync(tenantId.Value)
                : new Models.ServiceRequestCreateViewModel();
            model.ReceivedAt = DateTime.Now;

            await LoadCreateListsAsync(tenants, tenantId);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int? tenantId, Models.ServiceRequestCreateViewModel model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var tenants = await _serviceRequestService.GetTenantOptionsAsync(companyId);

            if (!tenantId.HasValue || tenants.All(x => x.Id != tenantId.Value))
            {
                ModelState.AddModelError(string.Empty, "Choose the tenant the request belongs to.");
                await LoadCreateListsAsync(tenants, null);
                return View(model);
            }

            var request = await _serviceRequestService.CreateAsync(
                ModelState, tenantId.Value, null, model, true, User.GetAppUserId(), GetDisplayName());

            if (request == null)
            {
                await _serviceRequestService.PopulateOptionsAsync(model, tenantId.Value);
                await LoadCreateListsAsync(tenants, tenantId);
                return View(model);
            }

            TempData["RequestSuccess"] = $"Request {request.RequestNumber} logged for the tenant.";
            return RedirectToAction(nameof(Details), new { id = request.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(
            int id, string status, string? resolutionNotes, int? assignedToAppUserId, string? priority)
        {
            var companyId = User.GetRequiredCurrentCompanyId();

            var (success, message) = await _serviceRequestService.UpdateStatusAsync(
                companyId, id, status, resolutionNotes, assignedToAppUserId, priority,
                User.GetAppUserId(), GetDisplayName());

            TempData[success ? "RequestSuccess" : "RequestError"] = message;

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddComment(int id, string body, bool isInternal)
        {
            var companyId = User.GetRequiredCurrentCompanyId();

            var added = await _serviceRequestService.AddStaffCommentAsync(
                companyId, id, User.GetAppUserId(), GetDisplayName(), body, isInternal);

            if (!added)
                TempData["RequestError"] = "The message could not be added.";
            else if (isInternal)
                TempData["RequestSuccess"] = "Internal note added. The tenant cannot see it.";
            else
                TempData["RequestSuccess"] = "Reply sent. The tenant will see it on their portal.";

            return RedirectToAction(nameof(Details), new { id });
        }

        /// <summary>
        /// Carries out an agreed postponement. Deliberately a separate, explicit action
        /// rather than something that happens when the request is resolved: agreeing to
        /// give a tenant more time and actually moving the date on the invoice are two
        /// decisions, and the second one changes a record outside this feature.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApplyPostponement(int id)
        {
            var companyId = User.GetRequiredCurrentCompanyId();

            var (success, message) = await _serviceRequestService.ApplyPostponementAsync(
                companyId, id, User.GetAppUserId(), GetDisplayName());

            TempData[success ? "RequestSuccess" : "RequestError"] = message;

            return RedirectToAction(nameof(Details), new { id });
        }

        private string? GetDisplayName() =>
            User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.Identity?.Name;

        private async Task LoadCreateListsAsync(List<Models.ServiceRequestTenantOption> tenants, int? tenantId)
        {
            ViewBag.Tenants = tenants;
            ViewBag.TenantId = tenantId;

            // Staff are not stopped, but they should know before promising a visit.
            if (tenantId.HasValue)
                ViewBag.MaintenanceHold = (await TenantStandingService.GetAsync(_db, tenantId.Value)).Message;
        }

        private async Task LoadListsAsync()
        {
            var companyId = User.GetRequiredCurrentCompanyId();

            ViewBag.StaffUsers = await _db.AppUserCompanies
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .Select(x => new SelectListItem
                {
                    Value = x.AppUserId.ToString(),
                    Text = x.AppUser!.DisplayName
                })
                .Distinct()
                .OrderBy(x => x.Text)
                .ToListAsync();

            ViewBag.Priorities = ServiceRequestPriorities.All
                .Select(x => new SelectListItem { Value = x, Text = x })
                .ToList();
        }
    }
}
