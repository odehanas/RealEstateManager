using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Maintenance jobs (tenant maintenance requests) as the maintenance team works
    /// them: who does each job, the materials it used, and the figures the owner
    /// watches on the maintenance dashboard.
    /// </summary>
    public class MaintenanceService
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _posting;
        private readonly JournalWorkflowService _workflow;

        public MaintenanceService(ApplicationDbContext db, AccountingPostingService posting, JournalWorkflowService workflow)
        {
            _db = db;
            _posting = posting;
            _workflow = workflow;
        }

        // =====================================================================
        //  WHO DOES THE JOB
        // =====================================================================

        public sealed record Technician(int Id, string Name);

        /// <summary>Active users of the company who carry out maintenance jobs.</summary>
        public static async Task<List<Technician>> TechniciansAsync(ApplicationDbContext db, int companyId)
        {
            var rows = await db.AppUserCompanies.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.AppUser!.IsActive &&
                            x.AppUser.Permissions.Any(p => p.PermissionKey == AppPermissions.MaintenanceExecute))
                .Select(x => new { x.AppUserId, x.AppUser!.DisplayName })
                .ToListAsync();

            return rows.DistinctBy(x => x.AppUserId)
                .Select(x => new Technician(x.AppUserId, x.DisplayName))
                .OrderBy(x => x.Name)
                .ToList();
        }

        /// <summary>
        /// The technician a new maintenance job goes to: the one with the fewest open
        /// jobs. Null when the company has no maintenance user yet; the job then waits in
        /// Tenant Requests for someone to assign it.
        /// </summary>
        public static async Task<int?> PickTechnicianAsync(ApplicationDbContext db, int companyId)
        {
            var technicians = await TechniciansAsync(db, companyId);
            if (technicians.Count == 0)
                return null;

            var ids = technicians.Select(t => t.Id).ToList();
            var open = await db.TenantServiceRequests.AsNoTracking().IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.AssignedToAppUserId != null && ids.Contains(x.AssignedToAppUserId.Value) &&
                            x.Status != ServiceRequestStatuses.Resolved && x.Status != ServiceRequestStatuses.Closed &&
                            x.Status != ServiceRequestStatuses.Rejected && x.Status != ServiceRequestStatuses.Cancelled)
                .GroupBy(x => x.AssignedToAppUserId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count);

            return technicians.OrderBy(t => open.TryGetValue(t.Id, out var n) ? n : 0).ThenBy(t => t.Name).First().Id;
        }

        // =====================================================================
        //  MATERIALS
        // =====================================================================

        public sealed class MaterialEntry
        {
            public int JobId { get; set; }
            public DateTime? Date { get; set; }
            public string? Description { get; set; }
            public decimal Quantity { get; set; } = 1m;
            public decimal UnitCost { get; set; }
            public string Source { get; set; } = MaintenanceMaterialSources.Stock;
            public string? Reference { get; set; }
        }

        /// <summary>
        /// Records material used on a job. A stock item is posted out of the maintenance
        /// store at once: to the owner's account (Due to Landlords) for a managed property,
        /// or to Property Maintenance for the company's own. A purchased item is recorded
        /// against the job; its cost is booked by the bill or voucher that paid for it.
        /// </summary>
        public async Task<(bool Ok, string Message)> AddMaterialAsync(int companyId, MaterialEntry entry, string? userName)
        {
            var job = await _db.TenantServiceRequests.Include(x => x.Property).Include(x => x.Unit)
                .FirstOrDefaultAsync(x => x.Id == entry.JobId && x.CompanyId == companyId);
            if (job == null) return (false, "The job was not found.");
            if (job.RequestType != ServiceRequestTypes.Maintenance) return (false, "Materials are recorded on maintenance jobs only.");
            if (string.IsNullOrWhiteSpace(entry.Description)) return (false, "Say what the material is.");
            if (entry.Quantity <= 0m || entry.UnitCost < 0m) return (false, "Quantity must be above zero and the cost cannot be negative.");
            if (!MaintenanceMaterialSources.All.Contains(entry.Source)) return (false, "Choose where the material came from.");
            if (entry.Source != MaintenanceMaterialSources.Stock && string.IsNullOrWhiteSpace(entry.Reference))
                return (false, entry.Source == MaintenanceMaterialSources.Subcontractor
                    ? "Give the number of the subcontractor's bill."
                    : "Give the bill or receipt number the item was bought on.");

            var amount = Math.Round(entry.Quantity * entry.UnitCost, 2);
            if (entry.Source == MaintenanceMaterialSources.Stock && amount <= 0m)
                return (false, "Give the cost of the item taken from the store.");

            var material = new MaintenanceMaterial
            {
                CompanyId = companyId,
                TenantServiceRequestId = job.Id,
                MaterialDate = (entry.Date ?? DateTime.Today).Date,
                Description = entry.Description.Trim(),
                Quantity = entry.Quantity,
                UnitCost = entry.UnitCost,
                Amount = amount,
                Source = entry.Source,
                Reference = string.IsNullOrWhiteSpace(entry.Reference) ? null : entry.Reference.Trim(),
                // A service the tenant pays the company for is the company's cost to bear,
                // whoever owns the building: the owner neither earns from it nor pays for it.
                ChargedTo = job.Property?.OwnershipType == "Managed" && !job.ServiceCatalogItemId.HasValue ? "Owner" : "Company",
                CreatedAt = DateTime.UtcNow,
                CreatedByName = userName
            };
            _db.MaintenanceMaterials.Add(material);
            await _db.SaveChangesAsync();

            if (material.Source != MaintenanceMaterialSources.Stock)
                return (true, $"{material.Description} recorded on job {job.RequestNumber}; its cost is booked by {material.Reference}.");

            try
            {
                var journal = await _posting.PostMaintenanceMaterialAsync(material.Id);
                var notPosted = await _workflow.PostNewEntryAsync(journal.Id);
                return (true, $"{material.Description} ({amount:N2}) taken from the maintenance store for job {job.RequestNumber} and charged to " +
                              (material.ChargedTo == "Owner" ? "the owner" : "the company") + "." +
                              (notPosted == null ? string.Empty : $" The entry is a draft: {notPosted}"));
            }
            catch (Exception ex)
            {
                // Without its entry the stock item would cost nothing; take it off again.
                _db.ChangeTracker.Clear();
                var orphan = await _db.MaintenanceMaterials.FirstOrDefaultAsync(x => x.Id == material.Id);
                if (orphan != null)
                {
                    _db.MaintenanceMaterials.Remove(orphan);
                    await _db.SaveChangesAsync();
                }

                return (false, $"The material could not be posted, so it was not recorded. {ex.Message}");
            }
        }

        /// <summary>The balance of the maintenance store account (posted entries).</summary>
        public async Task<decimal> StockBalanceAsync()
        {
            var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Code == MaintenanceStandards.StockAccountCode);
            if (account == null) return 0m;
            var movements = await _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.AccountId == account.Id && x.JournalEntry!.IsPosted)
                .SumAsync(x => (decimal?)(x.Debit - x.Credit)) ?? 0m;
            return account.OpeningBalance + movements;
        }

        // =====================================================================
        //  DASHBOARD
        // =====================================================================

        public sealed class JobRow
        {
            public int Id { get; set; }
            public string Number { get; set; } = string.Empty;
            public string Subject { get; set; } = string.Empty;
            public string? Category { get; set; }
            public string Priority { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            /// <summary>Empty on a job no tenant asked for.</summary>
            public string? Tenant { get; set; }
            public string? Purpose { get; set; }

            /// <summary>When the work should be done: a planned job's due date.</summary>
            public DateTime? DueDate { get; set; }
            public string Place { get; set; } = string.Empty;
            public string? Property { get; set; }
            public string? Technician { get; set; }
            public DateTime SubmittedAt { get; set; }
            public DateTime? AcknowledgedAt { get; set; }
            public DateTime? WorkStartedAt { get; set; }
            public DateTime? ResolvedAt { get; set; }
            public decimal Materials { get; set; }

            /// <summary>The part of the materials taken from the maintenance store; the rest was bought for the job.</summary>
            public decimal MaterialsFromStore { get; set; }

            /// <summary>The part of the materials charged on to the owner of a managed property.</summary>
            public decimal MaterialsChargedToOwner { get; set; }
            public int? PropertyId { get; set; }
            public int? UnitId { get; set; }
            public string? Unit { get; set; }
            public string Source { get; set; } = ServiceRequestSources.Portal;
            public int? Rating { get; set; }
            public string? RatingFeedback { get; set; }
            public DateTime? RatedAt { get; set; }

            public double? ResponseHours => AcknowledgedAt.HasValue ? (AcknowledgedAt.Value - SubmittedAt).TotalHours : null;
            public double? ResolveHours => ResolvedAt.HasValue ? (ResolvedAt.Value - SubmittedAt).TotalHours : null;
            public double AgeHours => ((ResolvedAt ?? DateTime.UtcNow) - SubmittedAt).TotalHours;
            public bool IsOpen => Status is ServiceRequestStatuses.Submitted or ServiceRequestStatuses.Acknowledged or ServiceRequestStatuses.InProgress;
            public bool IsDone => Status is ServiceRequestStatuses.Resolved or ServiceRequestStatuses.Closed;

            public bool IsPlanned => Source == ServiceRequestSources.Planned;
            public bool IsInternal => ServiceRequestSources.IsInternal(Source);

            // A planned job is opened days before it is due, so it is measured against its due
            // date, not against the response times for a tenant waiting on a repair.
            public bool ResponseLate => !IsPlanned &&
                (ResponseHours ?? (IsOpen ? AgeHours : 0)) > MaintenanceStandards.TargetFor(Priority).ResponseHours;
            public bool ResolveLate => IsPlanned
                ? DueDate.HasValue && (ResolvedAt ?? DateTime.UtcNow) > DueDate.Value.Date.AddDays(1).ToUniversalTime()
                : (ResolveHours ?? (IsOpen ? AgeHours : 0)) > MaintenanceStandards.TargetFor(Priority).ResolveHours;
        }

        public sealed class Dashboard
        {
            public DateTime From { get; set; }
            public DateTime To { get; set; }
            public List<JobRow> Jobs { get; set; } = new();
            public decimal StockBalance { get; set; }
            public bool HasTechnicians { get; set; }

            public IEnumerable<JobRow> Open => Jobs.Where(x => x.IsOpen).OrderBy(x => ServiceRequestPriorities.Weight(x.Priority)).ThenBy(x => x.SubmittedAt);
            public int Received => Jobs.Count;
            public int Done => Jobs.Count(x => x.IsDone);
            public int OpenCount => Jobs.Count(x => x.IsOpen);
            public int Rejected => Jobs.Count(x => x.Status == ServiceRequestStatuses.Rejected);
            public int Unassigned => Jobs.Count(x => x.IsOpen && x.Technician == null);
            public double? AvgResponseHours => Average(Jobs.Where(x => !x.IsPlanned).Select(x => x.ResponseHours));
            public double? AvgResolveHours => Average(Jobs.Where(x => x.IsDone).Select(x => x.ResolveHours));
            public int OnTime => Jobs.Count(x => x.IsDone && !x.ResolveLate);
            public double? OnTimePercent => Done == 0 ? null : 100.0 * OnTime / Done;
            public int OverdueOpen => Jobs.Count(x => x.IsOpen && x.ResolveLate);
            public decimal MaterialsCost => Jobs.Sum(x => x.Materials);

            // ---- Work no tenant asked for --------------------------------------
            public int ActivePlans { get; set; }
            public int PlannedJobs => Jobs.Count(x => x.IsPlanned);
            public int PlannedDone => Jobs.Count(x => x.IsPlanned && x.IsDone);
            public int PlannedOverdue => Jobs.Count(x => x.IsPlanned && x.IsOpen && x.ResolveLate);
            public double? PlannedOnTimePercent => PlannedDone == 0 ? null : 100.0 * Jobs.Count(x => x.IsPlanned && x.IsDone && !x.ResolveLate) / PlannedDone;
            public int ManagementJobs => Jobs.Count(x => x.Source == ServiceRequestSources.Management);
            public List<VacantUnit> AwaitingMakeReady { get; set; } = new();

            // The tenants' ratings. The overall average is the team's score, the measure
            // of the maintenance manager; each technician's own average is below it.
            public int RatedCount => Jobs.Count(x => x.Rating.HasValue);
            public double? AvgRating => AverageRating(Jobs);
            public int LowRatings => Jobs.Count(x => x.Rating.HasValue && ServiceRequestRatings.IsLow(x.Rating.Value));
            public double? RatedPercent => Done == 0 ? null : 100.0 * Jobs.Count(x => x.IsDone && x.Rating.HasValue) / Done;
            public IEnumerable<JobRow> RatedJobs => Jobs.Where(x => x.Rating.HasValue).OrderBy(x => x.Rating).ThenByDescending(x => x.RatedAt);

            public IEnumerable<(string Name, int Jobs, int Open, int Done, double? AvgResolveHours, int Late, double? AvgRating, int Rated)> ByTechnician =>
                Jobs.GroupBy(x => x.Technician ?? "(not assigned)")
                    .Select(g => (g.Key, g.Count(), g.Count(x => x.IsOpen), g.Count(x => x.IsDone),
                                  Average(g.Where(x => x.IsDone).Select(x => x.ResolveHours)), g.Count(x => x.ResolveLate),
                                  AverageRating(g), g.Count(x => x.Rating.HasValue)))
                    .OrderByDescending(x => x.Item2);

            public IEnumerable<(string Source, int Jobs, int Open)> BySource =>
                Jobs.GroupBy(x => x.Source)
                    .Select(g => (g.Key, g.Count(), g.Count(x => x.IsOpen)))
                    .OrderByDescending(x => x.Item2);

            public IEnumerable<(string Name, int Jobs, int Open, decimal Materials)> ByProperty =>
                Jobs.GroupBy(x => x.Property ?? "(no property)")
                    .Select(g => (g.Key, g.Count(), g.Count(x => x.IsOpen), g.Sum(x => x.Materials)))
                    .OrderByDescending(x => x.Item2);

            public IEnumerable<(string Name, int Jobs, double? AvgResolveHours)> ByCategory =>
                Jobs.GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "General" : x.Category!)
                    .Select(g => (g.Key, g.Count(), Average(g.Where(x => x.IsDone).Select(x => x.ResolveHours))))
                    .OrderByDescending(x => x.Item2);

            // ---- For the charts and the cost report ------------------------------

            /// <summary>Materials recorded on the jobs, per completed job.</summary>
            public decimal? MaterialsPerDoneJob => Done == 0 ? null : Math.Round(MaterialsCost / Done, 2);
            public decimal MaterialsFromStore => Jobs.Sum(x => x.MaterialsFromStore);
            public decimal MaterialsPurchased => MaterialsCost - MaterialsFromStore;
            public decimal MaterialsChargedToOwners => Jobs.Sum(x => x.MaterialsChargedToOwner);
            public decimal MaterialsOnCompany => MaterialsCost - MaterialsChargedToOwners;

            /// <summary>
            /// Jobs raised and jobs completed in each month of the period. A job still open
            /// from before the period is in the list but was not raised in it, so it is
            /// counted only if it was completed in the period.
            /// </summary>
            public List<(DateTime Month, int Raised, int Completed, decimal Materials)> ByMonth
            {
                get
                {
                    var result = new List<(DateTime, int, int, decimal)>();
                    var last = new DateTime(To.Year, To.Month, 1);
                    var month = new DateTime(From.Year, From.Month, 1);
                    // A very long period would make an unreadable chart: the last three years at most.
                    if (month < last.AddMonths(-35))
                        month = last.AddMonths(-35);

                    for (; month <= last; month = month.AddMonths(1))
                    {
                        var next = month.AddMonths(1);
                        var raised = Jobs.Where(x => x.SubmittedAt.ToLocalTime() >= month && x.SubmittedAt.ToLocalTime() < next).ToList();
                        result.Add((month, raised.Count,
                            Jobs.Count(x => x.IsDone && x.ResolvedAt.HasValue && x.ResolvedAt.Value.ToLocalTime() >= month && x.ResolvedAt.Value.ToLocalTime() < next),
                            raised.Sum(x => x.Materials)));
                    }

                    return result;
                }
            }

            public IEnumerable<(string Status, int Jobs)> ByStatus =>
                Jobs.GroupBy(x => x.Status)
                    .Select(g => (g.Key, g.Count()))
                    .OrderBy(x => Array.IndexOf(StatusOrder, x.Item1));

            private static readonly string[] StatusOrder =
            {
                ServiceRequestStatuses.Submitted, ServiceRequestStatuses.Acknowledged, ServiceRequestStatuses.InProgress,
                ServiceRequestStatuses.Resolved, ServiceRequestStatuses.Closed, ServiceRequestStatuses.Rejected
            };

            /// <summary>Jobs within and past their completion target, by priority. Declined jobs are left out.</summary>
            public IEnumerable<(string Priority, int WithinTarget, int PastTarget)> ByPriority =>
                Jobs.Where(x => x.IsOpen || x.IsDone)
                    .GroupBy(x => x.Priority)
                    .OrderBy(g => ServiceRequestPriorities.Weight(g.Key))
                    .Select(g => (g.Key, g.Count(x => !x.ResolveLate), g.Count(x => x.ResolveLate)));

            public IEnumerable<(string Name, int Jobs, int Open, int Done, decimal Materials, decimal OnCompany, decimal ChargedToOwner)> CostByProperty =>
                Jobs.GroupBy(x => x.Property ?? "(no property)")
                    .Select(g => (g.Key, g.Count(), g.Count(x => x.IsOpen), g.Count(x => x.IsDone), g.Sum(x => x.Materials),
                                  g.Sum(x => x.Materials - x.MaterialsChargedToOwner), g.Sum(x => x.MaterialsChargedToOwner)))
                    .OrderByDescending(x => x.Item5).ThenByDescending(x => x.Item2);

            /// <summary>Jobs and materials for each unit. Jobs on a building's common areas are grouped as such.</summary>
            public IEnumerable<(string Property, string Unit, int Jobs, int Open, int Done, decimal Materials, decimal ChargedToOwner)> CostByUnit =>
                Jobs.GroupBy(x => (Property: x.Property ?? "(no property)", Unit: x.Unit ?? "(building / common areas)"))
                    .Select(g => (g.Key.Property, g.Key.Unit, g.Count(), g.Count(x => x.IsOpen), g.Count(x => x.IsDone),
                                  g.Sum(x => x.Materials), g.Sum(x => x.MaterialsChargedToOwner)))
                    .OrderByDescending(x => x.Item6).ThenByDescending(x => x.Item3).ThenBy(x => x.Item1).ThenBy(x => x.Item2);

            private static double? AverageRating(IEnumerable<JobRow> jobs) =>
                Average(jobs.Select(x => (double?)x.Rating));

            private static double? Average(IEnumerable<double?> values)
            {
                var list = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
                return list.Count == 0 ? null : list.Average();
            }
        }

        /// <summary>
        /// Maintenance jobs raised in the period, plus every job still open whenever it
        /// was raised, with their timings and materials.
        /// </summary>
        public async Task<Dashboard> GetDashboardAsync(int companyId, DateTime from, DateTime to, int? technicianId = null)
        {
            var fromUtc = from.Date.ToUniversalTime();
            var toUtc = to.Date.AddDays(1).ToUniversalTime();

            var query = _db.TenantServiceRequests.AsNoTracking()
                .Where(x => x.RequestType == ServiceRequestTypes.Maintenance && x.Status != ServiceRequestStatuses.Cancelled)
                .Where(x => (x.SubmittedAt >= fromUtc && x.SubmittedAt < toUtc) ||
                            x.Status == ServiceRequestStatuses.Submitted || x.Status == ServiceRequestStatuses.Acknowledged ||
                            x.Status == ServiceRequestStatuses.InProgress);
            if (technicianId.HasValue)
                query = query.Where(x => x.AssignedToAppUserId == technicianId.Value);

            var jobs = await query
                .Select(x => new JobRow
                {
                    Id = x.Id,
                    Number = x.RequestNumber,
                    Subject = x.Subject,
                    Category = x.Category,
                    Priority = x.Priority,
                    Status = x.Status,
                    Tenant = x.Tenant != null ? x.Tenant.FullName : null,
                    Purpose = x.Purpose,
                    DueDate = x.PreferredVisitDate,
                    Property = x.Property != null ? x.Property.Code + " " + x.Property.Name : null,
                    Place = x.Unit != null ? x.Unit.UnitNumber : (x.Property != null ? x.Property.Code : "-"),
                    Technician = x.AssignedToAppUserId == null ? null
                        : _db.AppUsers.Where(u => u.Id == x.AssignedToAppUserId).Select(u => u.DisplayName).FirstOrDefault(),
                    SubmittedAt = x.SubmittedAt,
                    AcknowledgedAt = x.AcknowledgedAt,
                    WorkStartedAt = x.WorkStartedAt,
                    ResolvedAt = x.ResolvedAt,
                    Materials = _db.MaintenanceMaterials.Where(m => m.TenantServiceRequestId == x.Id).Sum(m => (decimal?)m.Amount) ?? 0m,
                    MaterialsFromStore = _db.MaintenanceMaterials
                        .Where(m => m.TenantServiceRequestId == x.Id && m.Source == MaintenanceMaterialSources.Stock).Sum(m => (decimal?)m.Amount) ?? 0m,
                    MaterialsChargedToOwner = _db.MaintenanceMaterials
                        .Where(m => m.TenantServiceRequestId == x.Id && m.ChargedTo == "Owner").Sum(m => (decimal?)m.Amount) ?? 0m,
                    PropertyId = x.PropertyId,
                    UnitId = x.UnitId,
                    Unit = x.Unit != null ? x.Unit.UnitNumber : null,
                    Source = x.Source,
                    Rating = x.Rating,
                    RatingFeedback = x.RatingFeedback,
                    RatedAt = x.RatedAt
                })
                .ToListAsync();

            return new Dashboard
            {
                From = from.Date,
                To = to.Date,
                Jobs = jobs,
                StockBalance = await StockBalanceAsync(),
                HasTechnicians = (await TechniciansAsync(_db, companyId)).Count > 0,
                ActivePlans = await _db.MaintenancePlans.CountAsync(x => x.CompanyId == companyId && x.IsActive),
                AwaitingMakeReady = await VacantUnitsAwaitingMakeReadyAsync(companyId)
            };
        }

        public sealed record MaterialLine(
            DateTime Date, string Job, string? Property, string? Unit, string Description,
            decimal Quantity, decimal UnitCost, decimal Amount, string Source, string? Reference, string ChargedTo);

        /// <summary>Every material recorded on the given jobs, for the cost sheet of the Excel report.</summary>
        public async Task<List<MaterialLine>> GetMaterialLinesAsync(IReadOnlyCollection<int> jobIds)
        {
            if (jobIds.Count == 0)
                return new List<MaterialLine>();

            return await _db.MaintenanceMaterials.AsNoTracking()
                .Where(m => jobIds.Contains(m.TenantServiceRequestId))
                .OrderBy(m => m.MaterialDate).ThenBy(m => m.Id)
                .Select(m => new MaterialLine(
                    m.MaterialDate,
                    m.ServiceRequest!.RequestNumber,
                    m.ServiceRequest.Property != null ? m.ServiceRequest.Property.Code + " " + m.ServiceRequest.Property.Name : null,
                    m.ServiceRequest.Unit != null ? m.ServiceRequest.Unit.UnitNumber : null,
                    m.Description, m.Quantity, m.UnitCost, m.Amount, m.Source, m.Reference, m.ChargedTo))
                .ToListAsync();
        }

        // =====================================================================
        //  MAKE READY: VACANT UNITS AFTER A TENANCY
        // =====================================================================

        public sealed record VacantUnit(int UnitId, int PropertyId, string Property, string Unit, DateTime LastTenancyEnded);

        /// <summary>
        /// Vacant units whose last tenancy has ended and that have no make-ready job opened
        /// since: the units the owner wants to see readied for the next tenant.
        /// </summary>
        public async Task<List<VacantUnit>> VacantUnitsAwaitingMakeReadyAsync(int companyId)
        {
            var today = DateTime.Today;
            var units = await _db.Units.AsNoTracking()
                .Where(u => u.Property!.CompanyId == companyId && u.Property.IsActive && u.Status == "Vacant")
                // Nobody in it now...
                .Where(u => !_db.Leases.Any(l => l.UnitId == u.Id && l.Status == LeaseStatuses.Active && l.StartDate <= today && l.EndDate >= today))
                .Select(u => new
                {
                    u.Id,
                    u.PropertyId,
                    Property = u.Property!.Code + " " + u.Property.Name,
                    u.UnitNumber,
                    // ...and someone was: the end of the last tenancy.
                    LastEnd = _db.Leases
                        .Where(l => l.UnitId == u.Id && l.Status != LeaseStatuses.Draft && l.Status != LeaseStatuses.PendingApproval && l.StartDate <= today)
                        .Max(l => (DateTime?)(l.EndDate < today ? l.EndDate : today))
                })
                .Where(u => u.LastEnd != null)
                .ToListAsync();

            var result = new List<VacantUnit>();
            foreach (var unit in units)
            {
                var since = DateTime.SpecifyKind(unit.LastEnd!.Value.Date, DateTimeKind.Local).ToUniversalTime();
                var readied = await _db.TenantServiceRequests.AsNoTracking()
                    .AnyAsync(x => x.UnitId == unit.Id && x.Purpose == MaintenancePurposes.MakeReady &&
                                   x.Status != ServiceRequestStatuses.Cancelled && x.Status != ServiceRequestStatuses.Rejected &&
                                   x.SubmittedAt >= since);
                if (!readied)
                    result.Add(new VacantUnit(unit.Id, unit.PropertyId, unit.Property, unit.UnitNumber, unit.LastEnd.Value.Date));
            }

            return result.OrderBy(x => x.LastTenancyEnded).ToList();
        }

        // =====================================================================
        //  PLANNED MAINTENANCE PROGRAMME
        // =====================================================================

        /// <summary>
        /// Opens a job for every active plan whose due date, less its lead days, has come. A
        /// plan opens one job per run and then moves to its next due date after today, so a
        /// programme that was paused does not open a backlog of identical jobs. Returns the
        /// number of jobs opened.
        /// </summary>
        public async Task<int> OpenDueJobsAsync(int companyId, ServiceRequestService requests, DateTime today)
        {
            var due = await _db.MaintenancePlans.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.IsActive)
                .ToListAsync();
            due = due.Where(x => x.NextDueDate.Date.AddDays(-x.LeadDays) <= today.Date).ToList();

            var opened = 0;
            foreach (var plan in due)
            {
                var (job, error) = await requests.CreateMaintenanceJobAsync(companyId, new ServiceRequestService.InternalJob
                {
                    PropertyId = plan.PropertyId,
                    UnitId = plan.UnitId,
                    Source = ServiceRequestSources.Planned,
                    Purpose = MaintenancePurposes.Preventive,
                    Category = plan.Category,
                    Priority = plan.Priority,
                    Subject = plan.Name,
                    Description = string.IsNullOrWhiteSpace(plan.Description) ? plan.Name : plan.Description,
                    DueDate = plan.NextDueDate,
                    AssignTo = plan.AssignToAppUserId,
                    MaintenancePlanId = plan.Id,
                    RaisedByName = "Planned maintenance programme"
                });

                if (job == null)
                {
                    // A plan pointing at something that has gone stops rather than failing every run.
                    plan.IsActive = false;
                    await _db.SaveChangesAsync();
                    continue;
                }

                plan.LastJobOpenedAt = DateTime.UtcNow;
                do
                {
                    plan.NextDueDate = plan.NextDueDate.AddMonths(Math.Clamp(plan.IntervalMonths, MaintenancePlan.MinInterval, MaintenancePlan.MaxInterval));
                }
                while (plan.NextDueDate.Date.AddDays(-plan.LeadDays) <= today.Date);

                await _db.SaveChangesAsync();
                opened++;
            }

            return opened;
        }
    }
}
