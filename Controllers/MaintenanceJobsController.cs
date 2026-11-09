using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The maintenance team's screen. A technician (Maintenance.Execute) sees only the
    /// maintenance jobs assigned to them; whoever handles tenant requests sees them all.
    /// The dashboard is the owner's view of the whole maintenance operation.
    /// </summary>
    public class MaintenanceJobsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ServiceRequestService _requests;
        private readonly MaintenanceService _maintenance;
        private readonly PermissionService _permissions;

        public MaintenanceJobsController(
            ApplicationDbContext db, ServiceRequestService requests, MaintenanceService maintenance, PermissionService permissions)
        {
            _db = db;
            _requests = requests;
            _maintenance = maintenance;
            _permissions = permissions;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? show, int? technicianId)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var seesAll = await SeesAllAsync();
            show ??= "Open";

            var query = _db.TenantServiceRequests.AsNoTracking()
                .Where(x => x.RequestType == ServiceRequestTypes.Maintenance && x.Status != ServiceRequestStatuses.Cancelled);
            if (!seesAll)
                query = query.Where(x => x.AssignedToAppUserId == User.GetAppUserId());
            else if (technicianId.HasValue)
                query = query.Where(x => x.AssignedToAppUserId == technicianId.Value);

            query = show switch
            {
                "Done" => query.Where(x => x.Status == ServiceRequestStatuses.Resolved || x.Status == ServiceRequestStatuses.Closed || x.Status == ServiceRequestStatuses.Rejected),
                "All" => query,
                _ => query.Where(x => x.Status == ServiceRequestStatuses.Submitted || x.Status == ServiceRequestStatuses.Acknowledged || x.Status == ServiceRequestStatuses.InProgress)
            };

            var jobs = await query
                .OrderByDescending(x => x.Priority == ServiceRequestPriorities.Urgent).ThenByDescending(x => x.Priority == ServiceRequestPriorities.High)
                .ThenBy(x => x.SubmittedAt)
                .Take(300)
                .Select(x => new MaintenanceService.JobRow
                {
                    Id = x.Id, Number = x.RequestNumber, Subject = x.Subject, Category = x.Category, Priority = x.Priority, Status = x.Status,
                    Tenant = x.Tenant != null ? x.Tenant.FullName : null,
                    Purpose = x.Purpose,
                    DueDate = x.PreferredVisitDate,
                    Property = x.Property != null ? x.Property.Code + " " + x.Property.Name : null,
                    Place = x.Unit != null ? x.Unit.UnitNumber : (x.Property != null ? x.Property.Code : "-"),
                    Technician = x.AssignedToAppUserId == null ? null : _db.AppUsers.Where(u => u.Id == x.AssignedToAppUserId).Select(u => u.DisplayName).FirstOrDefault(),
                    SubmittedAt = x.SubmittedAt, AcknowledgedAt = x.AcknowledgedAt, WorkStartedAt = x.WorkStartedAt, ResolvedAt = x.ResolvedAt,
                    Materials = _db.MaintenanceMaterials.Where(m => m.TenantServiceRequestId == x.Id).Sum(m => (decimal?)m.Amount) ?? 0m,
                    Source = x.Source, Rating = x.Rating
                })
                .ToListAsync();

            ViewBag.Show = show;
            ViewBag.SeesAll = seesAll;
            ViewBag.TechnicianId = technicianId;
            ViewBag.Technicians = await MaintenanceService.TechniciansAsync(_db, companyId);
            if (seesAll && show == "Open")
                ViewBag.AwaitingMakeReady = await _maintenance.VacantUnitsAwaitingMakeReadyAsync(companyId);
            return View(jobs);
        }

        // =====================================================================
        //  JOBS NO TENANT ASKED FOR
        // =====================================================================

        /// <summary>
        /// A job management raises itself: a vacant unit to make ready for the next tenant,
        /// an inspection, a repair spotted on a visit. A unit and purpose can be passed in, as
        /// the make-ready list does.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Create(int? unitId, string? purpose)
        {
            var model = new ServiceRequestService.InternalJob
            {
                Purpose = MaintenancePurposes.All.Contains(purpose) ? purpose! : MaintenancePurposes.Repair
            };

            if (unitId.HasValue)
            {
                var unit = await _db.Units.AsNoTracking().Include(x => x.Property).FirstOrDefaultAsync(x => x.Id == unitId.Value);
                if (unit != null)
                {
                    model.PropertyId = unit.PropertyId;
                    model.UnitId = unit.Id;
                    if (model.Purpose == MaintenancePurposes.MakeReady)
                    {
                        model.Subject = $"Make unit {unit.UnitNumber} ready for the next tenant";
                        model.Description = "Inspect the unit after the tenancy ended, repair what is needed, deep clean and check every fitting, so it can be let again.";
                    }
                }
            }

            await LoadCreateListsAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireFormFields("PropertyId", "Subject", "Purpose")]
        public async Task<IActionResult> Create(ServiceRequestService.InternalJob model)
        {
            if (!ModelState.IsValid)
            {
                await LoadCreateListsAsync();
                return View(model);
            }

            model.Source = ServiceRequestSources.Management;
            model.MaintenancePlanId = null;
            model.RaisedByAppUserId = User.GetAppUserId();
            model.RaisedByName = DisplayName();

            var (job, error) = await _requests.CreateMaintenanceJobAsync(User.GetRequiredCurrentCompanyId(), model);
            if (job == null)
            {
                ModelState.AddModelError(string.Empty, error!);
                await LoadCreateListsAsync();
                return View(model);
            }

            TempData["JobSuccess"] = $"Job {job.RequestNumber} raised.";
            return RedirectToAction(nameof(Details), new { id = job.Id });
        }

        private async Task LoadCreateListsAsync()
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            ViewBag.Properties = await _db.Properties.AsNoTracking()
                .Where(x => x.IsActive).OrderBy(x => x.Code)
                .Select(x => new { x.Id, Name = x.Code + " " + x.Name })
                .ToListAsync();
            ViewBag.Units = await _db.Units.AsNoTracking()
                .Where(x => x.Property!.IsActive).OrderBy(x => x.UnitNumber)
                .Select(x => new { x.Id, x.PropertyId, x.UnitNumber, x.Status })
                .ToListAsync();
            ViewBag.Technicians = await MaintenanceService.TechniciansAsync(_db, companyId);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            if (!await MayOpenAsync(id))
                return NotFound();

            var model = await _requests.GetStaffRequestAsync(companyId, id);
            if (model == null || model.RequestType != ServiceRequestTypes.Maintenance)
                return NotFound();

            ViewBag.WorkStartedAt = await _db.TenantServiceRequests.Where(x => x.Id == id).Select(x => x.WorkStartedAt).FirstOrDefaultAsync();
            ViewBag.Materials = await _db.MaintenanceMaterials.AsNoTracking()
                .Where(x => x.TenantServiceRequestId == id)
                .OrderBy(x => x.MaterialDate).ThenBy(x => x.Id)
                .ToListAsync();
            ViewBag.ChargedTo = await _db.TenantServiceRequests.Where(x => x.Id == id)
                .Select(x => x.Property != null && x.Property.OwnershipType == "Managed" ? "the owner" : "the company").FirstOrDefaultAsync();
            ViewBag.SeesAll = await SeesAllAsync();
            ViewBag.Technicians = await MaintenanceService.TechniciansAsync(_db, companyId);
            return View(model);
        }

        /// <summary>The technician's steps: acknowledge, start, resolve.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step(int id, string step, string? notes, int? assignTo)
        {
            if (!await MayOpenAsync(id))
                return NotFound();

            var status = step switch
            {
                "acknowledge" => ServiceRequestStatuses.Acknowledged,
                "start" => ServiceRequestStatuses.InProgress,
                "resolve" => ServiceRequestStatuses.Resolved,
                "reopen" => ServiceRequestStatuses.InProgress,
                "close" => ServiceRequestStatuses.Closed,
                _ => null
            };

            // Only a supervisor reassigns or closes a job.
            var seesAll = await SeesAllAsync();
            if (status == null || (status == ServiceRequestStatuses.Closed && !seesAll))
                return BadRequest();

            var (success, message) = await _requests.UpdateStatusAsync(
                User.GetRequiredCurrentCompanyId(), id, status, notes, seesAll ? assignTo : null, null, User.GetAppUserId(), DisplayName());
            TempData[success ? "JobSuccess" : "JobError"] = message;
            return RedirectToAction(nameof(Details), new { id });
        }

        /// <summary>A supervisor gives the job to another technician; the status is left as it is.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Assign(int id, int assignTo)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            if (!await SeesAllAsync())
                return NotFound();

            var technician = (await MaintenanceService.TechniciansAsync(_db, companyId)).FirstOrDefault(x => x.Id == assignTo);
            var job = await _db.TenantServiceRequests.FirstOrDefaultAsync(x => x.Id == id && x.RequestType == ServiceRequestTypes.Maintenance);
            if (job == null || technician == null)
                return NotFound();

            // A tenant's score stays with the person it was given to.
            if (job.Rating.HasValue && job.AssignedToAppUserId.HasValue && job.AssignedToAppUserId != technician.Id)
            {
                TempData["JobError"] = "This job has been rated, so it cannot move to someone else.";
                return RedirectToAction(nameof(Details), new { id });
            }

            job.AssignedToAppUserId = technician.Id;
            _db.TenantServiceRequestComments.Add(new TenantServiceRequestComment
            {
                TenantServiceRequestId = job.Id,
                AuthorType = ServiceRequestAuthorTypes.Staff,
                AuthorName = DisplayName() ?? "Staff",
                AppUserId = User.GetAppUserId(),
                Body = $"Assigned to {technician.Name}.",
                IsInternal = true,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            if (technician.Id != User.GetAppUserId())
                _requests.NotifyTechnician(job, technician.Id);

            TempData["JobSuccess"] = $"Job {job.RequestNumber} assigned to {technician.Name}.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Note(int id, string body, bool isInternal)
        {
            if (!await MayOpenAsync(id))
                return NotFound();

            var added = await _requests.AddStaffCommentAsync(User.GetRequiredCurrentCompanyId(), id, User.GetAppUserId(), DisplayName(), body, isInternal);
            TempData[added ? "JobSuccess" : "JobError"] = added
                ? (isInternal ? "Internal note added." : "Message sent to the tenant.")
                : "The note could not be added.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddMaterial(MaintenanceService.MaterialEntry entry)
        {
            if (!await MayOpenAsync(entry.JobId))
                return NotFound();

            var (ok, message) = await _maintenance.AddMaterialAsync(User.GetRequiredCurrentCompanyId(), entry, DisplayName());
            TempData[ok ? "JobSuccess" : "JobError"] = message;
            return RedirectToAction(nameof(Details), new { id = entry.JobId });
        }

        /// <summary>
        /// A supervisor records the rating a tenant gave by WhatsApp or phone. A technician
        /// cannot: the score measures their own work.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordRating(int id, int rating, string? feedback)
        {
            if (!await SeesAllAsync())
                return NotFound();

            var (ok, message) = await _requests.RecordRatingForTenantAsync(
                User.GetRequiredCurrentCompanyId(), id, rating, feedback, User.GetAppUserId(), DisplayName());
            TempData[ok ? "JobSuccess" : "JobError"] = ok ? "The tenant's rating has been recorded." : message;
            return RedirectToAction(nameof(Details), new { id });
        }

        /// <summary>The owner's maintenance dashboard: status, timings and costs.</summary>
        [HttpGet]
        public async Task<IActionResult> Dashboard(DateTime? fromDate, DateTime? toDate, int? technicianId)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var to = (toDate ?? DateTime.Today).Date;
            var from = (fromDate ?? to.AddDays(-89)).Date;
            if (from > to)
                (from, to) = (to, from);

            ViewBag.TechnicianId = technicianId;
            ViewBag.Technicians = await MaintenanceService.TechniciansAsync(_db, companyId);
            return View(await _maintenance.GetDashboardAsync(companyId, from, to, technicianId));
        }

        private async Task<bool> SeesAllAsync() =>
            await _permissions.HasAnyPermissionAsync(User, AppPermissions.ServiceRequestsManage, AppPermissions.OwnerOversight);

        /// <summary>A technician opens only the jobs assigned to them.</summary>
        private async Task<bool> MayOpenAsync(int id)
        {
            if (await SeesAllAsync())
                return true;
            var userId = User.GetAppUserId();
            return await _db.TenantServiceRequests.AnyAsync(x => x.Id == id && x.AssignedToAppUserId == userId);
        }

        private string? DisplayName() =>
            User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.Identity?.Name;
    }
}
