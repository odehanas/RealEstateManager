using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Drafting a new tenancy contract: the checks and the save, shared by the Leases
    /// screen and the AI assistant. A new contract is always a draft; it takes effect
    /// when activated, or when approved if the company requires approval.
    /// </summary>
    public class LeaseCreationService
    {
        private readonly ApplicationDbContext _db;
        private readonly CodeGenerationService _codeGenerationService;
        private readonly LeaseWorkflowService _workflow;
        private readonly RealEstateVatService _realEstateVat;

        public LeaseCreationService(
            ApplicationDbContext db,
            CodeGenerationService codeGenerationService,
            LeaseWorkflowService workflow,
            RealEstateVatService realEstateVat)
        {
            _db = db;
            _codeGenerationService = codeGenerationService;
            _workflow = workflow;
            _realEstateVat = realEstateVat;
        }

        public sealed record ValidationError(string Field, string Message);

        /// <summary>Everything that stops the draft being saved, by form field.</summary>
        public async Task<List<ValidationError>> ValidateAsync(int companyId, LeaseCreateViewModel model)
        {
            var errors = new List<ValidationError>();

            if (!await _db.Companies.AnyAsync(x => x.Id == companyId))
            {
                errors.Add(new(string.Empty, "No company record found."));
            }

            if (!await _db.Units.AnyAsync(x => x.Id == model.UnitId))
            {
                errors.Add(new(nameof(model.UnitId), "Selected unit was not found."));
            }

            if (!await _db.Tenants.AnyAsync(x => x.Id == model.TenantId))
            {
                errors.Add(new(nameof(model.TenantId), "Selected tenant was not found."));
            }

            if (model.StartDate >= model.EndDate)
            {
                errors.Add(new(nameof(model.EndDate), "End date must be later than start date."));
            }

            if (model.BillingFrequencyMonths <= 0 || 12 % model.BillingFrequencyMonths != 0)
            {
                errors.Add(new(nameof(model.BillingFrequencyMonths), "Billing frequency must be 1, 2, 3, 4, 6, or 12."));
            }

            // A contract that is active or awaiting approval holds the unit for its dates.
            var clash = await _workflow.CheckUnitFreeAsync(0, model.UnitId, model.StartDate, model.EndDate);
            if (clash != null)
            {
                errors.Add(new(nameof(model.UnitId), clash));
            }

            // Overriding the derived VAT treatment is allowed, but never silently:
            // a wrong treatment is a tax error, so the reason is on the record.
            if (!string.IsNullOrWhiteSpace(model.VatCategoryOverride) &&
                string.IsNullOrWhiteSpace(model.VatOverrideReason))
            {
                errors.Add(new(nameof(model.VatOverrideReason),
                    "Give a reason when overriding the VAT treatment derived from the unit."));
            }

            return errors;
        }

        /// <summary>The VAT treatment the contract will get unless overridden.</summary>
        public async Task<string> DerivedVatCategoryAsync(int unitId, DateTime startDate) =>
            (await _realEstateVat.DetermineAsync(unitId, startDate)).Category;

        /// <summary>Saves a validated draft contract with its generated contract number.</summary>
        public async Task<Lease> CreateDraftAsync(int companyId, int? createdByUserId, LeaseCreateViewModel model)
        {
            // VAT treatment comes from the unit, falling back to the property, and for a new
            // residential building from whether this is the unit's first supply (zero rated
            // within three years of completion, exempt afterwards). It is never typed in
            // freehand: an override must be a deliberate choice with a reason.
            var derivedCategory = await DerivedVatCategoryAsync(model.UnitId, model.StartDate);
            var effectiveCategory = string.IsNullOrWhiteSpace(model.VatCategoryOverride)
                ? derivedCategory
                : model.VatCategoryOverride!;

            var lease = new Lease
            {
                CompanyId = companyId,
                UnitId = model.UnitId,
                TenantId = model.TenantId,
                ContractNumber = await _codeGenerationService.GenerateContractCodeAsync(
                    model.UnitId, model.TenantId, model.StartDate),
                StartDate = model.StartDate,
                EndDate = model.EndDate,
                AnnualRent = model.AnnualRent,
                BillingFrequencyMonths = model.BillingFrequencyMonths,
                SecurityDepositAmount = model.SecurityDepositAmount,
                // Every contract starts as a draft: no invoices, no effect on the unit.
                // It takes effect when activated, or when approved if the company requires it.
                Status = LeaseStatuses.Draft,
                Notes = model.Notes,
                CreatedAt = DateTime.UtcNow,
                CreatedByAppUserId = createdByUserId
            };

            VatDeterminationService.ApplyToLease(
                lease,
                effectiveCategory,
                effectiveCategory == derivedCategory ? null : model.VatOverrideReason);

            _db.Leases.Add(lease);
            await _db.SaveChangesAsync();
            return lease;
        }
    }
}
