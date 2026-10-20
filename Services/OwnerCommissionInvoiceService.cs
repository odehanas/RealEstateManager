using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Monthly commission tax invoices to the landlords of managed properties.
    ///
    /// Our management commission is a real-estate service supplied to the owner and is
    /// standard rated at 5%, even when the rent it is earned on is exempt residential
    /// rent. At each month end one tax invoice is issued per owner for the commission
    /// recognised up to that date and not yet invoiced; its VAT is taken from the
    /// owner's money (Due to Landlords) and goes to VAT Payable.
    ///
    /// A company that is not VAT registered still gets a commission invoice for the
    /// owner's records, but with no VAT, no tax invoice number and no journal.
    /// </summary>
    public class OwnerCommissionInvoiceService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentCompanyAccessor _currentCompany;
        private readonly AccountingPostingService _posting;
        private readonly JournalWorkflowService _workflow;
        private readonly VatDeterminationService _vat;

        public OwnerCommissionInvoiceService(
            ApplicationDbContext db,
            ICurrentCompanyAccessor currentCompany,
            AccountingPostingService posting,
            JournalWorkflowService workflow,
            VatDeterminationService vat)
        {
            _db = db;
            _currentCompany = currentCompany;
            _posting = posting;
            _workflow = workflow;
            _vat = vat;
        }

        private int CompanyId => _currentCompany.CompanyId
            ?? throw new InvalidOperationException("No company is selected for the current user.");

        /// <summary>The VAT rate on our commission: 5% when the company is VAT registered.</summary>
        public static decimal CommissionVatRate(Company company) =>
            company.IsVatRegistered ? VatDeterminationService.StandardRate : 0m;

        /// <summary>Last day of the month the date falls in.</summary>
        public static DateTime MonthEnd(DateTime date) =>
            new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

        /// <summary>Commission recognised up to the date and not yet invoiced, per owner.</summary>
        public async Task<List<PendingCommission>> GetPendingAsync(DateTime upTo)
        {
            var rows = await LoadUninvoicedAsync(upTo.Date);
            return rows
                .GroupBy(x => OwnerKey(x.OwnerName), StringComparer.OrdinalIgnoreCase)
                .Select(g => new PendingCommission
                {
                    OwnerName = g.Key,
                    Months = g.Select(x => new DateTime(x.RecognitionDate.Year, x.RecognitionDate.Month, 1)).Distinct().Count(),
                    Commission = g.Sum(x => x.Amount)
                })
                .OrderBy(x => x.OwnerName)
                .ToList();
        }

        /// <summary>
        /// Issues one commission tax invoice per owner, dated at the month end, for all
        /// commission recognised up to it that has not been invoiced. Each invoice is
        /// posted straight away.
        /// </summary>
        public async Task<CommissionInvoiceRunResult> IssueAsync(DateTime monthEndDate, string? userName)
        {
            var monthEnd = MonthEnd(monthEndDate.Date);
            var companyId = CompanyId;
            var company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId);
            var rate = CommissionVatRate(company);

            var result = new CommissionInvoiceRunResult { MonthEnd = monthEnd };
            var rows = await LoadUninvoicedAsync(monthEnd);

            foreach (var group in rows.GroupBy(x => OwnerKey(x.OwnerName), StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
            {
                if (string.IsNullOrWhiteSpace(group.First().OwnerName))
                {
                    result.Warnings.Add(
                        "Commission on " + string.Join(", ", group.Select(x => x.PropertyName).Distinct()) +
                        " was not invoiced: the owner's name is not recorded on the property.");
                    continue;
                }

                var commission = group.Sum(x => x.Amount);
                if (commission <= 0m)
                    continue;

                var vatAmount = VatDeterminationService.Round(commission * rate / 100m);
                var first = group.Min(x => x.RecognitionDate);

                var invoice = new OwnerCommissionInvoice
                {
                    CompanyId = companyId,
                    InvoiceNumber = await NextNumberAsync(companyId, monthEnd.Year),
                    TaxInvoiceNumber = vatAmount > 0m
                        ? await _vat.GenerateTaxInvoiceNumberAsync(companyId, monthEnd)
                        : null,
                    OwnerName = group.First().OwnerName!.Trim(),
                    RecipientTrn = group.Select(x => x.OwnerTrn).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim(),
                    InvoiceDate = monthEnd,
                    PeriodStart = new DateTime(first.Year, first.Month, 1),
                    PeriodEnd = monthEnd,
                    Commission = commission,
                    VatRate = rate,
                    VatAmount = vatAmount,
                    TotalAmount = commission + vatAmount,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByName = userName,
                    Lines = group
                        .OrderBy(x => x.PropertyCode).ThenBy(x => x.UnitNumber).ThenBy(x => x.RecognitionDate)
                        .Select(x => new OwnerCommissionInvoiceLine
                        {
                            RevenueRecognitionScheduleId = x.ScheduleId,
                            PropertyId = x.PropertyId,
                            LeaseId = x.LeaseId,
                            RecognitionDate = x.RecognitionDate,
                            Commission = x.Amount,
                            Description = $"Management commission {x.RecognitionDate:MMM yyyy} - {x.PropertyName} / {x.UnitNumber}, contract {x.ContractNumber}"
                        })
                        .ToList()
                };

                int? journalId = null;
                try
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync();

                    _db.OwnerCommissionInvoices.Add(invoice);
                    await _db.SaveChangesAsync();

                    if (vatAmount > 0m)
                        journalId = (await _posting.PostOwnerCommissionVatAsync(invoice.Id)).Id;

                    await transaction.CommitAsync();
                }
                catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
                {
                    // The transaction rolled back; forget what it had added so the next
                    // owner's invoice is not saved with this one's leftovers.
                    foreach (var entry in _db.ChangeTracker.Entries()
                                 .Where(e => e.State == EntityState.Added ||
                                             e.Entity == invoice ||
                                             (e.Entity is OwnerCommissionInvoiceLine l && invoice.Lines.Contains(l)) ||
                                             (e.Entity is JournalEntry j && j.SourceType == "OwnerCommissionInvoice" && j.SourceId == invoice.Id))
                                 .ToList())
                    {
                        entry.State = EntityState.Detached;
                    }
                    result.Warnings.Add($"{invoice.OwnerName}: not invoiced - {ex.GetBaseException().Message}");
                    continue;
                }

                if (journalId.HasValue)
                {
                    var warning = await _workflow.PostNewEntryAsync(journalId.Value);
                    if (warning != null)
                        result.Warnings.Add($"{invoice.InvoiceNumber} for {invoice.OwnerName} was saved but its VAT journal is still a draft: {warning}");
                }

                result.Issued.Add(invoice.InvoiceNumber);
            }

            return result;
        }

        public async Task<List<OwnerCommissionInvoice>> GetInvoicesAsync(string? ownerName = null, int take = 50)
        {
            var query = _db.OwnerCommissionInvoices.AsNoTracking().Include(x => x.JournalEntry).AsQueryable();
            if (!string.IsNullOrWhiteSpace(ownerName))
            {
                var owner = ownerName.Trim();
                query = query.Where(x => x.OwnerName == owner);
            }

            return await query
                .OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.Id)
                .Take(take)
                .ToListAsync();
        }

        public async Task<OwnerCommissionInvoice?> GetAsync(int id)
        {
            return await _db.OwnerCommissionInvoices.AsNoTracking()
                .Include(x => x.Company)
                .Include(x => x.JournalEntry)
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        private static string OwnerKey(string? ownerName) => (ownerName ?? string.Empty).Trim();

        private async Task<List<UninvoicedCommission>> LoadUninvoicedAsync(DateTime upTo)
        {
            return await _db.RevenueRecognitionSchedules.AsNoTracking()
                .Where(x => x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.RecognitionDate <= upTo &&
                            x.Lease != null && x.Lease.Unit != null && x.Lease.Unit.Property != null &&
                            x.Lease.Unit.Property.OwnershipType == "Managed" &&
                            !_db.OwnerCommissionInvoiceLines.Any(l => l.RevenueRecognitionScheduleId == x.Id))
                .Select(x => new UninvoicedCommission
                {
                    ScheduleId = x.Id,
                    RecognitionDate = x.RecognitionDate,
                    Amount = x.Amount,
                    LeaseId = x.LeaseId,
                    ContractNumber = x.Lease!.ContractNumber,
                    UnitNumber = x.Lease.Unit!.UnitNumber,
                    PropertyId = x.Lease.Unit.PropertyId,
                    PropertyCode = x.Lease.Unit.Property!.Code,
                    PropertyName = x.Lease.Unit.Property.Name,
                    OwnerName = x.Lease.Unit.Property.OwnerName,
                    OwnerTrn = x.Lease.Unit.Property.OwnerTrn
                })
                .ToListAsync();
        }

        private async Task<string> NextNumberAsync(int companyId, int year)
        {
            var prefix = $"CI-{year}-";
            var numbers = await _db.OwnerCommissionInvoices.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.InvoiceNumber.StartsWith(prefix))
                .Select(x => x.InvoiceNumber)
                .ToListAsync();

            var next = numbers
                .Select(x => int.TryParse(x[prefix.Length..], out var n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

            return $"{prefix}{next:D4}";
        }

        private sealed class UninvoicedCommission
        {
            public int ScheduleId { get; set; }
            public DateTime RecognitionDate { get; set; }
            public decimal Amount { get; set; }
            public int LeaseId { get; set; }
            public string ContractNumber { get; set; } = string.Empty;
            public string UnitNumber { get; set; } = string.Empty;
            public int PropertyId { get; set; }
            public string PropertyCode { get; set; } = string.Empty;
            public string PropertyName { get; set; } = string.Empty;
            public string? OwnerName { get; set; }
            public string? OwnerTrn { get; set; }
        }
    }

    public class PendingCommission
    {
        public string OwnerName { get; set; } = string.Empty;
        public int Months { get; set; }
        public decimal Commission { get; set; }
    }

    public class CommissionInvoiceRunResult
    {
        public DateTime MonthEnd { get; set; }
        public List<string> Issued { get; } = new();
        public List<string> Warnings { get; } = new();
    }
}
