using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Receiving a payment from a tenant and matching it to their open invoices.
    ///
    /// The money goes first to the invoices the user chose, then to the rest in order of
    /// due date, each taking what it still owes until the money runs out. An invoice left
    /// with a balance has had a payment on account. Every invoice touched gets its own
    /// receipt and journal, and the settlement holds them together as one document.
    /// </summary>
    public class CustomerSettlementService
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _postingService;
        private readonly JournalWorkflowService _journalWorkflowService;

        public CustomerSettlementService(
            ApplicationDbContext db,
            AccountingPostingService postingService,
            JournalWorkflowService journalWorkflowService)
        {
            _db = db;
            _postingService = postingService;
            _journalWorkflowService = journalWorkflowService;
        }

        public sealed record ValidationError(string Field, string Message);

        /// <summary>The part of a payment applied to one invoice.</summary>
        public sealed record Allocation(CustomerInvoice Invoice, decimal Outstanding, decimal Applied)
        {
            public bool IsOnAccount => Applied < Outstanding;
        }

        public sealed record Plan(List<Allocation> Allocations, List<ValidationError> Errors);

        public sealed record CreateResult(CustomerSettlement Settlement, List<string> NotPostedReasons);

        /// <summary>
        /// The tenant's posted invoices that still have a balance, nearest due date first.
        /// A draft invoice is not yet a receivable, so it is not here.
        /// </summary>
        public Task<List<CustomerInvoice>> OpenInvoicesAsync(int tenantId) =>
            _db.CustomerInvoices
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .Where(x => x.Lease != null
                            && x.Lease.TenantId == tenantId
                            && x.TotalAmount > x.AmountPaid
                            && x.Status != "Cancelled"
                            && x.JournalEntry != null
                            && x.JournalEntry.IsPosted)
                .OrderBy(x => x.DueDate)
                .ThenBy(x => x.InvoiceDate)
                .ThenBy(x => x.Id)
                .ToListAsync();

        /// <summary>
        /// Spreads an amount over open invoices: the chosen ones first, then the others,
        /// each group nearest due date first. Returns what is left when every invoice is
        /// paid in full.
        /// </summary>
        public static (List<Allocation> Allocations, decimal Unallocated) Allocate(
            IReadOnlyList<CustomerInvoice> openInvoices, IReadOnlyCollection<int> chosenInvoiceIds, decimal amount)
        {
            var allocations = new List<Allocation>();
            var left = amount;
            var order = openInvoices.Where(x => chosenInvoiceIds.Contains(x.Id))
                .Concat(openInvoices.Where(x => !chosenInvoiceIds.Contains(x.Id)));

            foreach (var invoice in order)
            {
                if (left <= 0m) break;
                var outstanding = invoice.TotalAmount - invoice.AmountPaid;
                var applied = Math.Min(outstanding, left);
                allocations.Add(new Allocation(invoice, outstanding, applied));
                left -= applied;
            }

            return (allocations, left);
        }

        /// <summary>Checks the form and works out which invoices the payment settles.</summary>
        public async Task<Plan> PlanAsync(int companyId, CustomerSettlementCreateViewModel model)
        {
            var errors = new List<ValidationError>();
            var allocations = new List<Allocation>();

            var tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == model.TenantId);
            if (tenant == null)
            {
                errors.Add(new(nameof(model.TenantId), "Choose the tenant who paid."));
                return new Plan(allocations, errors);
            }

            if (model.Amount <= 0m)
                errors.Add(new(nameof(model.Amount), "Receipt amount must be greater than zero."));

            var open = await OpenInvoicesAsync(tenant.Id);
            if (open.Count == 0)
                errors.Add(new(string.Empty,
                    $"{tenant.FullName} has no posted invoice with a balance. Post the invoice from Customer Invoices first."));

            var chosen = model.InvoiceIds.Distinct().ToList();
            if (chosen.Any(id => open.All(x => x.Id != id)))
                errors.Add(new(nameof(model.InvoiceIds),
                    "One of the chosen invoices is no longer open for this tenant. Choose again."));

            if (!string.IsNullOrWhiteSpace(model.ReceiptNumber))
            {
                var number = model.ReceiptNumber.Trim();
                if (await NumberIsUsedAsync(number))
                    errors.Add(new(nameof(model.ReceiptNumber), "Receipt number already exists."));
            }

            if (errors.Count > 0)
                return new Plan(allocations, errors);

            var (planned, unallocated) = Allocate(open, chosen, model.Amount);
            if (unallocated > 0m)
            {
                var owed = open.Sum(x => x.TotalAmount - x.AmountPaid);
                errors.Add(new(nameof(model.Amount),
                    $"The amount is {unallocated:N2} more than the {owed:N2} this tenant owes on posted invoices. " +
                    "Receive up to what is owed, or post the next invoice first."));
            }

            return new Plan(planned, errors);
        }

        /// <summary>
        /// Saves the settlement with one receipt and journal for each invoice it touches,
        /// then posts them. Call <see cref="PlanAsync"/> first and pass its allocations.
        /// Throws if a journal cannot be built, in which case nothing is saved.
        /// </summary>
        public async Task<CreateResult> CreateAsync(
            int companyId, CustomerSettlementCreateViewModel model, IReadOnlyList<Allocation> allocations, string? byName)
        {
            // The settlement, its receipts and their journals succeed or fail together.
            await using var transaction = await _db.Database.BeginTransactionAsync();

            var settlement = new CustomerSettlement
            {
                CompanyId = companyId,
                TenantId = model.TenantId,
                SettlementNumber = string.IsNullOrWhiteSpace(model.ReceiptNumber)
                    ? await GenerateNumberAsync()
                    : model.ReceiptNumber.Trim(),
                SettlementDate = model.ReceiptDate,
                Amount = model.Amount,
                PaymentMethod = model.PaymentMethod,
                Reference = string.IsNullOrWhiteSpace(model.Reference) ? null : model.Reference.Trim(),
                Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
                CreatedAt = DateTime.UtcNow,
                CreatedByName = byName
            };
            _db.CustomerSettlements.Add(settlement);
            await _db.SaveChangesAsync();

            var journalIds = new List<int>();
            for (var i = 0; i < allocations.Count; i++)
            {
                var allocation = allocations[i];
                var receipt = new CustomerReceipt
                {
                    CompanyId = companyId,
                    CustomerInvoiceId = allocation.Invoice.Id,
                    CustomerSettlementId = settlement.Id,
                    // A payment for one invoice keeps the plain number; one spread over
                    // several numbers each part under it.
                    ReceiptNumber = allocations.Count == 1
                        ? settlement.SettlementNumber
                        : $"{settlement.SettlementNumber}/{i + 1}",
                    ReceiptDate = model.ReceiptDate,
                    Amount = allocation.Applied,
                    PaymentMethod = model.PaymentMethod,
                    OutstandingBefore = allocation.Outstanding
                };
                _db.CustomerReceipts.Add(receipt);
                await _db.SaveChangesAsync();

                var journal = await _postingService.PostCustomerReceiptAsync(receipt.Id);
                journalIds.Add(journal.Id);
            }

            await transaction.CommitAsync();

            // Posted through the same checks as Journal Inquiry. A journal one of them
            // refuses stays a draft, and its invoice stays unpaid until it is posted.
            var notPosted = new List<string>();
            foreach (var journalId in journalIds)
            {
                var reason = await _journalWorkflowService.PostNewEntryAsync(journalId);
                if (reason != null && !notPosted.Contains(reason))
                    notPosted.Add(reason);
            }

            return new CreateResult(settlement, notPosted);
        }

        /// <summary>A settlement with everything its document shows.</summary>
        public Task<CustomerSettlement?> LoadAsync(int id) =>
            _db.CustomerSettlements
                .Include(x => x.Company)
                .Include(x => x.Tenant)
                .Include(x => x.Receipts).ThenInclude(x => x.JournalEntry)
                .Include(x => x.Receipts).ThenInclude(x => x.CustomerInvoice)
                    .ThenInclude(x => x!.Lease).ThenInclude(x => x!.Unit).ThenInclude(x => x!.Property)
                .FirstOrDefaultAsync(x => x.Id == id);

        /// <summary>The tenants of the company with what each owes on posted invoices.</summary>
        public async Task<List<SettlementTenantOption>> TenantOptionsAsync()
        {
            var owed = await _db.CustomerInvoices
                .Where(x => x.Lease != null
                            && x.TotalAmount > x.AmountPaid
                            && x.Status != "Cancelled"
                            && x.JournalEntry != null
                            && x.JournalEntry.IsPosted)
                .GroupBy(x => x.Lease!.TenantId)
                .Select(g => new { TenantId = g.Key, Outstanding = g.Sum(x => x.TotalAmount - x.AmountPaid) })
                .ToDictionaryAsync(x => x.TenantId, x => x.Outstanding);

            var tenants = await _db.Tenants
                .OrderBy(x => x.FullName)
                .Select(x => new SettlementTenantOption { Id = x.Id, Code = x.Code, FullName = x.FullName, Phone = x.Phone })
                .ToListAsync();

            foreach (var tenant in tenants)
                tenant.Outstanding = owed.GetValueOrDefault(tenant.Id);

            return tenants;
        }

        private async Task<bool> NumberIsUsedAsync(string number) =>
            await _db.CustomerSettlements.AnyAsync(x => x.SettlementNumber == number)
            || await _db.CustomerReceipts.AnyAsync(x => x.ReceiptNumber == number);

        private async Task<string> GenerateNumberAsync()
        {
            var today = DateTime.Today;
            var next = await _db.CustomerSettlements.CountAsync(x => x.CreatedAt >= today.ToUniversalTime()) + 1;
            while (true)
            {
                var number = $"REC-{today:yyyyMMdd}-{next:D3}";
                if (!await NumberIsUsedAsync(number))
                    return number;
                next++;
            }
        }
    }
}
