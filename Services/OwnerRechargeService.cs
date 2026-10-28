using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Records costs charged on to the owners of managed properties as the company's own
    /// standard-rated supply: 5% VAT on the cost, a tax invoice in the company's series,
    /// and the cost plus VAT charged to the owner through Due to Landlords.
    /// </summary>
    public class OwnerRechargeService
    {
        private readonly ApplicationDbContext _db;
        private readonly VatDeterminationService _vat;

        public OwnerRechargeService(ApplicationDbContext db, VatDeterminationService vat)
        {
            _db = db;
            _vat = vat;
        }

        /// <summary>The company's choice of recharge or disbursement.</summary>
        public async Task<string> TreatmentAsync(int companyId) =>
            await _db.Companies.AsNoTracking().Where(x => x.Id == companyId).Select(x => x.OwnerCostTreatment).FirstOrDefaultAsync()
            ?? OwnerCostTreatments.Recharge;

        /// <summary>The output VAT on a recharge of this net cost.</summary>
        public static decimal VatOn(decimal net) => VatDeterminationService.CalculateTax(net, VatCategories.Standard);

        /// <summary>Why the property cannot take a recharge, or null when it can.</summary>
        public static string? CannotRecharge(Property? property) =>
            property == null ? "Choose the owner's property the cost is for."
            : property.OwnershipType != "Managed" ? $"{property.Code} is not a managed property; only an owner's property can be charged to its owner."
            : string.IsNullOrWhiteSpace(property.OwnerName) ? $"{property.Code} has no owner recorded."
            : null;

        /// <summary>
        /// Creates or refreshes the recharge for one source document and property. A
        /// recharge keeps its tax invoice number for life, even when its draft is rebuilt.
        /// </summary>
        public async Task<OwnerRecharge> RecordAsync(
            int companyId, string sourceType, int sourceId, string? sourceReference, int propertyId, int? unitId,
            DateTime date, string description, decimal net, int journalEntryId,
            string treatment = OwnerCostTreatments.Recharge, decimal supplierVat = 0m, string? category = null)
        {
            var property = await _db.Properties.AsNoTracking().FirstAsync(x => x.Id == propertyId);
            var recharge = await _db.OwnerRecharges.FirstOrDefaultAsync(x =>
                x.CompanyId == companyId && x.SourceType == sourceType && x.SourceId == sourceId && x.PropertyId == propertyId);
            if (recharge == null)
            {
                recharge = new OwnerRecharge { CompanyId = companyId, SourceType = sourceType, SourceId = sourceId, PropertyId = propertyId };
                _db.OwnerRecharges.Add(recharge);
            }

            var isDisbursement = treatment == OwnerCostTreatments.Disbursement;
            var vat = isDisbursement ? 0m : VatOn(net);
            recharge.SourceReference = sourceReference;
            recharge.UnitId = unitId;
            recharge.OwnerName = property.OwnerName?.Trim() ?? string.Empty;
            recharge.OwnerTrn = string.IsNullOrWhiteSpace(property.OwnerTrn) ? null : property.OwnerTrn.Trim();
            recharge.RechargeDate = date.Date;
            recharge.Description = description.Length > 500 ? description[..500] : description;
            recharge.NetAmount = net;
            recharge.VatRate = VatDeterminationService.StandardRate;
            recharge.VatAmount = vat;
            recharge.Treatment = isDisbursement ? OwnerCostTreatments.Disbursement : OwnerCostTreatments.Recharge;
            recharge.SupplierVatAmount = isDisbursement ? supplierVat : 0m;
            recharge.VatRate = isDisbursement ? 0m : VatDeterminationService.StandardRate;
            recharge.TotalAmount = net + vat + recharge.SupplierVatAmount;
            recharge.CostCategory = category;
            recharge.JournalEntryId = journalEntryId;
            // A disbursement is not our supply: no tax invoice of ours.
            if (!isDisbursement)
                recharge.TaxInvoiceNumber ??= await _vat.GenerateTaxInvoiceNumberAsync(companyId, date);

            await _db.SaveChangesAsync();
            return recharge;
        }

        /// <summary>Drops the recharges of a source whose properties are no longer charged (a draft bill edited).</summary>
        public async Task RemoveOthersAsync(int companyId, string sourceType, int sourceId, IReadOnlyCollection<int> keepPropertyIds)
        {
            var stale = await _db.OwnerRecharges
                .Where(x => x.CompanyId == companyId && x.SourceType == sourceType && x.SourceId == sourceId && !keepPropertyIds.Contains(x.PropertyId))
                .ToListAsync();
            if (stale.Count == 0)
                return;
            _db.OwnerRecharges.RemoveRange(stale);
            await _db.SaveChangesAsync();
        }
    }
}
