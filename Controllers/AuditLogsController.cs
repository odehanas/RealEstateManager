using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// What people did in the system. A System Admin sees every company; a company's own
    /// administrator sees only the company they are working in.
    /// </summary>
    [Authorize(Policy = UserManagerRequirement.PolicyName)]
    public class AuditLogsController : Controller
    {
        private readonly AuditLogService _auditLogService;

        public AuditLogsController(AuditLogService auditLogService)
        {
            _auditLogService = auditLogService;
        }

        [HttpGet]
        public async Task<IActionResult> Index([FromQuery] AuditLogFilterViewModel filter)
        {
            // The policy lets in only a System Admin or an administrator of the current company.
            int? onlyCompanyId = User.IsInRole("SystemAdmin") ? null : User.GetRequiredCurrentCompanyId();

            var model = await _auditLogService.GetLogsAsync(filter ?? new AuditLogFilterViewModel(), onlyCompanyId);
            return View(model);
        }
    }
}
