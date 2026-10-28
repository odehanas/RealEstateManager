using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The company's own VAT identity and recovery settings.
    ///
    /// Deliberately separate from Setup > Companies, which is system-administrator
    /// only. A company's accountant needs to maintain their own TRN and address
    /// without needing system-admin rights, and cannot be expected to do it in SQL.
    /// </summary>
    public class VatSettingsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public VatSettingsController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var model = new VatSettingsViewModel
            {
                CompanyName = company.Name,
                LegalName = company.LegalName,
                TrnNumber = company.TrnNumber,
                AddressLine1 = company.AddressLine1,
                AddressLine2 = company.AddressLine2,
                City = company.City,
                Country = company.Country,
                Phone = company.Phone,
                Email = company.Email,
                ProvisionalVatRecoveryRate = company.ProvisionalVatRecoveryRate,
                OwnerCostTreatment = company.OwnerCostTreatment
            };

            await PopulateReadinessAsync(model, companyId);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(VatSettingsViewModel model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            if (model.ProvisionalVatRecoveryRate < 0m || model.ProvisionalVatRecoveryRate > 100m)
            {
                ModelState.AddModelError(nameof(model.ProvisionalVatRecoveryRate),
                    "The recovery rate must be between 0 and 100.");
            }

            if (!ModelState.IsValid)
            {
                model.CompanyName = company.Name;
                await PopulateReadinessAsync(model, companyId);
                return View(model);
            }

            company.LegalName = Trim(model.LegalName);
            company.TrnNumber = Trim(model.TrnNumber);
            company.AddressLine1 = Trim(model.AddressLine1);
            company.AddressLine2 = Trim(model.AddressLine2);
            company.City = Trim(model.City);
            company.Country = Trim(model.Country);
            company.Phone = Trim(model.Phone);
            company.Email = Trim(model.Email);
            company.ProvisionalVatRecoveryRate = model.ProvisionalVatRecoveryRate;
            company.OwnerCostTreatment = RealEstateManager.Domain.OwnerCostTreatments.All.Contains(model.OwnerCostTreatment)
                ? model.OwnerCostTreatment
                : RealEstateManager.Domain.OwnerCostTreatments.Recharge;

            await _db.SaveChangesAsync();

            TempData["VatSettingsMessage"] = "VAT settings saved.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Counts the master data still missing a VAT classification, so the page can
        /// say what remains rather than leaving the user to discover it on an invoice.
        /// </summary>
        private async Task PopulateReadinessAsync(VatSettingsViewModel model, int companyId)
        {
            model.StandardRatedProperties = await _db.Properties
                .CountAsync(x => x.VatCategory == Domain.VatCategories.Standard);

            model.ExemptProperties = await _db.Properties
                .CountAsync(x => x.VatCategory == Domain.VatCategories.Exempt);

            model.TenantsWithTrn = await _db.Tenants.CountAsync(x => x.TrnNumber != null && x.TrnNumber != "");
            model.TotalTenants = await _db.Tenants.CountAsync();

            model.TaxInvoicesIssued = await _db.CustomerInvoices
                .CountAsync(x => x.TaxInvoiceNumber != null);

            model.BillsAwaitingAttribution = await _db.VendorBills
                .CountAsync(x => x.VatAmount > 0m && x.Status == "Draft");
        }

        private static string? Trim(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
