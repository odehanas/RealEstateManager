using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    [Authorize(Roles = "SystemAdmin")]
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
            var model = await _auditLogService.GetLogsAsync(filter ?? new AuditLogFilterViewModel());
            return View(model);
        }
    }
}
