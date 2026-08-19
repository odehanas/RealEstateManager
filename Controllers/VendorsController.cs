using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class VendorsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly CodeGenerationService _codeGenerationService;
        private readonly MasterDataUniquenessService _uniquenessService;

        public VendorsController(
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
            var vendors = await _db.Vendors
                .OrderBy(x => x.Code)
                .ToListAsync();

            return View(vendors);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new Vendor());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Vendor model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
            {
                ModelState.AddModelError(string.Empty, "No company record found.");
            }

            // The code is generated, never typed. Anything posted from the form is ignored.
            ModelState.Remove(nameof(model.Code));

            await _uniquenessService.ValidateVendorAsync(ModelState, companyId, model);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            model.Code = await _codeGenerationService.GenerateVendorCodeAsync();
            model.CompanyId = companyId;
            model.IsActive = true;

            _db.Vendors.Add(model);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var vendor = await _db.Vendors.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
            if (vendor == null)
            {
                return NotFound();
            }

            return View(vendor);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Vendor model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var vendor = await _db.Vendors.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
            if (vendor == null)
            {
                return NotFound();
            }

            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
            {
                ModelState.AddModelError(string.Empty, "No company record found.");
            }

            // The code is issued by the system and fixed once in circulation, so it is
            // never taken from the form.
            ModelState.Remove(nameof(model.Code));

            await _uniquenessService.ValidateVendorAsync(ModelState, companyId, model, id);

            if (!ModelState.IsValid)
            {
                model.Code = vendor.Code;
                model.CompanyId = vendor.CompanyId;
                return View(model);
            }

            vendor.Name = model.Name;
            vendor.ServiceType = model.ServiceType;
            vendor.ContactPerson = model.ContactPerson;
            vendor.Phone = model.Phone;
            vendor.Email = model.Email;
            vendor.TrnNumber = model.TrnNumber;
            vendor.TradeLicenseNumber = model.TradeLicenseNumber;
            vendor.OpeningBalance = model.OpeningBalance;
            vendor.IsActive = model.IsActive;

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
