using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class UnitsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly CodeGenerationService _codeGenerationService;

        public UnitsController(ApplicationDbContext db, CodeGenerationService codeGenerationService)
        {
            _db = db;
            _codeGenerationService = codeGenerationService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var today = DateTime.Today;

            var properties = await _db.Properties
                .Include(x => x.Units)
                    .ThenInclude(x => x.Leases)
                        .ThenInclude(x => x.Tenant)
                .OrderBy(x => x.Code)
                .ToListAsync();

            var model = properties.Select(property => new PropertyUnitsHierarchyViewModel
            {
                PropertyId = property.Id,
                PropertyCode = property.Code,
                PropertyName = property.Name,
                PropertyType = property.PropertyType,
                OwnershipType = property.OwnershipType,
                OwnerName = property.OwnerName,
                Units = property.Units
                    .OrderBy(x => x.UnitNumber)
                    .Select(unit =>
                    {
                        var activeLease = unit.Leases
                            .Where(l => l.Status == "Active" && l.StartDate <= today && l.EndDate >= today)
                            .OrderByDescending(l => l.StartDate)
                            .FirstOrDefault();

                        return new UnitHierarchyItemViewModel
                        {
                            UnitId = unit.Id,
                            UnitNumber = unit.UnitNumber,
                            Floor = unit.Floor,
                            UsageType = unit.UsageType,
                            AreaSqFt = unit.AreaSqFt,
                            StandardAnnualRent = unit.StandardAnnualRent,
                            Status = unit.Status,
                            HasActiveLease = activeLease != null,
                            ActiveLeaseId = activeLease?.Id,
                            ActiveContractNumber = activeLease?.ContractNumber,
                            ActiveTenantName = activeLease?.Tenant?.FullName,
                            LeaseStartDate = activeLease?.StartDate,
                            LeaseEndDate = activeLease?.EndDate
                        };
                    })
                    .ToList()
            }).ToList();

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.Properties = await _db.Properties
                .OrderBy(x => x.Code)
                .ToListAsync();

            return View(new UnitCreateViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UnitCreateViewModel model)
        {
            ViewBag.Properties = await _db.Properties
                .OrderBy(x => x.Code)
                .ToListAsync();

            var property = await _db.Properties.FirstOrDefaultAsync(x => x.Id == model.PropertyId);
            if (property == null)
            {
                ModelState.AddModelError(nameof(model.PropertyId), "Selected property was not found.");
            }

            // The unit code is generated from the property code, never typed.
            ModelState.Remove(nameof(model.UnitNumber));

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var unit = new Unit
            {
                PropertyId = model.PropertyId,
                UnitNumber = await _codeGenerationService.GenerateUnitCodeAsync(model.PropertyId),
                Floor = model.Floor,
                UsageType = model.UsageType,
                AreaSqFt = model.AreaSqFt,
                StandardAnnualRent = model.StandardAnnualRent,
                Status = model.Status,
                // Blank means inherit the property's VAT treatment.
                VatCategory = string.IsNullOrWhiteSpace(model.VatCategory) ? null : model.VatCategory,
                TitleDeedNumber = Clean(model.TitleDeedNumber),
                ParkingBays = model.ParkingBays,
                UtilityAccountNumber = Clean(model.UtilityAccountNumber),
                IsActive = true
            };

            _db.Units.Add(unit);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var unit = await _db.Units.Include(x => x.Property).FirstOrDefaultAsync(x => x.Id == id);
            if (unit == null)
            {
                return NotFound();
            }

            ViewBag.Properties = await _db.Properties
                .OrderBy(x => x.Code)
                .ToListAsync();
            ViewBag.Id = id;

            var model = new UnitCreateViewModel
            {
                PropertyId = unit.PropertyId,
                UnitNumber = unit.UnitNumber,
                Floor = unit.Floor,
                UsageType = unit.UsageType,
                AreaSqFt = unit.AreaSqFt,
                StandardAnnualRent = unit.StandardAnnualRent,
                Status = unit.Status,
                VatCategory = unit.VatCategory,
                TitleDeedNumber = unit.TitleDeedNumber,
                ParkingBays = unit.ParkingBays,
                UtilityAccountNumber = unit.UtilityAccountNumber
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, UnitCreateViewModel model)
        {
            ViewBag.Properties = await _db.Properties
                .OrderBy(x => x.Code)
                .ToListAsync();
            ViewBag.Id = id;

            var unit = await _db.Units.FirstOrDefaultAsync(x => x.Id == id);
            if (unit == null)
            {
                return BadRequest("Record not found.");
            }

            var property = await _db.Properties.FirstOrDefaultAsync(x => x.Id == model.PropertyId);
            if (property == null)
            {
                ModelState.AddModelError(nameof(model.PropertyId), "Selected property was not found.");
            }

            if (await _db.Units.AnyAsync(x => x.PropertyId == model.PropertyId && x.UnitNumber == model.UnitNumber && x.Id != id))
            {
                ModelState.AddModelError(nameof(model.UnitNumber), "Unit number already exists in this property.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            unit.PropertyId = model.PropertyId;
            unit.UnitNumber = model.UnitNumber;
            unit.Floor = model.Floor;
            unit.UsageType = model.UsageType;
            unit.AreaSqFt = model.AreaSqFt;
            unit.StandardAnnualRent = model.StandardAnnualRent;
            unit.Status = model.Status;
            unit.VatCategory = string.IsNullOrWhiteSpace(model.VatCategory) ? null : model.VatCategory;
            unit.TitleDeedNumber = Clean(model.TitleDeedNumber);
            unit.ParkingBays = model.ParkingBays;
            unit.UtilityAccountNumber = Clean(model.UtilityAccountNumber);

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}