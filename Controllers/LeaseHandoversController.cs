using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The handover of a unit when its contract ends. Everyone concerned can open a
    /// handover; each step belongs to its own people - see PermissionCatalog
    /// (ControllerPermissionMap for the screen, ActionPermissionMap for the steps).
    /// </summary>
    [Authorize]
    public class LeaseHandoversController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly LeaseHandoverService _handovers;

        public LeaseHandoversController(ApplicationDbContext db, LeaseHandoverService handovers)
        {
            _db = db;
            _handovers = handovers;
        }

        private int CompanyId => User.GetRequiredCurrentCompanyId();
        private string? UserName => User.Identity?.Name;

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var companyId = CompanyId;
            var rows = await _db.LeaseHandovers.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.Status == HandoverStatuses.Open ? 0 : 1).ThenByDescending(x => x.HandoverDate)
                .Include(x => x.Lease)!.ThenInclude(x => x!.Tenant)
                .Include(x => x.Lease)!.ThenInclude(x => x!.Unit)
                .ToListAsync();

            ViewBag.Leases = await _handovers.LeasesToHandOverAsync(companyId);
            return View(rows);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Start(int leaseId, DateTime? handoverDate)
        {
            var (handover, error) = await _handovers.StartAsync(CompanyId, leaseId, handoverDate ?? DateTime.Today, UserName);
            if (handover == null)
            {
                TempData["ErrorMessage"] = error;
                return RedirectToAction(nameof(Index));
            }

            TempData["SuccessMessage"] = $"Handover {handover.HandoverNumber} opened. Start with the inspection.";
            return RedirectToAction(nameof(Details), new { id = handover.Id });
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var handover = await _handovers.LoadAsync(CompanyId, id);
            if (handover == null)
                return NotFound();

            ViewBag.Summary = await _handovers.SummaryAsync(handover);
            return View(handover);
        }

        /// <summary>The final report, to print and sign.</summary>
        [HttpGet]
        public async Task<IActionResult> Report(int id)
        {
            var handover = await _handovers.LoadAsync(CompanyId, id);
            if (handover == null)
                return NotFound();

            ViewBag.Summary = await _handovers.SummaryAsync(handover);
            ViewBag.CompanyName = User.FindFirst(AppClaimTypes.CurrentCompanyName)?.Value;
            return View(handover);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveInspection(LeaseHandoverService.InspectionInput input) =>
            Done(input.Id, await _handovers.SaveInspectionAsync(CompanyId, input, UserName));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCharge(int id, string? description, decimal amount) =>
            Done(id, await _handovers.AddChargeAsync(CompanyId, id, description, amount, UserName));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveCharge(int id, int chargeId) =>
            Done(id, await _handovers.RemoveChargeAsync(CompanyId, id, chargeId));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SignMaintenance(int id, string? notes) =>
            Done(id, await _handovers.SignMaintenanceAsync(CompanyId, id, notes, UserName));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReopenMaintenance(int id) =>
            Done(id, await _handovers.ReopenMaintenanceAsync(CompanyId, id));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Settle(int id, string? notes) =>
            Done(id, await _handovers.SettleAsync(CompanyId, id, notes, UserName));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var error = await _handovers.CancelAsync(CompanyId, id);
            TempData[error == null ? "SuccessMessage" : "ErrorMessage"] = error ?? "Handover cancelled. The contract stays in force.";
            return RedirectToAction(nameof(Index));
        }

        // ---- the company's checklist ---------------------------------------

        [HttpGet]
        public async Task<IActionResult> Checklist() =>
            View(await _handovers.ChecklistAsync(CompanyId));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddChecklistItem(string? name)
        {
            var error = await _handovers.AddChecklistItemAsync(CompanyId, name);
            TempData[error == null ? "SuccessMessage" : "ErrorMessage"] = error ?? "Added to the checklist.";
            return RedirectToAction(nameof(Checklist));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeChecklistItem(int id, string? name, string? move, bool toggle)
        {
            var error = await _handovers.ChangeChecklistItemAsync(CompanyId, id, name, move, toggle);
            if (error != null)
                TempData["ErrorMessage"] = error;
            return RedirectToAction(nameof(Checklist));
        }

        private IActionResult Done(int id, (bool Ok, string Message) result)
        {
            TempData[result.Ok ? "SuccessMessage" : "ErrorMessage"] = result.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
