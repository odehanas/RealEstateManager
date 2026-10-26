using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class TenantsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly CodeGenerationService _codeGenerationService;
        private readonly TenantPortalAuthService _portalAuthService;
        private readonly MasterDataUniquenessService _uniquenessService;
        private readonly MasterDataCreationService _creationService;

        public TenantsController(
            ApplicationDbContext db,
            CodeGenerationService codeGenerationService,
            TenantPortalAuthService portalAuthService,
            MasterDataUniquenessService uniquenessService,
            MasterDataCreationService creationService)
        {
            _db = db;
            _codeGenerationService = codeGenerationService;
            _portalAuthService = portalAuthService;
            _uniquenessService = uniquenessService;
            _creationService = creationService;
        }

        /// <summary>
        /// Issue, reset or suspend a tenant's self-service portal access.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> PortalAccess(int id)
        {
            var model = await BuildPortalAccessModelAsync(id);
            return model == null ? NotFound() : View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IssuePortalAccess(int id, string newUsername)
        {
            var result = await _portalAuthService.IssueAccessAsync(id, newUsername, User.GetAppUserId());

            var model = await BuildPortalAccessModelAsync(id);
            if (model == null)
                return NotFound();

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Could not issue portal access.");
                model.NewUsername = newUsername;
                return View(nameof(PortalAccess), model);
            }

            // Shown once. The password is stored only as a hash, so it cannot be
            // retrieved again - a reset issues a new one.
            model.IssuedPassword = result.TemporaryPassword;
            model.Username = result.Username;
            model.HasAccess = true;
            model.IsActive = true;
            model.MustChangePassword = true;

            return View(nameof(PortalAccess), model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPortalAccessActive(int id, bool isActive)
        {
            await _portalAuthService.SetActiveAsync(id, isActive);
            TempData["SuccessMessage"] = isActive ? "Portal access enabled." : "Portal access suspended.";
            return RedirectToAction(nameof(PortalAccess), new { id });
        }

        private async Task<TenantPortalAccessViewModel?> BuildPortalAccessModelAsync(int tenantId)
        {
            var companyId = User.GetRequiredCurrentCompanyId();

            var tenant = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == tenantId && x.CompanyId == companyId);

            if (tenant == null)
                return null;

            var portalUser = await _portalAuthService.GetByTenantAsync(tenantId);

            return new TenantPortalAccessViewModel
            {
                TenantId = tenant.Id,
                TenantCode = tenant.Code,
                TenantName = tenant.FullName,
                TenantEmail = tenant.Email,
                HasAccess = portalUser != null,
                Username = portalUser?.Username,
                IsActive = portalUser?.IsActive ?? false,
                MustChangePassword = portalUser?.MustChangePassword ?? false,
                LastLoginAt = portalUser?.LastLoginAt,
                LockedOutUntil = portalUser?.LockedOutUntil,
                NewUsername = portalUser?.Username ?? tenant.Email ?? string.Empty
            };
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var tenants = await _db.Tenants
                .Include(x => x.Company)
                .OrderBy(x => x.Code)
                .ToListAsync();

            return View(tenants);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new Tenant());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Tenant model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();

            // The code is generated, never typed. Anything posted from the form is ignored.
            ModelState.Remove(nameof(model.Code));

            await _creationService.ValidateTenantAsync(companyId, model, ModelState);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            await _creationService.CreateTenantAsync(companyId, model);

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
            if (tenant == null)
            {
                return NotFound();
            }

            return View(tenant);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Tenant model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
            if (tenant == null)
            {
                return NotFound();
            }

            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
            {
                ModelState.AddModelError(string.Empty, "No company record found.");
            }

            // The code is issued by the system and never changes - it identifies the
            // tenant on statements and contracts already in circulation.
            ModelState.Remove(nameof(model.Code));

            MasterDataCreationService.NormaliseTenantEInvoicingFields(model, ModelState);
            await _uniquenessService.ValidateTenantAsync(ModelState, companyId, model, id);

            if (!ModelState.IsValid)
            {
                model.Code = tenant.Code;
                model.CompanyId = tenant.CompanyId;
                return View(model);
            }

            tenant.FullName = model.FullName;
            tenant.Email = model.Email;
            tenant.Phone = model.Phone;
            tenant.Nationality = model.Nationality;
            tenant.IdNumber = model.IdNumber;
            tenant.PassportNumber = model.PassportNumber;
            tenant.TradeLicenseNumber = model.TradeLicenseNumber;
            tenant.TrnNumber = model.TrnNumber;
            tenant.Address = model.Address;
            tenant.City = model.City;
            tenant.Emirate = model.Emirate;
            tenant.CountryCode = model.CountryCode;
            tenant.PartyType = model.PartyType;
            tenant.TaxIdentificationNumber = model.TaxIdentificationNumber;
            tenant.LegalRegistrationType = model.LegalRegistrationType;
            tenant.LicensingAuthority = model.LicensingAuthority;
            tenant.PassportIssuingCountry = model.PassportIssuingCountry;
            tenant.OpeningBalance = model.OpeningBalance;
            tenant.IsActive = model.IsActive;

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
