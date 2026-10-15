using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The tenant's own account: balance, contracts, invoices, payments, cheques and
    /// statement, plus the service requests they have raised.
    ///
    /// Every action resolves the tenant from the signed-in principal. No action accepts
    /// a tenant id, so a tenant cannot reach another tenant's data by changing a URL;
    /// the actions that take a request id pass the tenant id alongside it, so a
    /// mismatched pair finds nothing rather than someone else's request.
    ///
    /// The account screens are read only. The request screens write, but only to the
    /// request tables - nothing a tenant does here touches the ledger.
    /// </summary>
    [Route("portal")]
    public class PortalController : Controller
    {
        private readonly TenantPortalService _portalService;
        private readonly ServiceRequestService _serviceRequestService;

        public PortalController(
            TenantPortalService portalService,
            ServiceRequestService serviceRequestService)
        {
            _portalService = portalService;
            _serviceRequestService = serviceRequestService;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var tenantId = User.GetRequiredPortalTenantId();
            var model = await _portalService.GetDashboardAsync(tenantId, DateTime.Today);
            return View(model);
        }

        [HttpGet("invoices")]
        public async Task<IActionResult> Invoices()
        {
            var tenantId = User.GetRequiredPortalTenantId();
            var model = await _portalService.GetInvoicesAsync(tenantId);
            return View(model);
        }

        [HttpGet("payments")]
        public async Task<IActionResult> Payments()
        {
            var tenantId = User.GetRequiredPortalTenantId();

            ViewBag.Pdcs = await _portalService.GetPdcsAsync(tenantId);

            var model = await _portalService.GetReceiptsAsync(tenantId);
            return View(model);
        }

        [HttpGet("contracts")]
        public async Task<IActionResult> Contracts()
        {
            var tenantId = User.GetRequiredPortalTenantId();
            var model = await _portalService.GetContractsAsync(tenantId, DateTime.Today);
            return View(model);
        }

        [HttpGet("statement")]
        public async Task<IActionResult> Statement(DateTime? fromDate, DateTime? toDate)
        {
            var tenantId = User.GetRequiredPortalTenantId();

            var to = (toDate ?? DateTime.Today).Date;
            var from = (fromDate ?? to.AddYears(-1)).Date;

            if (from > to)
                (from, to) = (to, from);

            var model = await _portalService.GetStatementAsync(tenantId, from, to);
            return View(model);
        }

        // =====================================================================
        //  SERVICE REQUESTS
        // =====================================================================

        [HttpGet("requests")]
        public async Task<IActionResult> Requests()
        {
            var tenantId = User.GetRequiredPortalTenantId();
            var model = await _serviceRequestService.GetTenantRequestsAsync(tenantId);
            return View(model);
        }

        [HttpGet("requests/new")]
        public async Task<IActionResult> NewRequest()
        {
            var tenantId = User.GetRequiredPortalTenantId();
            var model = await _serviceRequestService.BuildCreateModelAsync(tenantId);
            return View(model);
        }

        [HttpPost("requests/new")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NewRequest(ServiceRequestCreateViewModel model)
        {
            var tenantId = User.GetRequiredPortalTenantId();

            var request = await _serviceRequestService.CreateAsync(
                ModelState, tenantId, User.GetPortalUserId(), model);

            if (request == null)
            {
                // Rebuild the dropdowns, which are not posted back with the form.
                await _serviceRequestService.PopulateOptionsAsync(model, tenantId);
                return View(model);
            }

            TempData["PortalSuccess"] =
                $"Request {request.RequestNumber} has been sent. You will see replies here.";

            return RedirectToAction(nameof(RequestDetails), new { id = request.Id });
        }

        [HttpGet("requests/{id:int}")]
        public async Task<IActionResult> RequestDetails(int id)
        {
            var tenantId = User.GetRequiredPortalTenantId();
            var model = await _serviceRequestService.GetTenantRequestAsync(tenantId, id);

            return model == null ? NotFound() : View(model);
        }

        [HttpPost("requests/{id:int}/reply")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReplyToRequest(int id, string body)
        {
            var tenantId = User.GetRequiredPortalTenantId();

            var added = await _serviceRequestService.AddTenantCommentAsync(
                tenantId, id, User.GetPortalUserId(), body);

            if (!added)
                TempData["PortalError"] = "The message could not be added. The request may be closed.";

            return RedirectToAction(nameof(RequestDetails), new { id });
        }

        [HttpPost("requests/{id:int}/cancel")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelRequest(int id)
        {
            var tenantId = User.GetRequiredPortalTenantId();

            var cancelled = await _serviceRequestService.CancelByTenantAsync(tenantId, id);

            TempData[cancelled ? "PortalSuccess" : "PortalError"] = cancelled
                ? "Your request has been withdrawn."
                : "This request can no longer be withdrawn because work has already started on it.";

            return RedirectToAction(nameof(RequestDetails), new { id });
        }
    }
}
