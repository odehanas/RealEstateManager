using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The tenant's own account: balance, contracts, invoices, payments, cheques and
    /// statement. Read only - Phase 1 shows information and changes nothing.
    ///
    /// Every action resolves the tenant from the signed-in principal. No action accepts
    /// a tenant id, so a tenant cannot reach another tenant's data by changing a URL.
    /// </summary>
    [Route("portal")]
    public class PortalController : Controller
    {
        private readonly TenantPortalService _portalService;

        public PortalController(TenantPortalService portalService)
        {
            _portalService = portalService;
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
    }
}
