using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The UAE VAT treatment of a supply of real estate - a sale or a lease of a unit:
    ///
    ///   Commercial (standard rated unit or property)        standard rated, 5%
    ///   Residential, let for LESS THAN SIX MONTHS            standard rated, 5%
    ///     (daily, weekly, monthly: a short stay is a taxable
    ///     supply, not the exempt letting of a home)
    ///   Residential, the unit's FIRST supply, made within    zero rated
    ///     three years of the building's completion
    ///     (or before completion, off plan)
    ///   Residential, any later supply                         exempt
    ///
    /// "New" means the building has a completion date recorded, or is being built as a
    /// development project. A residential building with neither is treated as existing, so
    /// its supplies are exempt - record the completion date to claim zero rating.
    /// A unit or property deliberately set to zero rated keeps that treatment.
    /// </summary>
    public class RealEstateVatService
    {
        private readonly ApplicationDbContext _db;

        public RealEstateVatService(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <param name="ShortTerm">A residential let of less than six months - a taxable supply.</param>
        /// <param name="Warning">Something the person saving the contract must be told, e.g. that the owner may have to register.</param>
        public sealed record VatDecision(string Category, string Reason, bool ShortTerm = false, string? Warning = null);

        /// <summary>The length below which a residential let is a taxable supply.</summary>
        public const int ShortTermMonths = 6;

        /// <summary>A person making taxable supplies above this in twelve months must register for VAT.</summary>
        public const decimal MandatoryRegistrationThreshold = 375_000m;

        /// <summary>
        /// Whether a contract runs for less than six months. 1 January to 30 June is six
        /// months and is not short; 1 January to 29 June is.
        /// </summary>
        public static bool IsShortTerm(DateTime startDate, DateTime endDate) =>
            endDate.Date < startDate.Date.AddMonths(ShortTermMonths).AddDays(-1);

        /// <param name="leaseEndDate">For a lease: its end date, so a let of less than six months is recognised as taxable. Leave empty for a sale.</param>
        public async Task<VatDecision> DetermineAsync(int unitId, DateTime supplyDate, int? excludeLeaseId = null, int? excludeSaleId = null, DateTime? leaseEndDate = null)
        {
            var unit = await _db.Units.AsNoTracking().Include(x => x.Property).FirstOrDefaultAsync(x => x.Id == unitId)
                       ?? throw new InvalidOperationException("The unit was not found.");
            var property = unit.Property!;

            var category = VatDeterminationService.ResolveCategory(property, unit);
            var shortTerm = leaseEndDate.HasValue && category != VatCategories.Standard && IsShortTerm(supplyDate, leaseEndDate.Value);

            // Invoiced in the name of an owner who is not registered for VAT: no VAT can
            // be charged on the rent at all.
            if (property.BillsInOwnerName && string.IsNullOrWhiteSpace(property.OwnerTrn))
            {
                // A short let is still a taxable supply of the owner's: it counts towards
                // the turnover at which they must register.
                if (shortTerm)
                    return new VatDecision(VatCategories.Exempt,
                        $"Short-term residential let (less than {ShortTermMonths} months) - a taxable supply, but it is invoiced in the owner's name and {property.OwnerName} is not registered for VAT, so no VAT is charged.",
                        ShortTerm: true,
                        Warning: $"Short-term rent is a taxable supply of {property.OwnerName}. It counts towards the AED {MandatoryRegistrationThreshold:N0} of taxable supplies in twelve months at which the owner must register for VAT; once registered, record the TRN on the property and VAT is charged at 5%.");

                return new VatDecision(VatCategories.Exempt,
                    $"Invoiced in the owner's name, and {property.OwnerName} is not registered for VAT - no VAT is charged.");
            }

            if (category == VatCategories.Standard)
                return new VatDecision(VatCategories.Standard, "Commercial property - standard rated at 5%.");

            // A stay of less than six months is taxed whatever the building: this comes
            // before the zero rating of a first supply and the exemption of a home.
            if (shortTerm)
                return new VatDecision(VatCategories.Standard,
                    $"Short-term residential let (less than {ShortTermMonths} months) - a taxable supply, standard rated at 5%.",
                    ShortTerm: true);

            if (category == VatCategories.ZeroRated)
                return new VatDecision(VatCategories.ZeroRated, "Zero rated as set on the unit or property.");

            // Residential from here on.
            var isNewBuilding = property.CompletionDate.HasValue ||
                                await _db.DevelopmentProjects.AnyAsync(x => x.PropertyId == property.Id &&
                                                                            x.Status != DevelopmentProjectStatuses.Cancelled);
            if (!isNewBuilding)
                return new VatDecision(VatCategories.Exempt,
                    "Residential - exempt. (Zero rating applies only to the first supply of a new building: record its completion date if this is one.)");

            var prior = await PriorSupplyAsync(unit, excludeLeaseId, excludeSaleId);
            if (prior != null)
                return new VatDecision(VatCategories.Exempt, $"Residential - exempt: the unit's first supply was {prior}.");

            if (property.CompletionDate.HasValue && supplyDate.Date > property.ZeroRatingWindowEnds!.Value)
                return new VatDecision(VatCategories.Exempt,
                    $"Residential - exempt: more than three years after completion on {property.CompletionDate:dd/MM/yyyy}.");

            return new VatDecision(VatCategories.ZeroRated, property.CompletionDate.HasValue
                ? $"First supply of a new residential unit, within three years of completion on {property.CompletionDate:dd/MM/yyyy} - zero rated."
                : "First supply of a new residential unit, before completion (off plan) - zero rated.");
        }

        /// <summary>Records the unit's first supply, if none is recorded yet.</summary>
        public static void RecordFirstSupply(Unit unit, DateTime date, string type, string reference)
        {
            if (unit.FirstSupplyDate != null)
                return;

            unit.FirstSupplyDate = date.Date;
            unit.FirstSupplyType = type;
            unit.FirstSupplyReference = reference;
        }

        /// <summary>A description of an earlier supply of the unit, or null when there has been none.</summary>
        private async Task<string?> PriorSupplyAsync(Unit unit, int? excludeLeaseId, int? excludeSaleId)
        {
            if (unit.FirstSupplyDate.HasValue)
            {
                var isExcluded = unit.FirstSupplyType == "Lease" && excludeLeaseId.HasValue &&
                                 await _db.Leases.AnyAsync(x => x.Id == excludeLeaseId && x.ContractNumber == unit.FirstSupplyReference)
                                 || unit.FirstSupplyType == "Sale" && excludeSaleId.HasValue &&
                                 await _db.UnitSales.AnyAsync(x => x.Id == excludeSaleId && x.ContractNumber == unit.FirstSupplyReference);
                if (!isExcluded)
                    return $"{unit.FirstSupplyType?.ToLowerInvariant()} {unit.FirstSupplyReference} on {unit.FirstSupplyDate:dd/MM/yyyy}";
            }

            var lease = await _db.Leases.AsNoTracking()
                .Where(x => x.UnitId == unit.Id && x.Id != (excludeLeaseId ?? 0) &&
                            x.Status != LeaseStatuses.Draft && x.Status != LeaseStatuses.PendingApproval)
                .OrderBy(x => x.StartDate)
                .Select(x => new { x.ContractNumber, x.StartDate })
                .FirstOrDefaultAsync();
            if (lease != null)
                return $"lease {lease.ContractNumber} from {lease.StartDate:dd/MM/yyyy}";

            var sale = await _db.UnitSales.AsNoTracking()
                .Where(x => x.UnitId == unit.Id && x.Id != (excludeSaleId ?? 0) && x.Status != UnitSaleStatuses.Cancelled)
                .OrderBy(x => x.ContractDate)
                .Select(x => new { x.ContractNumber, x.ContractDate })
                .FirstOrDefaultAsync();
            return sale == null ? null : $"sale {sale.ContractNumber} on {sale.ContractDate:dd/MM/yyyy}";
        }
    }
}
