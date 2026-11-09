using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The planned maintenance programme: work done on a schedule, each entry opening its
    /// own job a few days before it is due. The owner may look; whoever handles tenant
    /// requests sets it up.
    /// </summary>
    public class MaintenancePlansController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly MaintenanceService _maintenance;
        private readonly ServiceRequestService _requests;
        private readonly PermissionService _permissions;

        public MaintenancePlansController(ApplicationDbContext db, MaintenanceService maintenance, ServiceRequestService requests, PermissionService permissions)
        {
            _db = db;
            _maintenance = maintenance;
            _requests = requests;
            _permissions = permissions;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var plans = await _db.MaintenancePlans.AsNoTracking()
                .Include(x => x.Property)
                .Include(x => x.Unit)
                .OrderByDescending(x => x.IsActive).ThenBy(x => x.NextDueDate)
                .ToListAsync();

            // The latest job of each plan, and whether it is still open.
            var latest = await _db.TenantServiceRequests.AsNoTracking()
                .Where(x => x.MaintenancePlanId != null)
                .GroupBy(x => x.MaintenancePlanId!.Value)
                .Select(g => g.OrderByDescending(x => x.SubmittedAt).Select(x => new { x.MaintenancePlanId, x.Id, x.RequestNumber, x.Status, x.PreferredVisitDate }).First())
                .ToListAsync();

            ViewBag.Latest = latest.ToDictionary(x => x.MaintenancePlanId!.Value, x => (x.Id, x.RequestNumber, x.Status, x.PreferredVisitDate));
            ViewBag.CanEdit = await CanEditAsync();
            ViewBag.Technicians = (await MaintenanceService.TechniciansAsync(_db, User.GetRequiredCurrentCompanyId())).ToDictionary(x => x.Id, x => x.Name);
            return View(plans);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            if (!await CanEditAsync()) return Forbid();
            await LoadListsAsync();
            return View("Form", new MaintenancePlan { NextDueDate = DateTime.Today.AddDays(14) });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireFormFields("Name", "PropertyId", "IntervalMonths", "NextDueDate")]
        public async Task<IActionResult> Create(MaintenancePlan model)
        {
            if (!await CanEditAsync()) return Forbid();
            model.Id = 0;
            return await SaveAsync(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            if (!await CanEditAsync()) return Forbid();
            var plan = await _db.MaintenancePlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (plan == null) return NotFound();
            await LoadListsAsync();
            return View("Form", plan);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireFormFields("Name", "PropertyId", "IntervalMonths", "NextDueDate")]
        public async Task<IActionResult> Edit(int id, MaintenancePlan model)
        {
            if (!await CanEditAsync()) return Forbid();
            model.Id = id;
            return await SaveAsync(model);
        }

        /// <summary>Opens the jobs that have come due now, rather than waiting for the next run.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OpenDue()
        {
            if (!await CanEditAsync()) return Forbid();
            var opened = await _maintenance.OpenDueJobsAsync(User.GetRequiredCurrentCompanyId(), _requests, DateTime.Today);
            TempData["PlanMessage"] = opened == 0 ? "Nothing is due yet." : $"{opened} planned job(s) opened.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<IActionResult> SaveAsync(MaintenancePlan model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            ModelState.Remove(nameof(MaintenancePlan.Company));
            ModelState.Remove(nameof(MaintenancePlan.Property));
            ModelState.Remove(nameof(MaintenancePlan.Unit));

            model.Name = model.Name?.Trim() ?? string.Empty;
            if (model.Name.Length == 0)
                ModelState.AddModelError(nameof(model.Name), "Give the work a name, e.g. AC service.");
            if (!await _db.Properties.AnyAsync(x => x.Id == model.PropertyId && x.CompanyId == companyId))
                ModelState.AddModelError(nameof(model.PropertyId), "Choose the property.");
            if (model.UnitId.HasValue && !await _db.Units.AnyAsync(x => x.Id == model.UnitId.Value && x.PropertyId == model.PropertyId))
                ModelState.AddModelError(nameof(model.UnitId), "That unit is not in the property chosen.");
            if (model.IntervalMonths < MaintenancePlan.MinInterval || model.IntervalMonths > MaintenancePlan.MaxInterval)
                ModelState.AddModelError(nameof(model.IntervalMonths), "The work recurs every 1 to 24 months.");
            if (model.LeadDays < 0 || model.LeadDays > MaintenancePlan.MaxLeadDays)
                ModelState.AddModelError(nameof(model.LeadDays), "Open the job between 0 and 60 days before it is due.");
            if (!ServiceRequestPriorities.All.Contains(model.Priority))
                ModelState.AddModelError(nameof(model.Priority), "Choose how urgent the work is.");
            if (!string.IsNullOrWhiteSpace(model.Category) && !ServiceRequestCategories.Maintenance.Contains(model.Category))
                ModelState.AddModelError(nameof(model.Category), "Choose the kind of work from the list.");
            if (model.AssignToAppUserId.HasValue &&
                (await MaintenanceService.TechniciansAsync(_db, companyId)).All(x => x.Id != model.AssignToAppUserId.Value))
                ModelState.AddModelError(nameof(model.AssignToAppUserId), "Choose one of the maintenance team.");

            MaintenancePlan? plan = null;
            if (model.Id != 0)
            {
                plan = await _db.MaintenancePlans.FirstOrDefaultAsync(x => x.Id == model.Id && x.CompanyId == companyId);
                if (plan == null) return NotFound();
            }

            if (!ModelState.IsValid)
            {
                await LoadListsAsync();
                return View("Form", model);
            }

            if (plan == null)
            {
                plan = new MaintenancePlan
                {
                    CompanyId = companyId,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByName = User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.Identity?.Name
                };
                _db.MaintenancePlans.Add(plan);
            }

            plan.Name = model.Name;
            plan.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
            plan.PropertyId = model.PropertyId;
            plan.UnitId = model.UnitId;
            plan.Category = string.IsNullOrWhiteSpace(model.Category) ? null : model.Category;
            plan.Priority = model.Priority;
            plan.IntervalMonths = model.IntervalMonths;
            plan.NextDueDate = model.NextDueDate.Date;
            plan.LeadDays = model.LeadDays;
            plan.AssignToAppUserId = model.AssignToAppUserId;
            plan.IsActive = model.IsActive;
            await _db.SaveChangesAsync();

            TempData["PlanMessage"] = $"Planned maintenance \"{plan.Name}\" saved.";
            return RedirectToAction(nameof(Index));
        }

        private Task<bool> CanEditAsync() => _permissions.HasPermissionAsync(User, AppPermissions.ServiceRequestsManage);

        private async Task LoadListsAsync()
        {
            ViewBag.Properties = await _db.Properties.AsNoTracking()
                .Where(x => x.IsActive).OrderBy(x => x.Code)
                .Select(x => new { x.Id, Name = x.Code + " " + x.Name })
                .ToListAsync();
            ViewBag.Units = await _db.Units.AsNoTracking()
                .Where(x => x.Property!.IsActive).OrderBy(x => x.UnitNumber)
                .Select(x => new { x.Id, x.PropertyId, x.UnitNumber })
                .ToListAsync();
            ViewBag.Technicians = await MaintenanceService.TechniciansAsync(_db, User.GetRequiredCurrentCompanyId());
        }
    }
}
