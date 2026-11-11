using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Tenant service requests: raising them from the portal, working them from the
    /// staff queue, and the conversation in between.
    ///
    /// TWO RULES HOLD THIS TOGETHER.
    ///
    /// 1. TENANT METHODS TAKE A TENANT ID AND FILTER ON IT, ALWAYS. A tenant principal
    ///    carries no company claim, so the global company query filter is switched off
    ///    for portal traffic. Nothing here may rely on it. Every tenant-facing query
    ///    ends with a TenantId comparison, and the ones that load a single request take
    ///    the tenant id as well as the request id so that a tenant cannot open someone
    ///    else's request by changing the number in the URL.
    ///
    /// 2. NOTHING HERE TOUCHES THE LEDGER. A request is a conversation, not a
    ///    transaction. The single exception is ApplyPostponementAsync, which moves one
    ///    invoice's DUE DATE after a member of staff has agreed to it - a due date is an
    ///    operational field that no journal depends on, the action is explicit, it is
    ///    stamped with who did it, and it cannot run twice.
    /// </summary>
    public class ServiceRequestService
    {
        private readonly ApplicationDbContext _db;
        private readonly Push.PushNotifier _push;

        public ServiceRequestService(ApplicationDbContext db, Push.PushNotifier push)
        {
            _db = db;
            _push = push;
        }

        // =====================================================================
        //  TENANT SIDE
        // =====================================================================

        /// <summary>Builds the blank form, with the tenant's own contracts and unpaid invoices.</summary>
        public async Task<ServiceRequestCreateViewModel> BuildCreateModelAsync(int tenantId)
        {
            var model = new ServiceRequestCreateViewModel();
            await PopulateOptionsAsync(model, tenantId);

            // One contract is the common case, so choose it rather than making the
            // tenant pick from a list of one.
            if (model.Leases.Count == 1)
                model.LeaseId = model.Leases[0].LeaseId;

            return model;
        }

        public async Task PopulateOptionsAsync(ServiceRequestCreateViewModel model, int tenantId)
        {
            model.Leases = await _db.Leases
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId &&
                            x.Status != LeaseStatuses.Draft && x.Status != LeaseStatuses.PendingApproval)
                .OrderByDescending(x => x.StartDate)
                .Select(x => new ServiceRequestLeaseOption
                {
                    LeaseId = x.Id,
                    ContractNumber = x.ContractNumber,
                    Description = x.Unit!.Property!.Name + " - Unit " + x.Unit.UnitNumber
                })
                .ToListAsync();

            model.OutstandingInvoices = await _db.CustomerInvoices
                .AsNoTracking()
                .Where(x => x.Lease!.TenantId == tenantId
                            && x.Status != "Draft"
                            && x.Status != "Cancelled"
                            && x.TotalAmount > x.AmountPaid)
                .OrderBy(x => x.DueDate)
                .Select(x => new ServiceRequestInvoiceOption
                {
                    InvoiceId = x.Id,
                    InvoiceNumber = x.InvoiceNumber,
                    DueDate = x.DueDate,
                    Outstanding = x.TotalAmount - x.AmountPaid
                })
                .ToListAsync();

            var companyId = await _db.Tenants.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == tenantId).Select(x => (int?)x.CompanyId).FirstOrDefaultAsync();
            model.Services = companyId.HasValue
                ? await PaidServiceService.OptionsAsync(_db, companyId.Value)
                : new();
        }

        /// <summary>
        /// Raises a request for a tenant. Returns the saved record, or null when
        /// validation failed, in which case modelState carries the reasons. The tenant's
        /// own request from the portal passes portalUserId; a request that reached the
        /// office another way (WhatsApp, a call, a visit) passes the staff member who logged
        /// it, and only then are the source and the time received taken from the form. A request
        /// that logged itself (WhatsApp intake) has nobody who already knows, so it asks for notifyStaff.
        /// </summary>
        public async Task<TenantServiceRequest?> CreateAsync(
            ModelStateDictionary modelState,
            int tenantId,
            int? portalUserId,
            ServiceRequestCreateViewModel model,
            bool loggedByStaff = false,
            int? loggedByAppUserId = null,
            string? loggedByName = null,
            bool notifyStaff = false)
        {

            var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId);
            if (tenant == null)
            {
                modelState.AddModelError(string.Empty, "Your tenant record could not be found.");
                return null;
            }

            // An order for a paid service is saved as a maintenance job that carries the
            // service. Its title and text default to the service's own, so a tenant need
            // only pick it.
            ServiceCatalogItem? ordered = null;
            if (model.RequestType == ServiceRequestCreateViewModel.PaidServiceType)
            {
                model.RequestType = ServiceRequestTypes.Maintenance;
                ordered = model.ServiceCatalogItemId.HasValue
                    ? await _db.ServiceCatalogItems.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
                        x.Id == model.ServiceCatalogItemId.Value && x.CompanyId == tenant.CompanyId && x.IsActive)
                    : null;

                if (ordered == null)
                {
                    modelState.AddModelError(nameof(model.ServiceCatalogItemId), "Choose the service from the list.");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(model.Subject))
                        model.Subject = ordered.Name;
                    if (string.IsNullOrWhiteSpace(model.Description))
                        model.Description = $"Service ordered: {ordered.Name}.";
                    if (!model.LeaseId.HasValue)
                        modelState.AddModelError(nameof(model.LeaseId), "Choose the property the service is for.");
                }
            }
            else
            {
                model.ServiceCatalogItemId = null;
            }

            if (string.IsNullOrWhiteSpace(model.Subject))
                modelState.AddModelError(nameof(model.Subject), "Give the request a short title.");

            if (string.IsNullOrWhiteSpace(model.Description))
                modelState.AddModelError(nameof(model.Description), "Describe what you need.");

            if (!ServiceRequestTypes.All.Contains(model.RequestType))
                modelState.AddModelError(nameof(model.RequestType), "Choose the kind of request.");

            // The contract must be the tenant's own. Checked rather than trusted,
            // because the id comes off a form the tenant controls.
            Lease? lease = null;
            if (model.LeaseId.HasValue)
            {
                lease = await _db.Leases
                    .AsNoTracking()
                    .Include(x => x.Unit)
                    .FirstOrDefaultAsync(x => x.Id == model.LeaseId.Value && x.TenantId == tenantId);

                if (lease == null)
                    modelState.AddModelError(nameof(model.LeaseId), "That contract is not one of yours.");
            }

            // Same for the invoice on a postponement request.
            CustomerInvoice? invoice = null;
            if (model.RequestType == ServiceRequestTypes.PaymentPostponement)
            {
                if (!model.RelatedInvoiceId.HasValue)
                {
                    modelState.AddModelError(nameof(model.RelatedInvoiceId),
                        "Choose the invoice you need more time for.");
                }
                else
                {
                    invoice = await _db.CustomerInvoices
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == model.RelatedInvoiceId.Value
                                                  && x.Lease!.TenantId == tenantId);

                    if (invoice == null)
                    {
                        modelState.AddModelError(nameof(model.RelatedInvoiceId),
                            "That invoice is not one of yours.");
                    }
                }

                if (!model.RequestedNewDueDate.HasValue)
                {
                    modelState.AddModelError(nameof(model.RequestedNewDueDate),
                        "Say which date you could pay by.");
                }
                else if (model.RequestedNewDueDate.Value.Date <= DateTime.Today)
                {
                    modelState.AddModelError(nameof(model.RequestedNewDueDate),
                        "The date you propose must be in the future.");
                }
                else if (invoice != null && model.RequestedNewDueDate.Value.Date <= invoice.DueDate.Date)
                {
                    modelState.AddModelError(nameof(model.RequestedNewDueDate),
                        $"That invoice is already due on {invoice.DueDate:dd/MM/yyyy}. " +
                        "Propose a later date than the one you already have.");
                }
            }

            // A tenant behind on payments must contact the office before asking for
            // maintenance. Staff raising a request on a tenant's behalf are not stopped.
            if (portalUserId.HasValue && model.RequestType == ServiceRequestTypes.Maintenance)
            {
                var standing = await TenantStandingService.GetAsync(_db, tenantId);
                if (!standing.IsHealthy)
                    modelState.AddModelError(string.Empty, standing.Message!);
            }

            if (model.PreferredVisitDate.HasValue &&
                model.PreferredVisitDate.Value.Date < DateTime.Today)
            {
                modelState.AddModelError(nameof(model.PreferredVisitDate),
                    "A preferred visit date cannot be in the past.");
            }

            if (loggedByStaff)
            {
                if (!ServiceRequestSources.StaffLogged.Contains(model.Source))
                    modelState.AddModelError(nameof(model.Source), "Say how the request reached you.");

                if (model.ReceivedAt.HasValue && model.ReceivedAt.Value > DateTime.Now.AddMinutes(5))
                    modelState.AddModelError(nameof(model.ReceivedAt), "The time it was received cannot be in the future.");
            }

            if (!modelState.IsValid)
                return null;

            var request = new TenantServiceRequest
            {
                CompanyId = tenant.CompanyId,
                TenantId = tenantId,
                LeaseId = lease?.Id,
                UnitId = lease?.UnitId,
                PropertyId = lease?.Unit?.PropertyId,
                RequestNumber = await GenerateRequestNumberAsync(tenant.CompanyId),
                RequestType = model.RequestType,
                Category = Trim(model.Category),
                Subject = Trim(model.Subject) ?? string.Empty,
                Description = Trim(model.Description) ?? string.Empty,
                Priority = ServiceRequestPriorities.All.Contains(model.Priority)
                    ? model.Priority
                    : ServiceRequestPriorities.Normal,
                PreferredVisitDate = model.RequestType == ServiceRequestTypes.Maintenance
                    ? model.PreferredVisitDate?.Date
                    : null,
                RelatedInvoiceId = invoice?.Id,
                RequestedNewDueDate = invoice != null ? model.RequestedNewDueDate?.Date : null,
                Status = ServiceRequestStatuses.Submitted,
                // A request logged later still counts from when the tenant actually asked.
                SubmittedAt = loggedByStaff && model.ReceivedAt.HasValue ? model.ReceivedAt.Value.ToUniversalTime() : DateTime.UtcNow,
                SubmittedByPortalUserId = portalUserId,
                Source = loggedByStaff ? model.Source : ServiceRequestSources.Portal,
                // Maintenance goes straight to the maintenance team, the least busy first.
                AssignedToAppUserId = model.RequestType == ServiceRequestTypes.Maintenance
                    ? await MaintenanceService.PickTechnicianAsync(_db, tenant.CompanyId)
                    : null
            };

            if (ordered != null && await PaidServiceService.AttachAsync(_db, request, ordered.Id) is { } notOrdered)
            {
                modelState.AddModelError(string.Empty, notOrdered);
                return null;
            }

            // The thread is the audit trail: it says who typed this in for the tenant.
            if (loggedByStaff)
                request.Comments.Add(new TenantServiceRequestComment
                {
                    AuthorType = ServiceRequestAuthorTypes.Staff,
                    AuthorName = loggedByName ?? "Staff",
                    AppUserId = loggedByAppUserId,
                    Body = $"Logged for the tenant. Received by: {ServiceRequestSources.Describe(request.Source)}.",
                    IsInternal = true,
                    CreatedAt = DateTime.UtcNow
                });

            _db.TenantServiceRequests.Add(request);
            await _db.SaveChangesAsync();

            if (request.AssignedToAppUserId.HasValue)
                NotifyTechnician(request, request.AssignedToAppUserId.Value);

            // A tenant with the app on their phone sees the office has picked it up.
            if (loggedByStaff)
                await _push.ToTenantAsync(tenantId,
                    "Request {0}", new object?[] { request.RequestNumber },
                    "We have logged your request. You can follow it here.", Array.Empty<object?>(),
                    $"/portal/requests/{request.Id}", $"request-{request.Id}");

            // Raised by the tenant: the people who handle requests hear about it.
            if (portalUserId.HasValue || notifyStaff)
                await _push.ToAnyPermissionAsync(request.CompanyId, PeopleToTell(request), null,
                    "New tenant request {0}", new object?[] { request.RequestNumber },
                    "{0}: {1}", new object?[] { tenant.FullName, request.Subject },
                    $"/ServiceRequests/Details/{request.Id}", $"request-{request.Id}");

            return request;
        }

        /// <summary>
        /// What a member of staff can set on a request WhatsApp opened by itself, or null when
        /// it is not one or has moved on. Such a request arrives as "Other request" because
        /// nothing in a free-form message says what it is.
        /// </summary>
        public async Task<ServiceRequestClassifyOptions?> GetClassifyOptionsAsync(int companyId, int requestId)
        {
            var request = await _db.TenantServiceRequests.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == requestId && x.CompanyId == companyId);

            if (request?.TenantId == null || !await CanBeClassifiedAsync(request))
                return null;

            var tenantId = request.TenantId.Value;
            var options = new ServiceRequestCreateViewModel();
            await PopulateOptionsAsync(options, tenantId);

            return new ServiceRequestClassifyOptions
            {
                RequestType = request.RequestType,
                Category = request.Category,
                LeaseId = request.LeaseId,
                Leases = options.Leases,
                MaintenanceHold = (await TenantStandingService.GetAsync(_db, tenantId)).Message
            };
        }

        /// <summary>
        /// Sorts a request WhatsApp opened by itself. Choosing maintenance is a person's decision,
        /// so like staff logging a request it is not stopped by a payment hold; the screen shows
        /// the hold first. From maintenance it goes to the team as any other job does.
        /// </summary>
        public async Task<(bool Success, string Message)> ClassifyWhatsAppRequestAsync(
            int companyId, int requestId, string requestType, string? category, int? leaseId, int? appUserId, string? userName)
        {
            var request = await _db.TenantServiceRequests
                .FirstOrDefaultAsync(x => x.Id == requestId && x.CompanyId == companyId);

            if (request == null || !await CanBeClassifiedAsync(request))
                return (false, "This request can no longer be changed.");

            if (!ServiceRequestTypes.Classifiable.Contains(requestType))
                return (false, "Choose the kind of request.");

            Lease? lease = null;
            if (leaseId.HasValue)
            {
                lease = await _db.Leases.AsNoTracking().Include(x => x.Unit)
                    .FirstOrDefaultAsync(x => x.Id == leaseId.Value && x.TenantId == request.TenantId);
                if (lease == null)
                    return (false, "That contract is not this tenant's.");
            }

            var wasMaintenance = request.RequestType == ServiceRequestTypes.Maintenance;
            var isMaintenance = requestType == ServiceRequestTypes.Maintenance;

            request.RequestType = requestType;
            request.Category = isMaintenance ? Trim(category) : null;
            request.LeaseId = lease?.Id;
            request.UnitId = lease?.UnitId;
            request.PropertyId = lease?.Unit?.PropertyId;

            if (isMaintenance && !wasMaintenance)
                request.AssignedToAppUserId = await MaintenanceService.PickTechnicianAsync(_db, companyId);
            else if (!isMaintenance && wasMaintenance)
                request.AssignedToAppUserId = null;

            request.Comments.Add(new TenantServiceRequestComment
            {
                AuthorType = ServiceRequestAuthorTypes.Staff,
                AuthorName = userName ?? "Staff",
                AppUserId = appUserId,
                Body = $"Sorted as: {ServiceRequestTypes.Describe(requestType)}.",
                IsInternal = true,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            if (isMaintenance && !wasMaintenance && request.AssignedToAppUserId.HasValue)
                NotifyTechnician(request, request.AssignedToAppUserId.Value);

            return (true, "Request updated.");
        }

        /// <summary>Only one that opened itself: a person logging a request has already said what it is.</summary>
        private async Task<bool> CanBeClassifiedAsync(TenantServiceRequest request) =>
            request.Source == ServiceRequestSources.WhatsApp &&
            ServiceRequestTypes.Classifiable.Contains(request.RequestType) &&
            (request.Status == ServiceRequestStatuses.Submitted || request.Status == ServiceRequestStatuses.Acknowledged) &&
            await _db.WhatsAppInboundMessages.AsNoTracking().AnyAsync(x =>
                x.TenantServiceRequestId == request.Id && x.CompanyId == request.CompanyId &&
                x.Status == WhatsAppMessageStatuses.RequestCreated);

        // =====================================================================
        //  JOBS NO TENANT ASKED FOR
        // =====================================================================

        /// <summary>A maintenance job raised by management or by the planned maintenance programme.</summary>
        public sealed class InternalJob
        {
            public int PropertyId { get; set; }
            public int? UnitId { get; set; }
            public string Source { get; set; } = ServiceRequestSources.Management;
            public string Purpose { get; set; } = MaintenancePurposes.Repair;
            public string? Category { get; set; }
            public string Priority { get; set; } = ServiceRequestPriorities.Normal;
            public string Subject { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;

            /// <summary>When the work should be done: a planned job's due date, or a target date.</summary>
            public DateTime? DueDate { get; set; }

            /// <summary>The technician to give it to; empty for the least busy one.</summary>
            public int? AssignTo { get; set; }

            public int? MaintenancePlanId { get; set; }
            public int? RaisedByAppUserId { get; set; }
            public string? RaisedByName { get; set; }
        }

        /// <summary>
        /// Opens a maintenance job that no tenant asked for. It has no tenant, so it never
        /// shows on the portal and is never rated; everything else - the technician, the
        /// steps, the materials and who bears their cost, the target times and the dashboard -
        /// works as for a tenant's request. Returns the job, or why it could not be opened.
        /// </summary>
        public async Task<(TenantServiceRequest? Job, string? Error)> CreateMaintenanceJobAsync(int companyId, InternalJob input)
        {
            var property = await _db.Properties.AsNoTracking().IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == input.PropertyId && x.CompanyId == companyId);
            if (property == null)
                return (null, "Choose the property the work is for.");

            if (input.UnitId.HasValue && !await _db.Units.AsNoTracking().IgnoreQueryFilters()
                    .AnyAsync(x => x.Id == input.UnitId.Value && x.PropertyId == property.Id))
                return (null, "That unit is not in the property chosen.");

            if (string.IsNullOrWhiteSpace(input.Subject))
                return (null, "Give the job a short title.");
            if (string.IsNullOrWhiteSpace(input.Description))
                return (null, "Describe the work to be done.");
            if (!ServiceRequestSources.IsInternal(input.Source))
                return (null, "A job without a tenant is raised by management or the planned programme.");
            if (!MaintenancePurposes.All.Contains(input.Purpose))
                return (null, "Choose what the job is for.");
            if (!ServiceRequestPriorities.All.Contains(input.Priority))
                return (null, "Choose how urgent the job is.");
            if (!string.IsNullOrWhiteSpace(input.Category) && !await MaintenanceCategoryService.IsOfferedAsync(_db, companyId, input.Category))
                return (null, "Choose the kind of work from the list.");

            var technicians = await MaintenanceService.TechniciansAsync(_db, companyId);
            int? assignTo = input.AssignTo.HasValue && technicians.Any(t => t.Id == input.AssignTo.Value)
                ? input.AssignTo
                : await MaintenanceService.PickTechnicianAsync(_db, companyId);

            for (var attempt = 1; ; attempt++)
            {
                var job = new TenantServiceRequest
                {
                    CompanyId = companyId,
                    TenantId = null,
                    PropertyId = property.Id,
                    UnitId = input.UnitId,
                    RequestNumber = await GenerateRequestNumberAsync(companyId),
                    RequestType = ServiceRequestTypes.Maintenance,
                    Category = Trim(input.Category),
                    Subject = Cut(input.Subject.Trim(), 200),
                    Description = Cut(input.Description.Trim(), 4000),
                    Priority = input.Priority,
                    PreferredVisitDate = input.DueDate?.Date,
                    Status = ServiceRequestStatuses.Submitted,
                    SubmittedAt = DateTime.UtcNow,
                    Source = input.Source,
                    Purpose = input.Purpose,
                    MaintenancePlanId = input.MaintenancePlanId,
                    AssignedToAppUserId = assignTo
                };

                job.Comments.Add(new TenantServiceRequestComment
                {
                    AuthorType = ServiceRequestAuthorTypes.Staff,
                    AuthorName = input.RaisedByName ?? "System",
                    AppUserId = input.RaisedByAppUserId,
                    Body = input.Source == ServiceRequestSources.Planned
                        ? "Opened by the planned maintenance programme."
                        : $"Raised by management: {MaintenancePurposes.Describe(input.Purpose)}.",
                    IsInternal = true,
                    CreatedAt = DateTime.UtcNow
                });

                _db.TenantServiceRequests.Add(job);
                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException) when (attempt < 3)
                {
                    // Two jobs took the same number at once; the next try gets the next one.
                    _db.Entry(job).State = EntityState.Detached;
                    foreach (var comment in job.Comments)
                        _db.Entry(comment).State = EntityState.Detached;
                    continue;
                }

                if (assignTo.HasValue && assignTo != input.RaisedByAppUserId)
                    NotifyTechnician(job, assignTo.Value);
                return (job, null);
            }
        }

        private static string Cut(string value, int length) => value.Length <= length ? value : value[..length];

        /// <summary>Who hears about a request: whoever handles requests, and for maintenance the maintenance manager too.</summary>
        private static string[] PeopleToTell(TenantServiceRequest request) =>
            request.RequestType == ServiceRequestTypes.Maintenance
                ? new[] { AppPermissions.ServiceRequestsManage, AppPermissions.MaintenanceManage }
                : new[] { AppPermissions.ServiceRequestsManage };

        /// <summary>The technician a maintenance job was just given to.</summary>
        public void NotifyTechnician(TenantServiceRequest job, int technicianId) =>
            _push.ToStaff(new[] { technicianId },
                "New maintenance job {0}", new object?[] { job.RequestNumber },
                "{0}", new object?[] { job.Subject },
                $"/MaintenanceJobs/Details/{job.Id}", $"job-{job.Id}");

        public async Task<List<ServiceRequestListItemViewModel>> GetTenantRequestsAsync(int tenantId)
        {
            return await _db.TenantServiceRequests
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId)
                .OrderByDescending(x => x.SubmittedAt)
                .Select(x => new ServiceRequestListItemViewModel
                {
                    Id = x.Id,
                    RequestNumber = x.RequestNumber,
                    RequestType = x.RequestType,
                    Category = x.Category,
                    Subject = x.Subject,
                    Status = x.Status,
                    Priority = x.Priority,
                    SubmittedAt = x.SubmittedAt,

                    // Internal notes are not messages as far as the tenant is concerned.
                    MessageCount = x.Comments.Count(c => !c.IsInternal),

                    CanBeRated = x.RequestType == ServiceRequestTypes.Maintenance && x.Rating == null &&
                                 (x.Status == ServiceRequestStatuses.Resolved || x.Status == ServiceRequestStatuses.Closed)
                })
                .ToListAsync();
        }

        /// <summary>
        /// One request, as the TENANT may see it. Takes the tenant id as well as the
        /// request id, so a mismatched pair returns null rather than someone else's
        /// request. Internal staff notes are filtered out here, in the query, not in
        /// the view - a note that leaks is a note read by the person it was about.
        /// </summary>
        public async Task<ServiceRequestDetailViewModel?> GetTenantRequestAsync(int tenantId, int requestId)
        {
            var request = await _db.TenantServiceRequests
                .AsNoTracking()
                .Include(x => x.Lease)
                .Include(x => x.Unit)
                .Include(x => x.Property)
                .Include(x => x.RelatedInvoice)
                .Include(x => x.Comments)
                .FirstOrDefaultAsync(x => x.Id == requestId && x.TenantId == tenantId);

            if (request == null)
                return null;

            var model = MapDetail(request, includeInternal: false);
            return model;
        }

        /// <summary>A tenant adding a message to their own request.</summary>
        public async Task<bool> AddTenantCommentAsync(
            int tenantId, int requestId, int? portalUserId, string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;

            var request = await _db.TenantServiceRequests
                .Include(x => x.Tenant)
                .FirstOrDefaultAsync(x => x.Id == requestId && x.TenantId == tenantId);

            if (request == null)
                return false;

            // A closed request is a finished conversation. Reopening it by replying
            // would hide the message, because nobody is looking at closed requests.
            if (request.Status == ServiceRequestStatuses.Closed ||
                request.Status == ServiceRequestStatuses.Cancelled)
            {
                return false;
            }

            request.Comments.Add(new TenantServiceRequestComment
            {
                AuthorType = ServiceRequestAuthorTypes.Tenant,
                AuthorName = request.Tenant?.FullName ?? "Tenant",
                TenantPortalUserId = portalUserId,
                Body = body.Trim(),
                IsInternal = false,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            // The person working it hears of the reply; with nobody on it yet, whoever handles requests.
            const string title = "{0} replied on {1}";
            var values = new object?[] { request.Tenant?.FullName ?? "The tenant", request.RequestNumber };
            var url = request.AssignedToAppUserId.HasValue && request.RequestType == ServiceRequestTypes.Maintenance
                ? $"/MaintenanceJobs/Details/{request.Id}"
                : $"/ServiceRequests/Details/{request.Id}";
            if (request.AssignedToAppUserId.HasValue)
                _push.ToStaff(new[] { request.AssignedToAppUserId.Value }, title, values, "{0}", new object?[] { request.Subject }, url, $"request-{request.Id}");
            else
                await _push.ToAnyPermissionAsync(request.CompanyId, PeopleToTell(request), null,
                    title, values, "{0}", new object?[] { request.Subject }, url, $"request-{request.Id}");

            return true;
        }

        /// <summary>A tenant withdrawing a request, allowed only before work starts.</summary>
        public async Task<bool> CancelByTenantAsync(int tenantId, int requestId)
        {
            var request = await _db.TenantServiceRequests
                .Include(x => x.Tenant)
                .FirstOrDefaultAsync(x => x.Id == requestId && x.TenantId == tenantId);

            if (request == null || !request.CanTenantCancel)
                return false;

            request.Status = ServiceRequestStatuses.Cancelled;
            request.ClosedAt = DateTime.UtcNow;

            request.Comments.Add(new TenantServiceRequestComment
            {
                AuthorType = ServiceRequestAuthorTypes.Tenant,
                AuthorName = request.Tenant?.FullName ?? "Tenant",
                Body = "Request withdrawn by the tenant.",
                IsInternal = false,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            return true;
        }

        // =====================================================================
        //  RATING A FINISHED MAINTENANCE JOB
        // =====================================================================

        /// <summary>The tenant rates their own finished job on the portal.</summary>
        public async Task<(bool Success, string Message)> RateByTenantAsync(
            int tenantId, int requestId, int rating, string? feedback)
        {
            var request = await _db.TenantServiceRequests
                .FirstOrDefaultAsync(x => x.Id == requestId && x.TenantId == tenantId);

            return request == null
                ? (false, "The request was not found.")
                : await ApplyRatingAsync(request, rating, feedback, null);
        }

        /// <summary>
        /// Records a rating the tenant gave by WhatsApp or phone, stamped with who entered
        /// it. Whoever did the job cannot enter their own score.
        /// </summary>
        public async Task<(bool Success, string Message)> RecordRatingForTenantAsync(
            int companyId, int requestId, int rating, string? feedback, int? actingAppUserId, string? actingUserName)
        {
            // Without a name on it, this would read as the tenant's own rating.
            if (!actingAppUserId.HasValue)
                return (false, "Sign in with your own user to record a rating.");

            var request = await _db.TenantServiceRequests
                .FirstOrDefaultAsync(x => x.Id == requestId && x.CompanyId == companyId);

            if (request == null)
                return (false, "The request was not found.");

            if (request.AssignedToAppUserId == actingAppUserId)
                return (false, "You carried out this job, so someone else must record its rating.");

            return await ApplyRatingAsync(request, rating, feedback, actingAppUserId, actingUserName);
        }

        private async Task<(bool Success, string Message)> ApplyRatingAsync(
            TenantServiceRequest request, int rating, string? feedback, int? recordedByAppUserId, string? recordedByName = null)
        {
            if (!request.CanBeRated)
                return (false, request.Rating.HasValue
                    ? "This job has already been rated."
                    : "A job can be rated once it is done.");

            if (rating < ServiceRequestRatings.Lowest || rating > ServiceRequestRatings.Highest)
                return (false, "Choose a rating from 1 to 10.");

            var text = Trim(feedback);
            if (text != null && text.Length > ServiceRequestRatings.MaxFeedbackLength)
                text = text[..ServiceRequestRatings.MaxFeedbackLength];

            request.Rating = rating;
            request.RatingFeedback = text;
            request.RatedAt = DateTime.UtcNow;
            request.RatedByAppUserId = recordedByAppUserId;

            if (recordedByAppUserId.HasValue)
                _db.TenantServiceRequestComments.Add(new TenantServiceRequestComment
                {
                    TenantServiceRequestId = request.Id,
                    AuthorType = ServiceRequestAuthorTypes.Staff,
                    AuthorName = recordedByName ?? "Staff",
                    AppUserId = recordedByAppUserId,
                    Body = $"Rating of {rating}/{ServiceRequestRatings.Highest} recorded for the tenant.",
                    IsInternal = true,
                    CreatedAt = DateTime.UtcNow
                });

            await _db.SaveChangesAsync();

            // A poor score should reach the manager while it can still be put right.
            if (ServiceRequestRatings.IsLow(rating))
                await _push.ToAnyPermissionAsync(request.CompanyId, PeopleToTell(request), null,
                    "Low rating on job {0}", new object?[] { request.RequestNumber },
                    "The tenant rated it {0} out of 10.", new object?[] { rating },
                    $"/MaintenanceJobs/Details/{request.Id}", $"rating-{request.Id}");

            return (true, "Thank you. Your rating has been recorded.");
        }

        // =====================================================================
        //  STAFF SIDE
        // =====================================================================

        /// <summary>The tenants staff can log a request for, with the phone number to match a WhatsApp message.</summary>
        public async Task<List<ServiceRequestTenantOption>> GetTenantOptionsAsync(int companyId) =>
            await _db.Tenants.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.FullName)
                .Select(x => new ServiceRequestTenantOption { Id = x.Id, Code = x.Code, Name = x.FullName, Phone = x.Phone })
                .ToListAsync();

        public async Task<ServiceRequestQueueViewModel> GetQueueAsync(
            int companyId, string? status, string? type)
        {
            // What tenants asked for. A job management or the planned programme raised has no
            // tenant, and is worked on the Maintenance Jobs screen.
            var query = _db.TenantServiceRequests
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.TenantId != null);

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = status == "Open"
                    ? query.Where(x => x.Status != ServiceRequestStatuses.Closed
                                       && x.Status != ServiceRequestStatuses.Cancelled
                                       && x.Status != ServiceRequestStatuses.Rejected)
                    : query.Where(x => x.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(type))
                query = query.Where(x => x.RequestType == type);

            var rows = await query
                .Select(x => new ServiceRequestListItemViewModel
                {
                    Id = x.Id,
                    RequestNumber = x.RequestNumber,
                    RequestType = x.RequestType,
                    Category = x.Category,
                    Subject = x.Subject,
                    Status = x.Status,
                    Priority = x.Priority,
                    SubmittedAt = x.SubmittedAt,
                    TenantName = x.Tenant!.FullName,
                    TenantCode = x.Tenant.Code,
                    UnitDisplay = x.Unit != null
                        ? x.Unit.Property!.Code + " / " + x.Unit.UnitNumber
                        : null,
                    MessageCount = x.Comments.Count,
                    Source = x.Source,
                    AwaitingPostponementAction =
                        x.RequestType == ServiceRequestTypes.PaymentPostponement &&
                        x.Status == ServiceRequestStatuses.Resolved &&
                        x.RelatedInvoiceId != null &&
                        x.RequestedNewDueDate != null &&
                        !x.PostponementApplied
                })
                .ToListAsync();

            // Worst first, then oldest first. Sorted in memory because the priority
            // weighting is a C# concept the database knows nothing about.
            var ordered = rows
                .OrderBy(x => ServiceRequestPriorities.Weight(x.Priority))
                .ThenBy(x => x.SubmittedAt)
                .ToList();

            var all = await _db.TenantServiceRequests
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.TenantId != null)
                .Select(x => new
                {
                    x.Status,
                    x.RequestType,
                    x.RelatedInvoiceId,
                    x.RequestedNewDueDate,
                    x.PostponementApplied
                })
                .ToListAsync();

            return new ServiceRequestQueueViewModel
            {
                Requests = ordered,
                StatusFilter = status,
                TypeFilter = type,
                OpenCount = all.Count(x => x.Status != ServiceRequestStatuses.Closed
                                           && x.Status != ServiceRequestStatuses.Cancelled
                                           && x.Status != ServiceRequestStatuses.Rejected),
                SubmittedCount = all.Count(x => x.Status == ServiceRequestStatuses.Submitted),
                InProgressCount = all.Count(x => x.Status == ServiceRequestStatuses.InProgress),
                AwaitingPostponementCount = all.Count(x =>
                    x.RequestType == ServiceRequestTypes.PaymentPostponement &&
                    x.Status == ServiceRequestStatuses.Resolved &&
                    x.RelatedInvoiceId != null &&
                    x.RequestedNewDueDate != null &&
                    !x.PostponementApplied)
            };
        }

        /// <summary>One request as STAFF may see it, internal notes included.</summary>
        public async Task<ServiceRequestDetailViewModel?> GetStaffRequestAsync(int companyId, int requestId)
        {
            var request = await _db.TenantServiceRequests
                .AsNoTracking()
                .Include(x => x.Tenant)
                .Include(x => x.Lease)
                .Include(x => x.Unit)
                    .ThenInclude(x => x!.Property)
                .Include(x => x.Property)
                .Include(x => x.RelatedInvoice)
                .Include(x => x.Comments)
                .FirstOrDefaultAsync(x => x.Id == requestId && x.CompanyId == companyId);

            if (request == null)
                return null;

            var model = MapDetail(request, includeInternal: true);

            model.TenantName = request.Tenant?.FullName;
            model.TenantCode = request.Tenant?.Code;
            model.TenantEmail = request.Tenant?.Email;
            model.TenantPhone = request.Tenant?.Phone;

            if (request.AssignedToAppUserId.HasValue)
            {
                model.AssignedToName = await _db.AppUsers
                    .AsNoTracking()
                    .Where(x => x.Id == request.AssignedToAppUserId.Value)
                    .Select(x => x.DisplayName)
                    .FirstOrDefaultAsync();
            }

            if (request.RatedByAppUserId.HasValue)
            {
                model.RatedByStaffName = await _db.AppUsers
                    .AsNoTracking()
                    .Where(x => x.Id == request.RatedByAppUserId.Value)
                    .Select(x => x.DisplayName)
                    .FirstOrDefaultAsync();
            }

            return model;
        }

        /// <summary>
        /// Moves a request along and records who said so. Status names are checked
        /// against the known set so a hand-typed URL cannot invent one.
        /// </summary>
        public async Task<(bool Success, string Message)> UpdateStatusAsync(
            int companyId,
            int requestId,
            string newStatus,
            string? resolutionNotes,
            int? assignedToAppUserId,
            string? priority,
            int? actingAppUserId,
            string? actingUserName)
        {
            if (!ServiceRequestStatuses.All.Contains(newStatus))
                return (false, "That is not a valid status.");

            if (newStatus == ServiceRequestStatuses.Cancelled)
                return (false, "Only the tenant can withdraw a request.");

            var request = await _db.TenantServiceRequests
                .Include(x => x.Comments)
                .FirstOrDefaultAsync(x => x.Id == requestId && x.CompanyId == companyId);

            if (request == null)
                return (false, "The request was not found.");

            if (request.Status == ServiceRequestStatuses.Cancelled)
                return (false, "The tenant withdrew this request, so it cannot be reopened.");

            // A service the tenant pays for first is not started, or signed off, unpaid.
            if ((newStatus == ServiceRequestStatuses.InProgress || newStatus == ServiceRequestStatuses.Resolved) &&
                await PaidServiceService.WorkHoldAsync(_db, request) is { } hold)
                return (false, hold);

            if ((newStatus == ServiceRequestStatuses.Resolved ||
                 newStatus == ServiceRequestStatuses.Rejected) &&
                string.IsNullOrWhiteSpace(resolutionNotes) &&
                string.IsNullOrWhiteSpace(request.ResolutionNotes))
            {
                return (false, newStatus == ServiceRequestStatuses.Rejected
                    ? "Say why the request is being declined - the tenant will read it."
                    : "Say what was done - the tenant will read it.");
            }

            // A tenant's score stays with the person it was given to.
            if (request.Rating.HasValue && request.AssignedToAppUserId.HasValue &&
                assignedToAppUserId.HasValue && assignedToAppUserId != request.AssignedToAppUserId)
                return (false, "This job has been rated, so it cannot move to someone else.");

            var previous = request.Status;
            request.Status = newStatus;

            if (!string.IsNullOrWhiteSpace(resolutionNotes))
                request.ResolutionNotes = resolutionNotes.Trim();

            if (!string.IsNullOrWhiteSpace(priority) && ServiceRequestPriorities.All.Contains(priority))
                request.Priority = priority;

            var reassignedTo = assignedToAppUserId.HasValue && assignedToAppUserId != request.AssignedToAppUserId
                ? assignedToAppUserId
                : null;
            if (assignedToAppUserId.HasValue)
                request.AssignedToAppUserId = assignedToAppUserId.Value;

            var now = DateTime.UtcNow;

            if (newStatus == ServiceRequestStatuses.Acknowledged && request.AcknowledgedAt == null)
                request.AcknowledgedAt = now;

            // Starting work also counts as the first response when nobody acknowledged it.
            if (newStatus == ServiceRequestStatuses.InProgress)
            {
                request.AcknowledgedAt ??= now;
                request.WorkStartedAt ??= now;
            }

            if (newStatus == ServiceRequestStatuses.Resolved || newStatus == ServiceRequestStatuses.Rejected)
                request.ResolvedAt = now;

            if (newStatus == ServiceRequestStatuses.Closed)
                request.ClosedAt = now;

            // The thread is the audit trail, so every status change leaves a line in it.
            request.Comments.Add(new TenantServiceRequestComment
            {
                AuthorType = ServiceRequestAuthorTypes.Staff,
                AuthorName = actingUserName ?? "Staff",
                AppUserId = actingAppUserId,
                Body = $"Status changed from {ServiceRequestStatuses.Describe(previous)} " +
                       $"to {ServiceRequestStatuses.Describe(newStatus)}.",
                IsInternal = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();

            if (reassignedTo.HasValue && reassignedTo != actingAppUserId && request.RequestType == ServiceRequestTypes.Maintenance)
                NotifyTechnician(request, reassignedTo.Value);

            // A job no tenant asked for has nobody to tell.
            if (request.TenantId.HasValue && previous != newStatus && TenantStatusMessage(newStatus, request.CanBeRated) is { } line)
                await _push.ToTenantAsync(request.TenantId.Value,
                    "Request {0}", new object?[] { request.RequestNumber }, line, Array.Empty<object?>(),
                    $"/portal/requests/{request.Id}", $"request-{request.Id}");

            return (true, $"Request {request.RequestNumber} is now {ServiceRequestStatuses.Describe(newStatus)}.");
        }

        /// <summary>What the tenant's phone says when their request moves on; nothing for the steps they do themselves.</summary>
        private static string? TenantStatusMessage(string status, bool canBeRated) => status switch
        {
            ServiceRequestStatuses.Acknowledged => "We have seen your request and will be in touch.",
            ServiceRequestStatuses.InProgress => "Work on your request has started.",
            ServiceRequestStatuses.Resolved when canBeRated => "The job is done. Open it to read our reply and rate the work.",
            ServiceRequestStatuses.Resolved => "Your request is resolved. Open it to read our reply.",
            ServiceRequestStatuses.Rejected => "Your request was declined. Open it to read why.",
            ServiceRequestStatuses.Closed => "Your request is closed.",
            _ => null
        };

        public async Task<bool> AddStaffCommentAsync(
            int companyId, int requestId, int? appUserId, string? userName, string body, bool isInternal)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;

            var request = await _db.TenantServiceRequests
                .Include(x => x.Comments)
                .FirstOrDefaultAsync(x => x.Id == requestId && x.CompanyId == companyId);

            if (request == null)
                return false;

            request.Comments.Add(new TenantServiceRequestComment
            {
                AuthorType = ServiceRequestAuthorTypes.Staff,
                AuthorName = userName ?? "Staff",
                AppUserId = appUserId,
                Body = body.Trim(),
                IsInternal = isInternal,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            // A note among staff stays among staff.
            if (!isInternal && request.TenantId.HasValue)
                await _push.ToTenantAsync(request.TenantId.Value,
                    "New reply on {0}", new object?[] { request.RequestNumber },
                    "Open the request to read it.", Array.Empty<object?>(),
                    $"/portal/requests/{request.Id}", $"request-{request.Id}");

            return true;
        }

        /// <summary>
        /// Carries out an agreed postponement by moving the invoice's due date.
        ///
        /// This is the ONLY method in the portal feature that changes a record outside
        /// the request itself, and it is deliberately narrow. A due date drives when an
        /// invoice is chased, not what it is worth: no journal references it, so moving
        /// it alters no balance, no trial balance figure and no VAT return. The invoice
        /// amount, its tax and its journal are untouched. Granting more time to pay is
        /// not a credit note.
        /// </summary>
        public async Task<(bool Success, string Message)> ApplyPostponementAsync(
            int companyId, int requestId, int? actingAppUserId, string? actingUserName)
        {
            var request = await _db.TenantServiceRequests
                .Include(x => x.RelatedInvoice)
                .Include(x => x.Comments)
                .FirstOrDefaultAsync(x => x.Id == requestId && x.CompanyId == companyId);

            if (request == null)
                return (false, "The request was not found.");

            if (request.RequestType != ServiceRequestTypes.PaymentPostponement)
                return (false, "This is not a payment postponement request.");

            if (request.PostponementApplied)
                return (false, "The new due date has already been applied.");

            if (request.RelatedInvoice == null || !request.RequestedNewDueDate.HasValue)
                return (false, "This request does not name an invoice and a new date.");

            if (request.Status != ServiceRequestStatuses.Resolved)
                return (false, "Mark the request as resolved first - that is the record of your agreement.");

            var invoice = request.RelatedInvoice;

            if (invoice.AmountPaid >= invoice.TotalAmount)
                return (false, $"Invoice {invoice.InvoiceNumber} is already paid in full.");

            var previousDueDate = invoice.DueDate;
            invoice.DueDate = request.RequestedNewDueDate.Value.Date;

            request.PostponementApplied = true;
            request.PostponementAppliedAt = DateTime.UtcNow;
            request.PostponementAppliedByAppUserId = actingAppUserId;

            request.Comments.Add(new TenantServiceRequestComment
            {
                AuthorType = ServiceRequestAuthorTypes.Staff,
                AuthorName = actingUserName ?? "Staff",
                AppUserId = actingAppUserId,
                Body = $"Due date of invoice {invoice.InvoiceNumber} moved from " +
                       $"{previousDueDate:dd/MM/yyyy} to {invoice.DueDate:dd/MM/yyyy}. " +
                       "The amount owed is unchanged.",
                IsInternal = false,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            return (true, $"Invoice {invoice.InvoiceNumber} is now due on {invoice.DueDate:dd/MM/yyyy}.");
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        private static ServiceRequestDetailViewModel MapDetail(
            TenantServiceRequest request, bool includeInternal)
        {
            return new ServiceRequestDetailViewModel
            {
                Id = request.Id,
                RequestNumber = request.RequestNumber,
                RequestType = request.RequestType,
                Category = request.Category,
                Subject = request.Subject,
                Description = request.Description,
                Status = request.Status,
                Priority = request.Priority,
                SubmittedAt = request.SubmittedAt,
                AcknowledgedAt = request.AcknowledgedAt,
                ResolvedAt = request.ResolvedAt,
                ClosedAt = request.ClosedAt,
                PreferredVisitDate = request.PreferredVisitDate,
                ContractNumber = request.Lease?.ContractNumber,
                UnitDisplay = request.Unit?.UnitNumber,
                PropertyName = request.Property?.Name,
                ResolutionNotes = request.ResolutionNotes,
                Source = request.Source,
                Purpose = request.Purpose,
                Rating = request.Rating,
                RatingFeedback = request.RatingFeedback,
                RatedAt = request.RatedAt,
                CanBeRated = request.CanBeRated,
                RelatedInvoiceNumber = request.RelatedInvoice?.InvoiceNumber,
                RelatedInvoiceDueDate = request.RelatedInvoice?.DueDate,
                RelatedInvoiceOutstanding = request.RelatedInvoice != null
                    ? request.RelatedInvoice.TotalAmount - request.RelatedInvoice.AmountPaid
                    : null,
                RequestedNewDueDate = request.RequestedNewDueDate,
                PostponementApplied = request.PostponementApplied,
                PostponementAppliedAt = request.PostponementAppliedAt,
                AssignedToAppUserId = request.AssignedToAppUserId,
                CanTenantCancel = request.CanTenantCancel,
                AwaitingPostponementAction = request.AwaitingPostponementAction,
                Comments = request.Comments
                    .Where(x => includeInternal || !x.IsInternal)
                    .OrderBy(x => x.CreatedAt)
                    .Select(x => new ServiceRequestCommentViewModel
                    {
                        AuthorType = x.AuthorType,
                        AuthorName = x.AuthorName,
                        Body = x.Body,
                        CreatedAt = x.CreatedAt,
                        IsInternal = x.IsInternal
                    })
                    .ToList()
            };
        }

        /// <summary>One sequence per company per year: REQ-2026-0001.</summary>
        private async Task<string> GenerateRequestNumberAsync(int companyId)
        {
            var prefix = $"REQ-{DateTime.Today.Year}-";

            var existing = await _db.TenantServiceRequests
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.RequestNumber.StartsWith(prefix))
                .Select(x => x.RequestNumber)
                .ToListAsync();

            var highest = 0;
            foreach (var number in existing)
            {
                if (number.Length <= prefix.Length)
                    continue;

                if (int.TryParse(number[prefix.Length..], out var value) && value > highest)
                    highest = value;
            }

            for (var next = highest + 1; next < highest + 1000; next++)
            {
                var candidate = $"{prefix}{next:0000}";

                var taken = await _db.TenantServiceRequests
                    .IgnoreQueryFilters()
                    .AnyAsync(x => x.CompanyId == companyId && x.RequestNumber == candidate);

                if (!taken)
                    return candidate;
            }

            throw new InvalidOperationException("Could not allocate a request number.");
        }

        private static string? Trim(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
