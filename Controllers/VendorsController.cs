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
        private readonly MasterDataCreationService _creationService;

        public VendorsController(
            ApplicationDbContext db,
            CodeGenerationService codeGenerationService,
            MasterDataUniquenessService uniquenessService,
            MasterDataCreationService creationService)
        {
            _db = db;
            _codeGenerationService = codeGenerationService;
            _uniquenessService = uniquenessService;
            _creationService = creationService;
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

            // The code is generated, never typed. Anything posted from the form is ignored.
            ModelState.Remove(nameof(model.Code));

            await _creationService.ValidateVendorAsync(companyId, model, ModelState);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            await _creationService.CreateVendorAsync(companyId, model);

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

            // A registration with no number behind it cannot support an input tax claim,
            // and would let bills charge VAT the supplier may not be entitled to.
            if (model.IsVatRegistered && string.IsNullOrWhiteSpace(model.TrnNumber))
            {
                ModelState.AddModelError(nameof(model.TrnNumber),
                    "A VAT registered supplier must have a TRN on file.");
            }

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
            vendor.TaxIdentificationNumber = string.IsNullOrWhiteSpace(model.TaxIdentificationNumber)
                ? null
                : model.TaxIdentificationNumber.Trim();
            vendor.IsVatRegistered = model.IsVatRegistered;
            vendor.OpeningBalance = model.OpeningBalance;
            vendor.IsActive = model.IsActive;

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
