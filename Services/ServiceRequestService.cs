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

        public ServiceRequestService(ApplicationDbContext db)
        {
            _db = db;
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
        }

        /// <summary>
        /// Raises a request for a tenant. Returns the saved record, or null when
        /// validation failed, in which case modelState carries the reasons.
        /// </summary>
        public async Task<TenantServiceRequest?> CreateAsync(
            ModelStateDictionary modelState,
            int tenantId,
            int? portalUserId,
            ServiceRequestCreateViewModel model)
        {
            var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId);
            if (tenant == null)
            {
                modelState.AddModelError(string.Empty, "Your tenant record could not be found.");
                return null;
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
                SubmittedAt = DateTime.UtcNow,
                SubmittedByPortalUserId = portalUserId,
                // Maintenance goes straight to the maintenance team, the least busy first.
                AssignedToAppUserId = model.RequestType == ServiceRequestTypes.Maintenance
                    ? await MaintenanceService.PickTechnicianAsync(_db, tenant.CompanyId)
                    : null
            };

            _db.TenantServiceRequests.Add(request);
            await _db.SaveChangesAsync();

            return request;
        }

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
                    MessageCount = x.Comments.Count(c => !c.IsInternal)
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
        //  STAFF SIDE
        // =====================================================================

        public async Task<ServiceRequestQueueViewModel> GetQueueAsync(
            int companyId, string? status, string? type)
        {
            var query = _db.TenantServiceRequests
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId);

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
                .Where(x => x.CompanyId == companyId)
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

            if ((newStatus == ServiceRequestStatuses.Resolved ||
                 newStatus == ServiceRequestStatuses.Rejected) &&
                string.IsNullOrWhiteSpace(resolutionNotes) &&
                string.IsNullOrWhiteSpace(request.ResolutionNotes))
            {
                return (false, newStatus == ServiceRequestStatuses.Rejected
                    ? "Say why the request is being declined - the tenant will read it."
                    : "Say what was done - the tenant will read it.");
            }

            var previous = request.Status;
            request.Status = newStatus;

            if (!string.IsNullOrWhiteSpace(resolutionNotes))
                request.ResolutionNotes = resolutionNotes.Trim();

            if (!string.IsNullOrWhiteSpace(priority) && ServiceRequestPriorities.All.Contains(priority))
                request.Priority = priority;

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

            return (true, $"Request {request.RequestNumber} is now {ServiceRequestStatuses.Describe(newStatus)}.");
        }

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
