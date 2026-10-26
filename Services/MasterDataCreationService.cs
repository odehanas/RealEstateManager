using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Creating tenants and vendors: tidying, the duplicate checks and the generated
    /// code, shared by the master data screens and the AI assistant so both apply
    /// exactly the same rules.
    /// </summary>
    public class MasterDataCreationService
    {
        private readonly ApplicationDbContext _db;
        private readonly CodeGenerationService _codeGenerationService;
        private readonly MasterDataUniquenessService _uniquenessService;

        public MasterDataCreationService(
            ApplicationDbContext db,
            CodeGenerationService codeGenerationService,
            MasterDataUniquenessService uniquenessService)
        {
            _db = db;
            _codeGenerationService = codeGenerationService;
            _uniquenessService = uniquenessService;
        }

        /// <summary>Checks a new tenant; any problem lands in <paramref name="modelState"/>.</summary>
        public async Task ValidateTenantAsync(int companyId, Tenant model, ModelStateDictionary modelState, int? excludeId = null)
        {
            if (!await _db.Companies.AnyAsync(x => x.Id == companyId))
                modelState.AddModelError(string.Empty, "No company record found.");

            if (string.IsNullOrWhiteSpace(model.FullName) && !modelState.ContainsKey(nameof(model.FullName)))
                modelState.AddModelError(nameof(model.FullName), "The tenant's name is required.");

            NormaliseTenantEInvoicingFields(model, modelState);
            await _uniquenessService.ValidateTenantAsync(modelState, companyId, model, excludeId);
        }

        /// <summary>Saves a validated new tenant with its generated code.</summary>
        public async Task<Tenant> CreateTenantAsync(int companyId, Tenant model)
        {
            model.Code = await _codeGenerationService.GenerateTenantCodeAsync();
            model.CompanyId = companyId;
            model.IsActive = true;

            _db.Tenants.Add(model);
            await _db.SaveChangesAsync();
            return model;
        }

        /// <summary>Checks a new vendor; any problem lands in <paramref name="modelState"/>.</summary>
        public async Task ValidateVendorAsync(int companyId, Vendor model, ModelStateDictionary modelState, int? excludeId = null)
        {
            if (!await _db.Companies.AnyAsync(x => x.Id == companyId))
                modelState.AddModelError(string.Empty, "No company record found.");

            if (string.IsNullOrWhiteSpace(model.Name) && !modelState.ContainsKey(nameof(model.Name)))
                modelState.AddModelError(nameof(model.Name), "The vendor's name is required.");

            // A registration with no number behind it cannot support an input tax claim,
            // and would let bills charge VAT the supplier may not be entitled to.
            if (model.IsVatRegistered && string.IsNullOrWhiteSpace(model.TrnNumber))
            {
                modelState.AddModelError(nameof(model.TrnNumber),
                    "A VAT registered supplier must have a TRN on file.");
            }

            await _uniquenessService.ValidateVendorAsync(modelState, companyId, model, excludeId);
        }

        /// <summary>Saves a validated new vendor with its generated code.</summary>
        public async Task<Vendor> CreateVendorAsync(int companyId, Vendor model)
        {
            model.Code = await _codeGenerationService.GenerateVendorCodeAsync();
            model.CompanyId = companyId;
            model.IsActive = true;

            _db.Vendors.Add(model);
            await _db.SaveChangesAsync();
            return model;
        }

        /// <summary>
        /// Tidies and checks a tenant's e-invoicing details. Only the format is enforced
        /// here - completeness for a business tenant is reported on the E-Invoicing
        /// Settings page, so a tenant can still be set up before every number is to hand.
        /// </summary>
        public static void NormaliseTenantEInvoicingFields(Tenant model, ModelStateDictionary modelState)
        {
            static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

            model.TrnNumber = Clean(model.TrnNumber)?.Replace(" ", string.Empty);
            model.TaxIdentificationNumber = Clean(model.TaxIdentificationNumber)?.Replace(" ", string.Empty);
            model.LegalRegistrationType = Clean(model.LegalRegistrationType);
            model.LicensingAuthority = Clean(model.LicensingAuthority);
            model.PassportIssuingCountry = Clean(model.PassportIssuingCountry)?.ToUpperInvariant();
            model.City = Clean(model.City);
            model.Emirate = Clean(model.Emirate);
            model.CountryCode = Clean(model.CountryCode)?.ToUpperInvariant() ?? "AE";

            if (!PartyTypes.All.Contains(model.PartyType))
                modelState.AddModelError(nameof(model.PartyType), "Choose whether the tenant is an individual, a business or a government entity.");

            if (model.Emirate != null && !Emirates.IsValid(model.Emirate))
                modelState.AddModelError(nameof(model.Emirate), "Choose one of the seven emirates.");

            if (model.CountryCode.Length != 2 || !model.CountryCode.All(char.IsLetter))
                modelState.AddModelError(nameof(model.CountryCode), "Enter the country as a two-letter code, e.g. AE.");

            if (model.TaxIdentificationNumber != null
                && (model.TaxIdentificationNumber.Length != 10 || !model.TaxIdentificationNumber.All(char.IsDigit)))
                modelState.AddModelError(nameof(model.TaxIdentificationNumber), "A TIN is 10 digits.");

            if (model.LegalRegistrationType != null && !LegalRegistrationTypes.All.Contains(model.LegalRegistrationType))
                modelState.AddModelError(nameof(model.LegalRegistrationType), "Choose how the tenant is identified.");
        }
    }
}
