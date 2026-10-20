using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The Capital Assets Scheme: buildings of AED 5 million or more whose recovered input
    /// tax is reviewed each year for ten years against how the building was actually used.
    ///
    /// The taxable use of a year is proposed from the building's leases: each unit's area,
    /// for the days it was let in the year, counts as taxable (standard or zero-rated lease)
    /// or exempt (exempt lease). Vacant days are left out. The proposal can be overridden.
    ///
    ///   adjustment = total input tax x (taxable use this year - taxable use at recovery) / 10
    ///
    ///   more tax recoverable   Dr 11300 VAT Recoverable   Cr 61200 Irrecoverable VAT - CAS
    ///   tax to pay back        Dr 61200                   Cr 11300 VAT Recoverable
    /// </summary>
    public class CapitalAssetVatService
    {
        public const string SourceType = "CapitalAssetVatAdjustment";

        private readonly ApplicationDbContext _db;
        private readonly ICurrentCompanyAccessor _currentCompany;
        private readonly AccountingPostingService _posting;
        private readonly JournalWorkflowService _workflow;

        public CapitalAssetVatService(
            ApplicationDbContext db,
            ICurrentCompanyAccessor currentCompany,
            AccountingPostingService posting,
            JournalWorkflowService workflow)
        {
            _db = db;
            _currentCompany = currentCompany;
            _posting = posting;
            _workflow = workflow;
        }

        private int CompanyId => _currentCompany.CompanyId
            ?? throw new InvalidOperationException("No company is selected for the current user.");

        public async Task<List<CapitalAssetVatRecord>> GetRecordsAsync()
        {
            return await _db.CapitalAssetVatRecords.AsNoTracking()
                .Include(x => x.Property)
                .Include(x => x.DevelopmentProject)
                .Include(x => x.Adjustments).ThenInclude(x => x.JournalEntry)
                .OrderBy(x => x.Name)
                .ToListAsync();
        }

        public async Task<CapitalAssetVatRecord?> GetAsync(int id)
        {
            return await _db.CapitalAssetVatRecords.AsNoTracking()
                .Include(x => x.Property)
                .Include(x => x.DevelopmentProject)
                .Include(x => x.Adjustments).ThenInclude(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        /// <summary>Completed projects kept as rental or own-use buildings, of AED 5 million or more, not yet registered.</summary>
        public async Task<List<CapitalAssetCandidate>> GetProjectCandidatesAsync()
        {
            var registered = await _db.CapitalAssetVatRecords.AsNoTracking()
                .Where(x => x.DevelopmentProjectId != null)
                .Select(x => x.DevelopmentProjectId!.Value)
                .ToListAsync();

            var projects = await _db.DevelopmentProjects.AsNoTracking()
                .Where(x => x.Status == DevelopmentProjectStatuses.Completed && x.PropertyId != null &&
                            x.IntendedUse != DevelopmentIntendedUses.Sale && !registered.Contains(x.Id))
                .ToListAsync();

            var result = new List<CapitalAssetCandidate>();
            foreach (var project in projects)
            {
                var figures = await ProjectFiguresAsync(project);
                if (figures.Value >= CapitalAssetVatRecord.Threshold)
                    result.Add(figures);
            }
            return result;
        }

        private async Task<CapitalAssetCandidate> ProjectFiguresAsync(DevelopmentProject project)
        {
            var value = await _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.DevelopmentProjectId == project.Id && x.AccountId == project.WipAccountId &&
                            x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.JournalEntry.SourceType != DevelopmentTransferService.SourceType)
                .SumAsync(x => (decimal?)(x.Debit - x.Credit)) ?? 0m;

            var vat = await _db.VendorBillLines.AsNoTracking()
                .Where(x => x.DevelopmentProjectId == project.Id && x.VendorBill != null &&
                            x.VendorBill.JournalEntry != null && x.VendorBill.JournalEntry.IsPosted)
                .GroupBy(x => 1)
                .Select(g => new { Input = g.Sum(x => x.VatAmount), Recovered = g.Sum(x => x.RecoverableVatAmount) })
                .FirstOrDefaultAsync();

            return new CapitalAssetCandidate(project.Id, project.PropertyId!.Value, $"{project.Code} {project.Name}",
                value, vat?.Input ?? 0m, vat?.Recovered ?? 0m, project.CompletionDate ?? DateTime.Today);
        }

        public async Task<CapitalAssetVatRecord> RegisterProjectAsync(int projectId)
        {
            var candidate = (await GetProjectCandidatesAsync()).FirstOrDefault(x => x.ProjectId == projectId)
                            ?? throw new InvalidOperationException("That project is not a completed rental or own-use building of AED 5 million or more, or it is already registered.");

            return await SaveRecordAsync(candidate.Name, candidate.PropertyId, candidate.ProjectId, candidate.Value,
                candidate.InputVat, candidate.RecoveredVat, candidate.StartDate, "Registered from the development project's posted bills.");
        }

        /// <summary>A building bought or built outside the system: the figures are entered by hand.</summary>
        public async Task<CapitalAssetVatRecord> RegisterManualAsync(
            string name, int propertyId, decimal value, decimal inputVat, decimal recoveredVat, DateTime startDate, string? notes)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Name the asset.");
            if (value < CapitalAssetVatRecord.Threshold)
                throw new InvalidOperationException($"The scheme covers assets of AED {CapitalAssetVatRecord.Threshold:N0} or more before VAT.");
            if (inputVat <= 0m || recoveredVat < 0m || recoveredVat > inputVat)
                throw new InvalidOperationException("Enter the input tax charged and the part recovered (no more than the tax charged).");
            if (!await _db.Properties.AnyAsync(x => x.Id == propertyId))
                throw new InvalidOperationException("Choose the property.");

            return await SaveRecordAsync(name.Trim(), propertyId, null, value, inputVat, recoveredVat, startDate, notes);
        }

        private async Task<CapitalAssetVatRecord> SaveRecordAsync(
            string name, int propertyId, int? projectId, decimal value, decimal inputVat, decimal recoveredVat, DateTime startDate, string? notes)
        {
            var record = new CapitalAssetVatRecord
            {
                CompanyId = CompanyId,
                Name = name,
                PropertyId = propertyId,
                DevelopmentProjectId = projectId,
                AssetValue = value,
                InputVat = inputVat,
                RecoveredVat = recoveredVat,
                OriginalRecoveryPercent = inputVat == 0m ? 100m : Math.Round(recoveredVat / inputVat * 100m, 4),
                StartDate = startDate.Date,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            _db.CapitalAssetVatRecords.Add(record);
            await _db.SaveChangesAsync();
            return record;
        }

        /// <summary>The year's dates: year 1 starts on the record's start date.</summary>
        public static (DateTime Start, DateTime End) YearPeriod(CapitalAssetVatRecord record, int year) =>
            (record.StartDate.AddYears(year - 1), record.StartDate.AddYears(year).AddDays(-1));

        /// <summary>
        /// Taxable use of the building in a year, from its leases, weighted by unit area and days let.
        /// Returns null when nothing was let in the year.
        /// </summary>
        public async Task<(decimal? Percent, string Explanation)> ProposeTaxableUseAsync(int recordId, int year)
        {
            var record = await _db.CapitalAssetVatRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == recordId)
                         ?? throw new InvalidOperationException("The record was not found.");
            var (start, end) = YearPeriod(record, year);

            var leases = await _db.Leases.AsNoTracking()
                .Include(x => x.Unit)
                .Where(x => x.Unit != null && x.Unit.PropertyId == record.PropertyId &&
                            x.Status != LeaseStatuses.Draft && x.Status != LeaseStatuses.PendingApproval &&
                            x.StartDate <= end && x.EndDate >= start)
                .ToListAsync();

            decimal taxable = 0m, exempt = 0m;
            foreach (var lease in leases)
            {
                var from = lease.StartDate > start ? lease.StartDate : start;
                var to = lease.EndDate < end ? lease.EndDate : end;
                var weight = ((to - from).Days + 1) * lease.Unit!.AreaSqFt;
                if (VatCategories.IsTaxable(lease.VatCategory) || lease.VatCategory == VatCategories.ZeroRated)
                    taxable += weight;
                else
                    exempt += weight;
            }

            if (taxable + exempt == 0m)
                return (null, $"Nothing was let between {start:dd/MM/yyyy} and {end:dd/MM/yyyy}; enter the taxable use yourself.");

            var percent = Math.Round(taxable / (taxable + exempt) * 100m, 2);
            return (percent, $"From {leases.Count} lease(s) between {start:dd/MM/yyyy} and {end:dd/MM/yyyy}, weighted by unit area and days let: " +
                             $"{percent:0.##}% taxable (standard or zero rated), {100m - percent:0.##}% exempt.");
        }

        public static decimal CalculateAdjustment(CapitalAssetVatRecord record, decimal taxableUsePercent) =>
            VatDeterminationService.Round(record.InputVat * (taxableUsePercent - record.OriginalRecoveryPercent) / 100m / CapitalAssetVatRecord.AdjustmentYears);

        public async Task<CapitalAssetVatAdjustment> PostAdjustmentAsync(
            int recordId, int year, decimal taxableUsePercent, DateTime adjustmentDate, string? notes)
        {
            var record = await _db.CapitalAssetVatRecords.Include(x => x.Adjustments).FirstOrDefaultAsync(x => x.Id == recordId)
                         ?? throw new InvalidOperationException("The record was not found.");

            if (year < 1 || year > CapitalAssetVatRecord.AdjustmentYears)
                throw new InvalidOperationException("The adjustment period is ten years: choose year 1 to 10.");
            if (record.Adjustments.Any(x => x.YearNumber == year))
                throw new InvalidOperationException($"Year {year} has already been adjusted.");
            if (taxableUsePercent < 0m || taxableUsePercent > 100m)
                throw new InvalidOperationException("Taxable use is a percentage from 0 to 100.");

            var (start, end) = YearPeriod(record, year);
            if (end > DateTime.Today)
                throw new InvalidOperationException($"Year {year} runs to {end:dd/MM/yyyy}; adjust it once the year is over.");
            if (adjustmentDate.Date <= end)
                throw new InvalidOperationException("Date the adjustment after the end of the year it covers - it goes in the VAT return after that year.");

            var amount = CalculateAdjustment(record, taxableUsePercent);
            var adjustment = new CapitalAssetVatAdjustment
            {
                YearNumber = year,
                PeriodStart = start,
                PeriodEnd = end,
                TaxableUsePercent = taxableUsePercent,
                Adjustment = amount,
                AdjustmentDate = adjustmentDate.Date,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            record.Adjustments.Add(adjustment);
            await _db.SaveChangesAsync();

            if (amount != 0m)
            {
                var accounts = await _db.Accounts.AsNoTracking()
                    .Where(x => x.Code == "11300" || x.Code == "61200")
                    .ToDictionaryAsync(x => x.Code, x => x.Id);
                if (accounts.Count < 2)
                    throw new InvalidOperationException("Accounts 11300 and 61200 are needed. Restart the application once so they are added.");

                var abs = Math.Abs(amount);
                var lines = amount > 0m
                    ? new List<JournalEntryLine>
                    {
                        new() { AccountId = accounts["11300"], Debit = abs, Description = $"Capital Assets Scheme year {year} - more input tax recoverable", PropertyId = record.PropertyId },
                        new() { AccountId = accounts["61200"], Credit = abs, Description = $"{record.Name} - year {year} adjustment", PropertyId = record.PropertyId }
                    }
                    : new List<JournalEntryLine>
                    {
                        new() { AccountId = accounts["61200"], Debit = abs, Description = $"{record.Name} - year {year} input tax repaid", PropertyId = record.PropertyId },
                        new() { AccountId = accounts["11300"], Credit = abs, Description = $"Capital Assets Scheme year {year} - input tax repaid", PropertyId = record.PropertyId }
                    };

                var journal = await _posting.CreatePreparedJournalAsync(record.CompanyId, adjustment.AdjustmentDate,
                    $"Capital Assets Scheme - {record.Name}, year {year}", SourceType, adjustment.Id, lines, $"CAS-{record.Id}-{year}");
                adjustment.JournalEntryId = journal.Id;
                await _db.SaveChangesAsync();

                var warning = await _workflow.PostNewEntryAsync(journal.Id);
                if (warning != null)
                    throw new InvalidOperationException($"The adjustment was saved but its journal is still a draft: {warning}");
            }

            return adjustment;
        }
    }

    public sealed record CapitalAssetCandidate(
        int ProjectId, int PropertyId, string Name, decimal Value, decimal InputVat, decimal RecoveredVat, DateTime StartDate);
}
