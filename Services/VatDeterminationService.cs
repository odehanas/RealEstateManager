using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The single place that decides how a supply is treated for VAT, and the single
    /// place that computes a tax amount.
    ///
    /// Treatment flows down: Property -> Unit -> Lease -> Invoice. Each level may
    /// narrow it, and the level below inherits unless it overrides. The invoice
    /// snapshots whatever applied at the moment it was generated, so a later change
    /// to a property never silently rewrites an issued tax invoice.
    /// </summary>
    public class VatDeterminationService
    {
        /// <summary>Standard rate. Held here so there is one definition, not one per call site.</summary>
        public const decimal StandardRate = 5m;

        private readonly ApplicationDbContext _db;

        public VatDeterminationService(ApplicationDbContext db)
        {
            _db = db;
        }

        // =====================================================================
        //  DETERMINATION
        // =====================================================================

        /// <summary>
        /// The VAT category that applies to a unit: its own if set, otherwise the
        /// property's.
        /// </summary>
        public static string ResolveCategory(Property property, Unit? unit)
        {
            if (unit != null && !string.IsNullOrWhiteSpace(unit.VatCategory))
                return unit.VatCategory!;

            return string.IsNullOrWhiteSpace(property.VatCategory)
                ? VatCategories.FromPropertyType(property.PropertyType)
                : property.VatCategory;
        }

        /// <summary>Loads the unit and property and resolves the category for a unit id.</summary>
        public async Task<string> ResolveCategoryForUnitAsync(int unitId)
        {
            var unit = await _db.Units
                .AsNoTracking()
                .Include(x => x.Property)
                .FirstOrDefaultAsync(x => x.Id == unitId);

            if (unit?.Property == null)
                return VatCategories.Standard;

            return ResolveCategory(unit.Property, unit);
        }

        /// <summary>The rate implied by a category. Only Standard carries tax.</summary>
        public static decimal RateFor(string category) =>
            category == VatCategories.Standard ? StandardRate : 0m;

        /// <summary>
        /// Applies a category to a lease, keeping the legacy IsVatApplicable / VatRate
        /// fields consistent so older screens and reports keep working.
        /// </summary>
        public static void ApplyToLease(Lease lease, string category, string? overrideReason = null)
        {
            lease.VatCategory = category;
            lease.VatRate = RateFor(category);
            lease.IsVatApplicable = VatCategories.IsTaxable(category);
            lease.VatOverrideReason = string.IsNullOrWhiteSpace(overrideReason) ? null : overrideReason.Trim();
        }

        // =====================================================================
        //  CALCULATION
        // =====================================================================

        /// <summary>
        /// Tax on a net amount. Rounds half away from zero, which is ordinary
        /// arithmetic rounding - NOT .NET's default banker's rounding, which would
        /// send a 0.605 midpoint down to 0.60 instead of up to 0.61.
        /// </summary>
        public static decimal CalculateTax(decimal netAmount, string category)
        {
            if (!VatCategories.IsTaxable(category))
                return 0m;

            return Round(netAmount * StandardRate / 100m);
        }

        /// <summary>Rounds to 2 decimals, half away from zero.</summary>
        public static decimal Round(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        // =====================================================================
        //  TAX INVOICE
        // =====================================================================

        /// <summary>
        /// Which document the tenant should receive. An exempt supply is not a tax
        /// invoice at all. A zero-rated supply IS a taxable supply - at 0% - and needs a
        /// tax invoice like a standard-rated one. A registered recipient gets the full layout.
        /// </summary>
        public static string ResolveInvoiceType(string category, string? recipientTrn)
        {
            if (!VatCategories.IsTaxable(category) && category != VatCategories.ZeroRated)
                return TaxInvoiceTypes.NotApplicable;

            return string.IsNullOrWhiteSpace(recipientTrn)
                ? TaxInvoiceTypes.Simplified
                : TaxInvoiceTypes.Full;
        }

        /// <summary>
        /// Next number in the company's single sequential tax invoice series.
        /// Article 59 requires sequence, so this is deliberately one series per
        /// company rather than one per lease.
        /// </summary>
        public async Task<string> GenerateTaxInvoiceNumberAsync(int companyId, DateTime invoiceDate)
        {
            var prefix = $"TX-{invoiceDate:yyyy}-";

            var last = await _db.CustomerInvoices
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId
                            && x.TaxInvoiceNumber != null
                            && x.TaxInvoiceNumber.StartsWith(prefix))
                .OrderByDescending(x => x.TaxInvoiceNumber)
                .Select(x => x.TaxInvoiceNumber)
                .FirstOrDefaultAsync();

            // One series for the company: commission invoices to landlords are tax invoices
            // too, and draw from the same sequence as tenant invoices.
            var lastCommission = await _db.OwnerCommissionInvoices
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId
                            && x.TaxInvoiceNumber != null
                            && x.TaxInvoiceNumber.StartsWith(prefix))
                .OrderByDescending(x => x.TaxInvoiceNumber)
                .Select(x => x.TaxInvoiceNumber)
                .FirstOrDefaultAsync();

            var next = 1;
            foreach (var number in new[] { last, lastCommission })
            {
                if (!string.IsNullOrEmpty(number) && int.TryParse(number[prefix.Length..], out var parsed))
                    next = Math.Max(next, parsed + 1);
            }

            return $"{prefix}{next:D6}";
        }

        // =====================================================================
        //  INPUT TAX
        // =====================================================================

        /// <summary>
        /// Splits input tax into the recoverable and irrecoverable portions.
        ///
        ///   Taxable  - fully recoverable
        ///   Exempt   - nothing recoverable; the tax becomes part of the cost
        ///   Residual - apportioned at the provisional recovery rate, corrected later
        ///              by the annual wash-up
        /// </summary>
        public static (decimal Recoverable, decimal NonRecoverable, decimal AppliedRate) SplitInputTax(
            decimal vatAmount,
            string attribution,
            decimal provisionalRecoveryRate)
        {
            if (vatAmount <= 0m)
                return (0m, 0m, 100m);

            switch (attribution)
            {
                case InputTaxAttributions.Taxable:
                    return (vatAmount, 0m, 100m);

                case InputTaxAttributions.Exempt:
                    return (0m, vatAmount, 0m);

                case InputTaxAttributions.Residual:
                    var rate = Math.Clamp(provisionalRecoveryRate, 0m, 100m);
                    var recoverable = Round(vatAmount * rate / 100m);
                    return (recoverable, vatAmount - recoverable, rate);

                default:
                    // Unknown attribution is treated as the safest option: recover nothing.
                    return (0m, vatAmount, 0m);
            }
        }
    }
}
