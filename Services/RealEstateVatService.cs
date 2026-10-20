using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The UAE VAT treatment of a supply of real estate - a sale or a lease of a unit:
    ///
    ///   Commercial (standard rated unit or property)        standard rated, 5%
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

        public sealed record VatDecision(string Category, string Reason);

        public async Task<VatDecision> DetermineAsync(int unitId, DateTime supplyDate, int? excludeLeaseId = null, int? excludeSaleId = null)
        {
            var unit = await _db.Units.AsNoTracking().Include(x => x.Property).FirstOrDefaultAsync(x => x.Id == unitId)
                       ?? throw new InvalidOperationException("The unit was not found.");
            var property = unit.Property!;

            var category = VatDeterminationService.ResolveCategory(property, unit);
            if (category == VatCategories.Standard)
                return new VatDecision(VatCategories.Standard, "Commercial property - standard rated at 5%.");
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
