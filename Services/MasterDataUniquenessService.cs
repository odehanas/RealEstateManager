using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Guards the identity of master data records.
    ///
    /// A tenant or a vendor is a real-world party, and a party has legal identifiers -
    /// a trade licence, an Emirates ID, a passport, a TRN. If the same party is set up
    /// twice under two different codes its balance splits in two, statements are wrong
    /// and the same debt gets chased twice. So each identifier is unique within a
    /// company whenever it is supplied.
    ///
    /// The database carries filtered unique indexes as the last line of defence, but a
    /// raw index violation surfaces as an unhelpful error. These checks run first and
    /// name the record that already holds the identifier, so the user can go and find
    /// it instead of guessing.
    ///
    /// Blanks are not duplicates: an identifier that has not been captured yet is null,
    /// and any number of records may sit in that state.
    /// </summary>
    public class MasterDataUniquenessService
    {
        private readonly ApplicationDbContext _db;

        public MasterDataUniquenessService(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Checks a tenant's identifiers. Pass <paramref name="excludeId"/> when editing
        /// so the record does not clash with itself.
        /// </summary>
        public async Task ValidateTenantAsync(
            ModelStateDictionary modelState, int companyId, Tenant candidate, int? excludeId = null)
        {
            var tradeLicense = Normalise(candidate.TradeLicenseNumber);
            if (tradeLicense != null)
            {
                var holder = await _db.Tenants.AsNoTracking()
                    .Where(x => x.CompanyId == companyId
                                && x.Id != (excludeId ?? 0)
                                && x.TradeLicenseNumber != null
                                && x.TradeLicenseNumber.Trim() == tradeLicense)
                    .Select(x => new Holder { Code = x.Code, Name = x.FullName })
                    .FirstOrDefaultAsync();

                AddDuplicateError(modelState, nameof(Tenant.TradeLicenseNumber),
                    "Trade licence number", tradeLicense, holder);
            }

            var idNumber = Normalise(candidate.IdNumber);
            if (idNumber != null)
            {
                var holder = await _db.Tenants.AsNoTracking()
                    .Where(x => x.CompanyId == companyId
                                && x.Id != (excludeId ?? 0)
                                && x.IdNumber != null
                                && x.IdNumber.Trim() == idNumber)
                    .Select(x => new Holder { Code = x.Code, Name = x.FullName })
                    .FirstOrDefaultAsync();

                AddDuplicateError(modelState, nameof(Tenant.IdNumber),
                    "Emirates ID / ID number", idNumber, holder);
            }

            var passport = Normalise(candidate.PassportNumber);
            if (passport != null)
            {
                var holder = await _db.Tenants.AsNoTracking()
                    .Where(x => x.CompanyId == companyId
                                && x.Id != (excludeId ?? 0)
                                && x.PassportNumber != null
                                && x.PassportNumber.Trim() == passport)
                    .Select(x => new Holder { Code = x.Code, Name = x.FullName })
                    .FirstOrDefaultAsync();

                AddDuplicateError(modelState, nameof(Tenant.PassportNumber),
                    "Passport number", passport, holder);
            }

            var trn = Normalise(candidate.TrnNumber);
            if (trn != null)
            {
                var holder = await _db.Tenants.AsNoTracking()
                    .Where(x => x.CompanyId == companyId
                                && x.Id != (excludeId ?? 0)
                                && x.TrnNumber != null
                                && x.TrnNumber.Trim() == trn)
                    .Select(x => new Holder { Code = x.Code, Name = x.FullName })
                    .FirstOrDefaultAsync();

                AddDuplicateError(modelState, nameof(Tenant.TrnNumber), "TRN", trn, holder);
            }
        }

        public async Task ValidateVendorAsync(
            ModelStateDictionary modelState, int companyId, Vendor candidate, int? excludeId = null)
        {
            var tradeLicense = Normalise(candidate.TradeLicenseNumber);
            if (tradeLicense != null)
            {
                var holder = await _db.Vendors.AsNoTracking()
                    .Where(x => x.CompanyId == companyId
                                && x.Id != (excludeId ?? 0)
                                && x.TradeLicenseNumber != null
                                && x.TradeLicenseNumber.Trim() == tradeLicense)
                    .Select(x => new Holder { Code = x.Code, Name = x.Name })
                    .FirstOrDefaultAsync();

                AddDuplicateError(modelState, nameof(Vendor.TradeLicenseNumber),
                    "Trade licence number", tradeLicense, holder);
            }

            var trn = Normalise(candidate.TrnNumber);
            if (trn != null)
            {
                var holder = await _db.Vendors.AsNoTracking()
                    .Where(x => x.CompanyId == companyId
                                && x.Id != (excludeId ?? 0)
                                && x.TrnNumber != null
                                && x.TrnNumber.Trim() == trn)
                    .Select(x => new Holder { Code = x.Code, Name = x.Name })
                    .FirstOrDefaultAsync();

                AddDuplicateError(modelState, nameof(Vendor.TrnNumber), "TRN", trn, holder);
            }
        }

        /// <summary>
        /// A property name has to be unique too. The code is generated and therefore
        /// always distinct, which means a second "Marina Tower" would slip through and
        /// leave every report showing two buildings that read identically.
        /// </summary>
        public async Task ValidatePropertyAsync(
            ModelStateDictionary modelState, int companyId, Property candidate, int? excludeId = null)
        {
            var name = Normalise(candidate.Name);
            if (name == null)
                return;

            var existing = await _db.Properties.AsNoTracking()
                .Where(x => x.CompanyId == companyId
                            && x.Id != (excludeId ?? 0)
                            && x.Name.Trim() == name)
                .Select(x => new Holder { Code = x.Code, Name = x.Name })
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                modelState.AddModelError(nameof(Property.Name),
                    $"A property named \"{name}\" already exists ({existing.Code}). " +
                    "Use a distinct name so the two can be told apart on reports.");
            }
        }

        private static void AddDuplicateError(
            ModelStateDictionary modelState, string fieldName, string label, string value, Holder? holder)
        {
            if (holder == null)
                return;

            modelState.AddModelError(fieldName,
                $"{label} \"{value}\" is already registered to {holder.Code} - {holder.Name}. " +
                "Each record must carry its own identifiers; edit the existing record instead of creating a second one.");
        }

        private static string? Normalise(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private sealed class Holder
        {
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }
    }
}
