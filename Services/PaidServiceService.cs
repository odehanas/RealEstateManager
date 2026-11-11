using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Services a tenant asks for and pays for: the company's price list, the order (a
    /// tenant request that carries a service), the invoice raised for it, the hold on
    /// the work until it is paid, and what the service earned against what it cost.
    ///
    /// The order itself changes nothing in the accounts - a tenant filling in a form
    /// never does. The invoice is raised by a member of staff, explicitly, from the
    /// request; it is a tax invoice in the company's own name, like a cheque charge,
    /// credited to the service's income account.
    /// </summary>
    public class PaidServiceService
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _posting;
        private readonly JournalWorkflowService _workflow;
        private readonly Push.PushNotifier _push;

        public PaidServiceService(ApplicationDbContext db, AccountingPostingService posting, JournalWorkflowService workflow, Push.PushNotifier push)
        {
            _db = db;
            _posting = posting;
            _workflow = workflow;
            _push = push;
        }

        // =====================================================================
        //  THE PRICE LIST
        // =====================================================================

        public Task<List<ServiceCatalogItem>> ListAsync(int companyId) =>
            _db.ServiceCatalogItems.IgnoreQueryFilters().AsNoTracking()
                .Include(x => x.IncomeAccount)
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.IsActive).ThenBy(x => x.Kind).ThenBy(x => x.Name)
                .ToListAsync();

        /// <summary>Income accounts a service can be credited to.</summary>
        public Task<List<Account>> IncomeAccountsAsync(int companyId) =>
            _db.Accounts.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.IsActive && x.IsPostable && x.AccountType == "Revenue")
                .OrderBy(x => x.Code)
                .ToListAsync();

        /// <summary>Adds a service or changes one. Returns what is wrong, or null when saved.</summary>
        public async Task<string?> SaveAsync(int companyId, ServiceCatalogItem input, string? byName)
        {
            var name = string.Join(' ', (input.Name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (name.Length == 0)
                return "Give the service a name.";
            if (name.Length > ServiceCatalogItem.MaxNameLength)
                return $"Keep the name within {ServiceCatalogItem.MaxNameLength} characters.";
            if (!ServiceKinds.All.Contains(input.Kind))
                return "Choose whether it is special maintenance or a special request.";
            if (input.SellingPrice < 0m || input.ExpectedCost < 0m)
                return "The price and the cost cannot be negative.";
            if (input.IncomeAccountId.HasValue &&
                !await _db.Accounts.IgnoreQueryFilters().AnyAsync(x =>
                    x.Id == input.IncomeAccountId.Value && x.CompanyId == companyId && x.IsPostable && x.AccountType == "Revenue"))
                return "Choose one of the company's income accounts.";
            if (!string.IsNullOrWhiteSpace(input.Category) &&
                !(await MaintenanceCategoryService.NamesAsync(_db, companyId)).Contains(input.Category.Trim(), StringComparer.OrdinalIgnoreCase))
                return "Choose the kind of work from the list.";
            if (await _db.ServiceCatalogItems.IgnoreQueryFilters()
                    .AnyAsync(x => x.CompanyId == companyId && x.Id != input.Id && x.Name == name))
                return "Another service already has that name.";

            ServiceCatalogItem item;
            if (input.Id == 0)
            {
                item = new ServiceCatalogItem { CompanyId = companyId, CreatedByName = byName };
                _db.ServiceCatalogItems.Add(item);
            }
            else
            {
                var existing = await _db.ServiceCatalogItems.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.Id == input.Id && x.CompanyId == companyId);
                if (existing == null)
                    return "That service was not found.";
                item = existing;
            }

            item.Name = name;
            item.Kind = input.Kind;
            item.Category = string.IsNullOrWhiteSpace(input.Category) ? null : input.Category.Trim();
            item.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
            item.SellingPrice = Math.Round(input.SellingPrice, 2);
            item.ExpectedCost = Math.Round(input.ExpectedCost, 2);
            item.IncomeAccountId = input.IncomeAccountId;
            item.Subcontracted = input.Subcontracted;
            item.PayBeforeWork = input.PayBeforeWork;
            item.IsActive = input.IsActive;
            await _db.SaveChangesAsync();
            return null;
        }

        // =====================================================================
        //  ORDERING
        // =====================================================================

        /// <summary>One line of the list a tenant, or staff for them, orders from.</summary>
        public sealed record Option(int Id, string Name, string Kind, string? Description, decimal Price, decimal PriceWithVat, bool PayBeforeWork);

        public static async Task<List<Option>> OptionsAsync(ApplicationDbContext db, int companyId)
        {
            var items = await db.ServiceCatalogItems.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.Kind).ThenBy(x => x.Name)
                .ToListAsync();
            if (items.Count == 0)
                return new List<Option>();

            var company = await db.Companies.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == companyId);
            return items
                .Select(x => new Option(x.Id, x.Name, x.Kind, x.Description, x.SellingPrice,
                    PdcService.PriceCharge(company, x.SellingPrice).Total, x.PayBeforeWork))
                .ToList();
        }

        /// <summary>
        /// Makes a new request an order for a service: it becomes a maintenance job that
        /// carries the service's name and price as they are today. Returns what is wrong,
        /// or null. Nothing is invoiced here.
        /// </summary>
        public static async Task<string?> AttachAsync(ApplicationDbContext db, TenantServiceRequest request, int serviceId)
        {
            var service = await db.ServiceCatalogItems.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == serviceId && x.CompanyId == request.CompanyId && x.IsActive);
            if (service == null)
                return "Choose one of the services on the list.";
            if (!request.LeaseId.HasValue)
                return "Choose the property the service is for: the invoice is raised on its contract.";

            request.RequestType = ServiceRequestTypes.Maintenance;
            request.ServiceCatalogItemId = service.Id;
            request.ServiceName = service.Name;
            request.ServiceKind = service.Kind;
            request.ServicePrice = service.SellingPrice;
            request.ServiceExpectedCost = service.ExpectedCost;
            request.ServicePayFirst = service.PayBeforeWork;
            request.Category = service.Category ?? request.Category;
            return null;
        }

        // =====================================================================
        //  WHERE AN ORDER STANDS
        // =====================================================================

        public static class Stages
        {
            public const string NotInvoiced = "NotInvoiced";
            public const string InvoiceNotPosted = "InvoiceNotPosted";
            public const string AwaitingPayment = "AwaitingPayment";
            public const string PartlyPaid = "PartlyPaid";
            public const string Paid = "Paid";

            public static string Describe(string stage) => stage switch
            {
                NotInvoiced => "Not invoiced yet",
                InvoiceNotPosted => "Invoice awaiting posting",
                AwaitingPayment => "Awaiting payment",
                PartlyPaid => "Partly paid",
                Paid => "Paid",
                _ => stage
            };

            public static string BadgeClass(string stage) => stage switch
            {
                Paid => "bg-success",
                PartlyPaid => "bg-info text-dark",
                AwaitingPayment => "bg-warning text-dark",
                _ => "bg-secondary"
            };
        }

        public sealed class State
        {
            public int RequestId { get; set; }
            public string RequestNumber { get; set; } = string.Empty;
            public string RequestStatus { get; set; } = string.Empty;
            public DateTime SubmittedAt { get; set; }
            public string? Tenant { get; set; }
            public string? Unit { get; set; }
            public bool HasLease { get; set; }

            public string Name { get; set; } = string.Empty;
            public string? Kind { get; set; }
            public bool PayFirst { get; set; }

            /// <summary>Before VAT: as invoiced, or as quoted while there is no invoice.</summary>
            public decimal Net { get; set; }
            public decimal Vat { get; set; }
            public decimal Total { get; set; }

            public int? InvoiceId { get; set; }
            public string? InvoiceNumber { get; set; }
            public decimal Paid { get; set; }
            public string Stage { get; set; } = Stages.NotInvoiced;

            public decimal ExpectedCost { get; set; }

            /// <summary>Materials and subcontractor's charges recorded on the job.</summary>
            public decimal ActualCost { get; set; }
            public decimal Margin => Net - ActualCost;

            public bool IsPaid => Stage == Stages.Paid;

            /// <summary>The work may not start: the service is paid for first and has not been.</summary>
            public bool OnHold => PayFirst && !IsPaid;

            public bool RequestIsOpen => RequestStatus != ServiceRequestStatuses.Cancelled &&
                                         RequestStatus != ServiceRequestStatuses.Rejected &&
                                         RequestStatus != ServiceRequestStatuses.Closed;
        }

        /// <summary>Where one order stands, or null when the request is not an order for a service.</summary>
        public async Task<State?> StateAsync(int requestId) =>
            (await StatesAsync(_db.TenantServiceRequests.IgnoreQueryFilters().Where(x => x.Id == requestId))).FirstOrDefault();

        /// <summary>Every order raised in a period, newest first, for the report.</summary>
        public Task<List<State>> OrdersAsync(int companyId, DateTime from, DateTime to)
        {
            var start = from.Date.ToUniversalTime();
            var end = to.Date.AddDays(1).ToUniversalTime();
            return StatesAsync(_db.TenantServiceRequests.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.SubmittedAt >= start && x.SubmittedAt < end));
        }

        private async Task<List<State>> StatesAsync(IQueryable<TenantServiceRequest> requests)
        {
            var rows = await requests.AsNoTracking()
                .Where(x => x.ServiceCatalogItemId != null)
                .OrderByDescending(x => x.SubmittedAt)
                .Select(x => new
                {
                    x.Id, x.CompanyId, x.RequestNumber, x.Status, x.SubmittedAt, x.LeaseId,
                    Tenant = x.Tenant != null ? x.Tenant.FullName : null,
                    Unit = x.Unit != null ? x.Unit.UnitNumber : null,
                    x.ServiceName, x.ServiceKind, x.ServicePayFirst, x.ServicePrice, x.ServiceExpectedCost, x.ServiceInvoiceId,
                    Cost = _db.MaintenanceMaterials.IgnoreQueryFilters()
                        .Where(m => m.TenantServiceRequestId == x.Id).Sum(m => (decimal?)m.Amount) ?? 0m
                })
                .ToListAsync();
            if (rows.Count == 0)
                return new List<State>();

            var invoiceIds = rows.Where(x => x.ServiceInvoiceId.HasValue).Select(x => x.ServiceInvoiceId!.Value).ToList();
            var invoices = await _db.CustomerInvoices.IgnoreQueryFilters().AsNoTracking()
                .Where(x => invoiceIds.Contains(x.Id))
                .Select(x => new
                {
                    x.Id, x.InvoiceNumber, x.Subtotal, x.VatAmount, x.TotalAmount, x.AmountPaid, x.Status,
                    Posted = x.JournalEntry != null && x.JournalEntry.IsPosted
                })
                .ToDictionaryAsync(x => x.Id);

            var companyIds = rows.Select(x => x.CompanyId).Distinct().ToList();
            var companies = await _db.Companies.IgnoreQueryFilters().AsNoTracking()
                .Where(x => companyIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);

            return rows.Select(x =>
            {
                var state = new State
                {
                    RequestId = x.Id,
                    RequestNumber = x.RequestNumber,
                    RequestStatus = x.Status,
                    SubmittedAt = x.SubmittedAt,
                    Tenant = x.Tenant,
                    Unit = x.Unit,
                    HasLease = x.LeaseId.HasValue,
                    Name = x.ServiceName ?? string.Empty,
                    Kind = x.ServiceKind,
                    PayFirst = x.ServicePayFirst,
                    ExpectedCost = x.ServiceExpectedCost ?? 0m,
                    ActualCost = x.Cost
                };

                if (x.ServiceInvoiceId.HasValue && invoices.TryGetValue(x.ServiceInvoiceId.Value, out var invoice) && invoice.Status != "Cancelled")
                {
                    state.InvoiceId = invoice.Id;
                    state.InvoiceNumber = invoice.InvoiceNumber;
                    state.Net = invoice.Subtotal;
                    state.Vat = invoice.VatAmount;
                    state.Total = invoice.TotalAmount;
                    state.Paid = invoice.AmountPaid;
                    state.Stage = !invoice.Posted ? Stages.InvoiceNotPosted
                        : invoice.AmountPaid >= invoice.TotalAmount ? Stages.Paid
                        : invoice.AmountPaid > 0m ? Stages.PartlyPaid
                        : Stages.AwaitingPayment;
                }
                else
                {
                    var price = PdcService.PriceCharge(companies[x.CompanyId], x.ServicePrice ?? 0m);
                    state.Net = price.Net;
                    state.Vat = price.Vat;
                    state.Total = price.Total;
                }

                return state;
            }).ToList();
        }

        /// <summary>
        /// Why work on a job may not start or be finished yet, or null when it may: a
        /// service that is paid for first waits for its invoice to be paid in full.
        /// </summary>
        public static async Task<string?> WorkHoldAsync(ApplicationDbContext db, TenantServiceRequest request)
        {
            if (!request.ServiceCatalogItemId.HasValue || !request.ServicePayFirst)
                return null;

            if (!request.ServiceInvoiceId.HasValue)
                return $"\"{request.ServiceName}\" is a paid service: issue its invoice and collect the payment before the work starts.";

            var invoice = await db.CustomerInvoices.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Id == request.ServiceInvoiceId.Value)
                .Select(x => new { x.InvoiceNumber, x.TotalAmount, x.AmountPaid, x.Status })
                .FirstOrDefaultAsync();
            if (invoice == null || invoice.Status == "Cancelled")
                return $"\"{request.ServiceName}\" is a paid service and its invoice was cancelled. Issue the invoice again before the work starts.";

            return invoice.AmountPaid >= invoice.TotalAmount
                ? null
                : $"Invoice {invoice.InvoiceNumber} for \"{request.ServiceName}\" is not paid yet " +
                  $"({invoice.AmountPaid:N2} of {invoice.TotalAmount:N2} received). The work starts once it is paid.";
        }

        // =====================================================================
        //  THE INVOICE
        // =====================================================================

        /// <summary>
        /// Raises the tax invoice for a service order, to the tenant, on the contract the
        /// order names. The price is the one on the order unless staff give another (a
        /// service priced on quotation has none until now).
        /// </summary>
        public async Task<(bool Ok, string Message)> IssueInvoiceAsync(int companyId, int requestId, decimal? price, int? userId, string? userName)
        {
            var request = await _db.TenantServiceRequests.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == requestId && x.CompanyId == companyId);
            if (request == null || !request.ServiceCatalogItemId.HasValue)
                return (false, "This request is not an order for a paid service.");
            if (!request.IsOpen)
                return (false, "The request is closed, so no invoice can be issued for it.");
            if (!request.LeaseId.HasValue)
                return (false, "The request names no contract, and the invoice is raised on a contract. Log the order again with the property chosen.");

            if (request.ServiceInvoiceId.HasValue &&
                await _db.CustomerInvoices.IgnoreQueryFilters().AnyAsync(x => x.Id == request.ServiceInvoiceId.Value && x.Status != "Cancelled"))
                return (false, "The invoice for this service has already been issued.");

            var net = Math.Round(price ?? request.ServicePrice ?? 0m, 2);
            if (net <= 0m)
                return (false, "Give the price of the service, before VAT.");

            var service = await _db.ServiceCatalogItems.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.ServiceCatalogItemId.Value);
            var company = await _db.Companies.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == companyId);
            var charge = PdcService.PriceCharge(company, net);
            var today = DateTime.Today;
            var prefix = $"SRV-{today:yyyy}-";
            var used = await _db.CustomerInvoices.IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == companyId && x.InvoiceNumber.StartsWith(prefix));
            var label = request.ServiceName ?? "Paid service";

            var invoice = new CustomerInvoice
            {
                CompanyId = companyId,
                LeaseId = request.LeaseId.Value,
                InvoiceNumber = $"{prefix}{used + 1:D5}",
                InvoiceDate = today,
                DueDate = today,
                DateOfSupply = today,
                ChargeType = label.Length > 60 ? label[..60] : label,
                IncomeAccountId = service?.IncomeAccountId,
                VatCategory = charge.Category,
                VatRate = VatDeterminationService.RateFor(charge.Category),
                Subtotal = charge.Net,
                VatAmount = charge.Vat,
                TotalAmount = charge.Total,
                Status = "Draft"
            };
            _db.CustomerInvoices.Add(invoice);
            await _db.SaveChangesAsync();

            string? notPosted;
            try
            {
                var journal = await _posting.PostCustomerInvoiceAsync(invoice.Id);
                notPosted = await _workflow.PostNewEntryAsync(journal.Id);
            }
            catch (Exception ex)
            {
                // An invoice with no entry would be owed by the tenant and unknown to the accounts.
                _db.ChangeTracker.Clear();
                var orphan = await _db.CustomerInvoices.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == invoice.Id);
                if (orphan != null && orphan.JournalEntryId == null)
                {
                    _db.CustomerInvoices.Remove(orphan);
                    await _db.SaveChangesAsync();
                }
                return (false, $"The invoice could not be posted, so it was not issued. {ex.Message}");
            }

            request = await _db.TenantServiceRequests.IgnoreQueryFilters().FirstAsync(x => x.Id == requestId);
            request.ServiceInvoiceId = invoice.Id;
            request.ServicePrice = charge.Net;
            _db.TenantServiceRequestComments.Add(new TenantServiceRequestComment
            {
                TenantServiceRequestId = request.Id,
                AuthorType = ServiceRequestAuthorTypes.Staff,
                AuthorName = userName ?? "Staff",
                AppUserId = userId,
                Body = $"Invoice {invoice.InvoiceNumber} issued for {label}: {charge.Total:N2} including VAT." +
                       (request.ServicePayFirst ? " The work starts once it is paid." : string.Empty),
                IsInternal = false,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            if (request.TenantId.HasValue)
                await _push.ToTenantAsync(request.TenantId.Value,
                    "Request {0}", new object?[] { request.RequestNumber },
                    "Your invoice for this service is ready. Open it to see the amount.", Array.Empty<object?>(),
                    $"/portal/requests/{request.Id}", $"request-{request.Id}");

            return (true, $"Invoice {invoice.InvoiceNumber} issued to the tenant for {charge.Total:N2}." +
                          (notPosted == null ? string.Empty : $" Its entry is a draft: {notPosted}"));
        }
    }
}
