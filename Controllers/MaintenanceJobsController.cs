using ClosedXML.Excel;
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
                .Select(x => x.Property != null && x.Property.OwnershipType == "Managed" && x.ServiceCatalogItemId == null ? "the owner" : "the company").FirstOrDefaultAsync();
            ViewBag.SeesAll = await SeesAllAsync();
            ViewBag.Technicians = await MaintenanceService.TechniciansAsync(_db, companyId);
            return View(model);
        }

        /// <summary>Asks again for the English translation of what the tenant wrote.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Translate(int id, [FromServices] RequestTranslationService translator)
        {
            // The job must be one of this company's, and one this user may open.
            if (!await MayOpenAsync(id) || !await _db.TenantServiceRequests.AnyAsync(x => x.Id == id))
                return NotFound();

            var (ok, message) = await translator.TranslateRequestAsync(id, again: true);
            TempData[ok ? "JobSuccess" : "JobError"] = message;
            return RedirectToAction(nameof(Details), new { id });
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

        /// <summary>
        /// The same period and technician as the dashboard, as an Excel workbook: the
        /// figures on one sheet, then every job, the cost by property and by unit, the
        /// technicians, the kinds of job and every material line - so the numbers behind
        /// the charts can be filtered, pivoted and filed.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> DashboardExcel(DateTime? fromDate, DateTime? toDate, int? technicianId)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var to = (toDate ?? DateTime.Today).Date;
            var from = (fromDate ?? to.AddDays(-89)).Date;
            if (from > to)
                (from, to) = (to, from);

            var d = await _maintenance.GetDashboardAsync(companyId, from, to, technicianId);
            var materials = await _maintenance.GetMaterialLinesAsync(d.Jobs.Select(x => x.Id).ToList());
            var company = User.FindFirst(AppClaimTypes.CurrentCompanyName)?.Value ?? string.Empty;
            var technician = technicianId.HasValue
                ? (await MaintenanceService.TechniciansAsync(_db, companyId)).FirstOrDefault(x => x.Id == technicianId.Value)?.Name
                : null;

            using var book = new XLWorkbook();

            // A header row, then the rows; numbers stay numbers so they can be summed.
            IXLWorksheet Sheet(string name, string[] headers, IEnumerable<object?[]> rows, params int[] moneyColumns)
            {
                var sheet = book.Worksheets.Add(name);
                for (var c = 0; c < headers.Length; c++)
                    sheet.Cell(1, c + 1).Value = headers[c];
                var header = sheet.Range(1, 1, 1, headers.Length);
                header.Style.Font.Bold = true;
                header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1f2937");
                header.Style.Font.FontColor = XLColor.White;

                var r = 2;
                foreach (var row in rows)
                {
                    for (var c = 0; c < row.Length; c++)
                    {
                        var cell = sheet.Cell(r, c + 1);
                        switch (row[c])
                        {
                            case null: break;
                            case DateTime date: cell.Value = date; cell.Style.DateFormat.Format = date.TimeOfDay == TimeSpan.Zero ? "dd/mm/yyyy" : "dd/mm/yyyy hh:mm"; break;
                            case decimal money: cell.Value = money; break;
                            case double number: cell.Value = Math.Round(number, 1); break;
                            case int count: cell.Value = count; break;
                            // Text from users never starts a formula.
                            default: cell.SetValue(row[c]!.ToString()); break;
                        }
                    }
                    r++;
                }

                foreach (var c in moneyColumns)
                    sheet.Column(c).Style.NumberFormat.Format = "#,##0.00";
                sheet.SheetView.FreezeRows(1);
                if (r > 2)
                    sheet.Range(1, 1, r - 1, headers.Length).SetAutoFilter();
                sheet.Columns().AdjustToContents(1, Math.Min(r, 200));
                foreach (var column in sheet.ColumnsUsed().Where(x => x.Width > 60))
                    column.Width = 60;
                return sheet;
            }

            static object? Local(DateTime? utc) => utc.HasValue ? utc.Value.ToLocalTime() : null;

            // ---- Summary ----------------------------------------------------------
            var summary = book.Worksheets.Add("Summary");
            summary.Cell(1, 1).Value = "Maintenance report";
            summary.Cell(1, 1).Style.Font.Bold = true;
            summary.Cell(1, 1).Style.Font.FontSize = 16;
            summary.Cell(2, 1).SetValue(company);
            summary.Cell(3, 1).SetValue($"Jobs raised from {from:dd/MM/yyyy} to {to:dd/MM/yyyy}, and every job still open" +
                                         (technician != null ? $" - technician: {technician}" : string.Empty));
            summary.Cell(4, 1).SetValue($"Prepared {DateTime.Now:dd/MM/yyyy HH:mm}");
            summary.Range(2, 1, 4, 1).Style.Font.FontColor = XLColor.FromHtml("#6b7280");

            var line = 6;
            void Section(string title)
            {
                line++;
                summary.Cell(line, 1).Value = title;
                summary.Range(line, 1, line, 3).Style.Font.Bold = true;
                summary.Range(line, 1, line, 3).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                line++;
            }
            void Figure(string label, object? value, string? note = null, bool money = false)
            {
                summary.Cell(line, 1).Value = label;
                var cell = summary.Cell(line, 2);
                switch (value)
                {
                    case null: cell.Value = "-"; break;
                    case decimal m: cell.Value = m; break;
                    case double n: cell.Value = Math.Round(n, 1); break;
                    case int i: cell.Value = i; break;
                    default: cell.SetValue(value.ToString()); break;
                }
                if (money)
                    cell.Style.NumberFormat.Format = "#,##0.00";
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                if (note != null)
                {
                    summary.Cell(line, 3).Value = note;
                    summary.Cell(line, 3).Style.Font.FontColor = XLColor.FromHtml("#6b7280");
                }
                line++;
            }

            Section("Jobs");
            Figure("Jobs received", d.Received);
            Figure("Done", d.Done);
            Figure("Still open", d.OpenCount);
            Figure("Open and past target", d.OverdueOpen);
            Figure("Open and unassigned", d.Unassigned);
            Figure("Declined", d.Rejected);

            Section("Service levels");
            Figure("Average first response (hours)", d.AvgResponseHours, "from request to the engineer's response; planned jobs left out");
            Figure("Average time to complete (hours)", d.AvgResolveHours);
            Figure("Done within target (%)", d.OnTimePercent);
            Figure("Team rating", d.AvgRating, $"out of {ServiceRequestRatings.Highest}; {d.RatedCount} job(s) rated");
            Figure("Low ratings", d.LowRatings, $"rated {ServiceRequestRatings.LowAtOrBelow} or below");

            Section("Planned and management work");
            Figure("Planned maintenance jobs", d.PlannedJobs, $"{d.ActivePlans} active plan(s)");
            Figure("Planned work done by its due date (%)", d.PlannedOnTimePercent);
            Figure("Planned work open and past its due date", d.PlannedOverdue);
            Figure("Raised by management", d.ManagementJobs);
            Figure("Vacant units to make ready", d.AwaitingMakeReady.Count);

            Section("Cost of materials");
            Figure("Materials used on these jobs", d.MaterialsCost, null, true);
            Figure("  taken from the maintenance store", d.MaterialsFromStore, null, true);
            Figure("  bought for the job", d.MaterialsPurchased, null, true);
            Figure("  borne by the company", d.MaterialsOnCompany, "owned properties", true);
            Figure("  charged to owners", d.MaterialsChargedToOwners, "managed properties", true);
            Figure("Materials per completed job", d.MaterialsPerDoneJob, null, true);
            Figure("Maintenance store balance", d.StockBalance, $"account {MaintenanceStandards.StockAccountCode}", true);
            summary.Column(1).Width = 44;
            summary.Column(2).Width = 16;
            summary.Column(3).Width = 60;

            // ---- Detail sheets ----------------------------------------------------
            Sheet("Jobs",
                new[] { "Job", "Raised", "Property", "Unit", "Tenant", "What", "Kind", "Priority", "Reached us by", "Purpose", "Status", "Technician",
                        "First response (h)", "Completed", "Time to complete (h)", "Past target", "Due date", "Materials", "Charged to owner", "Rating", "Feedback" },
                d.Jobs.OrderBy(x => x.SubmittedAt).Select(x => new object?[]
                {
                    x.Number, Local(x.SubmittedAt), x.Property, x.Unit, x.Tenant, x.Subject,
                    string.IsNullOrWhiteSpace(x.Category) ? "General" : x.Category, x.Priority, ServiceRequestSources.Describe(x.Source),
                    string.IsNullOrWhiteSpace(x.Purpose) ? null : MaintenancePurposes.Describe(x.Purpose), ServiceRequestStatuses.Describe(x.Status),
                    x.Technician ?? "not assigned", x.ResponseHours, Local(x.ResolvedAt), x.ResolveHours, x.ResolveLate ? "Yes" : "No",
                    x.DueDate, x.Materials, x.MaterialsChargedToOwner, x.Rating, x.RatingFeedback
                }), 18, 19);

            Sheet("Cost by property",
                new[] { "Property", "Jobs", "Open", "Done", "Materials", "Borne by the company", "Charged to the owner", "Materials per job" },
                d.CostByProperty.Select(x => new object?[]
                    { x.Name, x.Jobs, x.Open, x.Done, x.Materials, x.OnCompany, x.ChargedToOwner, x.Jobs == 0 ? 0m : Math.Round(x.Materials / x.Jobs, 2) }),
                5, 6, 7, 8);

            Sheet("Cost by unit",
                new[] { "Property", "Unit", "Jobs", "Open", "Done", "Materials", "Charged to the owner", "Materials per job" },
                d.CostByUnit.Select(x => new object?[]
                    { x.Property, x.Unit, x.Jobs, x.Open, x.Done, x.Materials, x.ChargedToOwner, x.Jobs == 0 ? 0m : Math.Round(x.Materials / x.Jobs, 2) }),
                6, 7, 8);

            Sheet("By technician",
                new[] { "Technician", "Jobs", "Open", "Done", "Average to complete (h)", "Past target", "Rating", "Rated jobs" },
                d.ByTechnician.Select(x => new object?[] { x.Name, x.Jobs, x.Open, x.Done, x.AvgResolveHours, x.Late, x.AvgRating, x.Rated }));

            Sheet("By kind of job",
                new[] { "Kind", "Jobs", "Average to complete (h)" },
                d.ByCategory.Select(x => new object?[] { x.Name, x.Jobs, x.AvgResolveHours }));

            Sheet("By month",
                new[] { "Month", "Jobs raised", "Jobs completed", "Materials on jobs raised" },
                d.ByMonth.Select(x => new object?[] { x.Month.ToString("MMM yyyy"), x.Raised, x.Completed, x.Materials }), 4);

            Sheet("Materials",
                new[] { "Date", "Job", "Property", "Unit", "Material", "Quantity", "Unit cost", "Amount", "From", "Reference", "Charged to" },
                materials.Select(x => new object?[]
                {
                    x.Date, x.Job, x.Property, x.Unit, x.Description, x.Quantity, x.UnitCost, x.Amount,
                    MaintenanceMaterialSources.Describe(x.Source), x.Reference,
                    x.ChargedTo == "Owner" ? "The owner" : "The company"
                }), 6, 7, 8);

            using var stream = new MemoryStream();
            book.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"maintenance-report-{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx");
        }

        private async Task<bool> SeesAllAsync() =>
            await _permissions.HasAnyPermissionAsync(User, AppPermissions.MaintenanceManage, AppPermissions.ServiceRequestsManage, AppPermissions.OwnerOversight);

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
