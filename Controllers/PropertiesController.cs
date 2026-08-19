using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class PropertiesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly CodeGenerationService _codeGenerationService;
        private readonly MasterDataUniquenessService _uniquenessService;

        public PropertiesController(
            ApplicationDbContext db,
            CodeGenerationService codeGenerationService,
            MasterDataUniquenessService uniquenessService)
        {
            _db = db;
            _codeGenerationService = codeGenerationService;
            _uniquenessService = uniquenessService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var properties = await _db.Properties
                .Include(x => x.Company)
                .OrderBy(x => x.Code)
                .ToListAsync();

            return View(properties);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new Property { OwnershipType = "Owned" });
        }

        /// <summary>
        /// The code a new property would receive for the chosen ownership and type.
        /// Preview only - nothing is reserved until the property is saved.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> PreviewCode(string? ownershipType, string? propertyType)
        {
            if (string.IsNullOrWhiteSpace(propertyType))
                return Json(new { code = "Select ownership and type" });

            var code = await _codeGenerationService.PreviewPropertyCodeAsync(ownershipType, propertyType);
            return Json(new { code });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Property model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);

            if (company == null)
            {
                ModelState.AddModelError(string.Empty, "No company record found.");
            }

            // The code is generated, never typed, so the sequence cannot be broken.
            // Any value posted from the form is ignored.
            ModelState.Remove(nameof(model.Code));

            if (string.IsNullOrWhiteSpace(model.PropertyType))
            {
                ModelState.AddModelError(nameof(model.PropertyType),
                    "Select the property type - it determines the property code.");
            }

            await _uniquenessService.ValidatePropertyAsync(ModelState, companyId, model);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            model.Code = await _codeGenerationService.GeneratePropertyCodeAsync(
                model.OwnershipType,
                model.PropertyType);

            model.CompanyId = companyId;
            model.IsActive = true;

            _db.Properties.Add(model);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var property = await _db.Properties.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
            if (property == null)
            {
                return NotFound();
            }

            return View(property);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Property model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var property = await _db.Properties.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
            if (property == null)
            {
                return NotFound();
            }

            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
            {
                ModelState.AddModelError(string.Empty, "No company record found.");
            }

            // The code is issued by the system and fixed once units and contracts hang
            // off it, so it is never taken from the form.
            ModelState.Remove(nameof(model.Code));

            await _uniquenessService.ValidatePropertyAsync(ModelState, companyId, model, id);

            if (!ModelState.IsValid)
            {
                model.Code = property.Code;
                model.CompanyId = property.CompanyId;
                return View(model);
            }

            property.Name = model.Name;
            property.Address = model.Address;
            property.PropertyType = model.PropertyType;
            property.OwnershipType = model.OwnershipType;
            property.OwnerName = model.OwnerName;
            property.AssetValue = model.AssetValue;
            property.CommissionPercentage = model.CommissionPercentage;
            property.IsActive = model.IsActive;

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
