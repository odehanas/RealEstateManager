using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Owner-facing tenancy schedule: who is occupying which unit, on what terms, and
    /// what they owe - grouped by the person who owns the property.
    ///
    /// For a MANAGED property the owner is the landlord named on the property record.
    /// For an OWNED property the owner is the company itself, so those rows are grouped
    /// under the company name. That way one report serves both "here is your portfolio,
    /// Mr Landlord" and "here is our own portfolio".
    ///
    /// Vacant units are included deliberately. An owner's first question is usually
    /// about the empty ones, and a schedule that silently omits them hides the problem.
    /// </summary>
    public class OwnerReportService
    {
        private readonly ApplicationDbContext _db;

        public OwnerReportService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<OwnerTenancyScheduleDto> GetTenancyScheduleAsync(
            int companyId,
            DateTime asOfDate,
            string? ownershipType = null,
            string? ownerName = null,
            bool includeVacant = true)
        {
            var company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId);

            var properties = await _db.Properties
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.IsActive)
                .Select(x => new
                {
                    x.Id,
                    x.Code,
                    x.Name,
                    x.Address,
                    x.PropertyType,
                    x.OwnershipType,
                    x.OwnerName,
                    x.CommissionPercentage,
                    x.VatCategory
                })
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(ownershipType))
                properties = properties.Where(x => x.OwnershipType == ownershipType).ToList();

            var propertyIds = properties.Select(x => x.Id).ToList();

            var units = await _db.Units
                .AsNoTracking()
                .Where(x => propertyIds.Contains(x.PropertyId) && x.IsActive)
                .Select(x => new
                {
                    x.Id,
                    x.PropertyId,
                    x.UnitNumber,
                    x.Floor,
                    x.AreaSqFt,
                    x.StandardAnnualRent,
                    x.Status
                })
                .ToListAsync();

            var unitIds = units.Select(x => x.Id).ToList();

            // Contracts live at the as-of date. A lease that ended yesterday is history,
            // not a tenancy, so it does not belong on a current schedule.
            var leases = await _db.Leases
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId
                            && unitIds.Contains(x.UnitId)
                            && x.Status == "Active"
                            && x.StartDate <= asOfDate
                            && x.EndDate >= asOfDate)
                .Select(x => new
                {
                    x.Id,
                    x.UnitId,
                    x.TenantId,
                    x.ContractNumber,
                    x.StartDate,
                    x.EndDate,
                    x.AnnualRent,
                    x.BillingFrequencyMonths,
                    x.VatCategory,
                    x.SecurityDepositAmount,
                    TenantCode = x.Tenant!.Code,
                    TenantName = x.Tenant.FullName,
                    TenantPhone = x.Tenant.Phone,
                    TenantEmail = x.Tenant.Email,
                    TenantTrn = x.Tenant.TrnNumber
                })
                .ToListAsync();

            var leaseIds = leases.Select(x => x.Id).ToList();

            // Money owed per contract, from posted invoices only.
            var invoiceTotals = await _db.CustomerInvoices
                .AsNoTracking()
                .Where(x => leaseIds.Contains(x.LeaseId)
                            && x.JournalEntry != null
                            && x.JournalEntry.IsPosted
                            && x.Status != "Cancelled"
                            && x.InvoiceDate <= asOfDate)
                .GroupBy(x => x.LeaseId)
                .Select(g => new
                {
                    LeaseId = g.Key,
                    Invoiced = g.Sum(x => x.TotalAmount),
                    Collected = g.Sum(x => x.AmountPaid)
                })
                .ToListAsync();

            var depositsHeld = await _db.SecurityDeposits
                .AsNoTracking()
                .Where(x => leaseIds.Contains(x.LeaseId) && x.Status == "Held")
                .GroupBy(x => x.LeaseId)
                .Select(g => new { LeaseId = g.Key, Amount = g.Sum(x => x.Amount) })
                .ToListAsync();

            var rows = new List<OwnerTenancyRowDto>();

            foreach (var property in properties)
            {
                var owner = ResolveOwnerName(property.OwnershipType, property.OwnerName, company.Name);

                if (!string.IsNullOrWhiteSpace(ownerName) &&
                    !string.Equals(owner, ownerName, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var unit in units.Where(u => u.PropertyId == property.Id).OrderBy(u => u.UnitNumber))
                {
                    var lease = leases.FirstOrDefault(l => l.UnitId == unit.Id);

                    if (lease == null && !includeVacant)
                        continue;

                    var totals = lease == null ? null : invoiceTotals.FirstOrDefault(t => t.LeaseId == lease.Id);
                    var deposit = lease == null ? null : depositsHeld.FirstOrDefault(d => d.LeaseId == lease.Id);

                    rows.Add(new OwnerTenancyRowDto
                    {
                        OwnerName = owner,
                        OwnershipType = property.OwnershipType,
                        CommissionPercentage = property.CommissionPercentage,

                        PropertyId = property.Id,
                        PropertyCode = property.Code,
                        PropertyName = property.Name,
                        PropertyAddress = property.Address,
                        PropertyType = property.PropertyType,

                        UnitId = unit.Id,
                        UnitCode = unit.UnitNumber,
                        Floor = unit.Floor,
                        AreaSqFt = unit.AreaSqFt,
                        StandardAnnualRent = unit.StandardAnnualRent,
                        UnitStatus = unit.Status,

                        LeaseId = lease?.Id,
                        ContractNumber = lease?.ContractNumber,
                        StartDate = lease?.StartDate,
                        EndDate = lease?.EndDate,
                        AnnualRent = lease?.AnnualRent ?? 0m,
                        BillingFrequencyMonths = lease?.BillingFrequencyMonths ?? 0,
                        VatCategory = lease?.VatCategory ?? property.VatCategory,

                        TenantCode = lease?.TenantCode,
                        TenantName = lease?.TenantName,
                        TenantPhone = lease?.TenantPhone,
                        TenantEmail = lease?.TenantEmail,
                        TenantTrn = lease?.TenantTrn,

                        DepositHeld = deposit?.Amount ?? 0m,
                        Invoiced = totals?.Invoiced ?? 0m,
                        Collected = totals?.Collected ?? 0m,

                        AsOfDate = asOfDate
                    });
                }
            }

            return new OwnerTenancyScheduleDto
            {
                AsOfDate = asOfDate,
                CompanyName = company.Name,
                OwnershipTypeFilter = ownershipType,
                OwnerNameFilter = ownerName,
                IncludeVacant = includeVacant,
                Rows = rows
                    .OrderBy(x => x.OwnerName)
                    .ThenBy(x => x.PropertyCode)
                    .ThenBy(x => x.UnitCode)
                    .ToList()
            };
        }

        /// <summary>Distinct owners, for the report filter.</summary>
        public async Task<List<string>> GetOwnerNamesAsync(int companyId)
        {
            var company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId);

            var properties = await _db.Properties
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.IsActive)
                .Select(x => new { x.OwnershipType, x.OwnerName })
                .ToListAsync();

            return properties
                .Select(x => ResolveOwnerName(x.OwnershipType, x.OwnerName, company.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }

        /// <summary>
        /// Managed property belongs to its named landlord; owned property belongs to the
        /// company. A managed property with no landlord recorded is flagged rather than
        /// quietly grouped with the company's own.
        /// </summary>
        private static string ResolveOwnerName(string ownershipType, string? ownerName, string companyName)
        {
            if (ownershipType == "Managed")
            {
                return string.IsNullOrWhiteSpace(ownerName)
                    ? "(Managed - owner not recorded)"
                    : ownerName.Trim();
            }

            return companyName;
        }
    }

    // =========================================================================
    //  DTOs
    // =========================================================================

    public class OwnerTenancyRowDto
    {
        public string OwnerName { get; set; } = string.Empty;
        public string OwnershipType { get; set; } = string.Empty;
        public decimal CommissionPercentage { get; set; }

        public int PropertyId { get; set; }
        public string PropertyCode { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string? PropertyAddress { get; set; }
        public string PropertyType { get; set; } = string.Empty;

        public int UnitId { get; set; }
        public string UnitCode { get; set; } = string.Empty;
        public string? Floor { get; set; }
        public decimal AreaSqFt { get; set; }
        public decimal StandardAnnualRent { get; set; }
        public string UnitStatus { get; set; } = string.Empty;

        public int? LeaseId { get; set; }
        public string? ContractNumber { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal AnnualRent { get; set; }
        public int BillingFrequencyMonths { get; set; }
        public string VatCategory { get; set; } = string.Empty;

        public string? TenantCode { get; set; }
        public string? TenantName { get; set; }
        public string? TenantPhone { get; set; }
        public string? TenantEmail { get; set; }
        public string? TenantTrn { get; set; }

        public decimal DepositHeld { get; set; }
        public decimal Invoiced { get; set; }
        public decimal Collected { get; set; }

        public DateTime AsOfDate { get; set; }

        public bool IsOccupied => LeaseId.HasValue;
        public decimal Outstanding => Invoiced - Collected;

        /// <summary>Annual rent forgone while the unit sits empty.</summary>
        public decimal VacancyCost => IsOccupied ? 0m : StandardAnnualRent;

        public int? DaysToExpiry => EndDate.HasValue
            ? (int)(EndDate.Value - AsOfDate).TotalDays
            : null;

        public bool IsExpiringSoon => DaysToExpiry.HasValue && DaysToExpiry.Value <= 90;
    }

    public class OwnerTenancyScheduleDto
    {
        public DateTime AsOfDate { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string? OwnershipTypeFilter { get; set; }
        public string? OwnerNameFilter { get; set; }
        public bool IncludeVacant { get; set; }

        public List<OwnerTenancyRowDto> Rows { get; set; } = new();

        public IEnumerable<IGrouping<string, OwnerTenancyRowDto>> ByOwner =>
            Rows.GroupBy(x => x.OwnerName).OrderBy(g => g.Key);

        public int TotalUnits => Rows.Count;
        public int OccupiedUnits => Rows.Count(x => x.IsOccupied);
        public int VacantUnits => Rows.Count(x => !x.IsOccupied);

        public decimal TotalAnnualRent => Rows.Sum(x => x.AnnualRent);
        public decimal TotalOutstanding => Rows.Sum(x => x.Outstanding);
        public decimal TotalDepositHeld => Rows.Sum(x => x.DepositHeld);
        public decimal TotalVacancyCost => Rows.Sum(x => x.VacancyCost);

        public int ExpiringSoon => Rows.Count(x => x.IsExpiringSoon);

        public decimal OccupancyRate => TotalUnits == 0
            ? 0m
            : Math.Round((decimal)OccupiedUnits / TotalUnits * 100m, 1);
    }
}
