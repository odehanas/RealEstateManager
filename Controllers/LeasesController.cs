using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class LeasesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly CodeGenerationService _codeGenerationService;
        private readonly ContractQrService _contractQrService;

        public LeasesController(
            ApplicationDbContext db,
            CodeGenerationService codeGenerationService,
            ContractQrService contractQrService)
        {
            _db = db;
            _codeGenerationService = codeGenerationService;
            _contractQrService = contractQrService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var leases = await _db.Leases
                .Include(x => x.Unit)
                    .ThenInclude(x => x!.Property)
                .Include(x => x.Tenant)
                .OrderByDescending(x => x.StartDate)
                .ToListAsync();

            return View(leases);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await LoadCreateListsAsync();
            return View(new LeaseCreateViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(LeaseCreateViewModel model)
        {
            await LoadCreateListsAsync();

            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
            {
                ModelState.AddModelError(string.Empty, "No company record found.");
            }

            var unit = await _db.Units
                .Include(x => x.Property)
                .FirstOrDefaultAsync(x => x.Id == model.UnitId);

            if (unit == null)
            {
                ModelState.AddModelError(nameof(model.UnitId), "Selected unit was not found.");
            }

            var tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == model.TenantId);
            if (tenant == null)
            {
                ModelState.AddModelError(nameof(model.TenantId), "Selected tenant was not found.");
            }

            if (model.StartDate >= model.EndDate)
            {
                ModelState.AddModelError(nameof(model.EndDate), "End date must be later than start date.");
            }

            if (model.BillingFrequencyMonths <= 0 || 12 % model.BillingFrequencyMonths != 0)
            {
                ModelState.AddModelError(nameof(model.BillingFrequencyMonths), "Billing frequency must be 1, 2, 3, 4, 6, or 12.");
            }

            // The contract code is generated from the unit code, never typed.
            ModelState.Remove(nameof(model.ContractNumber));

            var overlappingLeaseExists = await _db.Leases.AnyAsync(x =>
                x.UnitId == model.UnitId &&
                x.Status == "Active" &&
                model.StartDate <= x.EndDate &&
                model.EndDate >= x.StartDate);

            if (overlappingLeaseExists)
            {
                ModelState.AddModelError(nameof(model.UnitId), "This unit already has an overlapping active lease.");
            }

            // Overriding the derived VAT treatment is allowed, but never silently:
            // a wrong treatment is a tax error, so the reason is on the record.
            if (!string.IsNullOrWhiteSpace(model.VatCategoryOverride) &&
                string.IsNullOrWhiteSpace(model.VatOverrideReason))
            {
                ModelState.AddModelError(nameof(model.VatOverrideReason),
                    "Give a reason when overriding the VAT treatment derived from the unit.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // VAT treatment comes from the unit, falling back to the property. It is
            // never typed in freehand: an override must be a deliberate choice with a
            // reason, so a residential lease cannot silently acquire 5% output tax.
            var derivedCategory = VatDeterminationService.ResolveCategory(unit!.Property!, unit);
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
                Status = "Active",
                Notes = model.Notes,
                CreatedAt = DateTime.UtcNow
            };

            VatDeterminationService.ApplyToLease(
                lease,
                effectiveCategory,
                effectiveCategory == derivedCategory ? null : model.VatOverrideReason);

            _db.Leases.Add(lease);
            await _db.SaveChangesAsync();

            unit.Status = "Occupied";

            GenerateInvoices(companyId, lease);
            GenerateRevenueRecognitionSchedules(companyId, lease, unit.Property!.OwnershipType);

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        private void GenerateInvoices(int companyId, Lease lease)
        {
            var billingMonths = lease.BillingFrequencyMonths;
            var invoicesPerYear = 12 / billingMonths;

            var baseAmountPerInvoice = lease.AnnualRent / invoicesPerYear;

            // Tax is computed from the lease's VAT category through the one service
            // that owns that decision, so the rate and the rounding are identical
            // everywhere tax is calculated.
            var vatAmountPerInvoice = VatDeterminationService.CalculateTax(baseAmountPerInvoice, lease.VatCategory);

            var invoiceDate = lease.StartDate;

            while (invoiceDate <= lease.EndDate)
            {
                _db.CustomerInvoices.Add(new CustomerInvoice
                {
                    CompanyId = companyId,
                    LeaseId = lease.Id,
                    InvoiceNumber = $"INV-{lease.ContractNumber}-{invoiceDate:yyyyMM}",
                    InvoiceDate = invoiceDate,
                    DueDate = invoiceDate,
                    DateOfSupply = invoiceDate,
                    Subtotal = baseAmountPerInvoice,
                    VatAmount = vatAmountPerInvoice,
                    TotalAmount = baseAmountPerInvoice + vatAmountPerInvoice,
                    AmountPaid = 0m,
                    // Snapshot the treatment so a later change to the property or the
                    // lease can never rewrite an invoice that has already been issued.
                    VatCategory = lease.VatCategory,
                    VatRate = lease.VatRate,
                    Status = "Draft"
                });

                invoiceDate = invoiceDate.AddMonths(billingMonths);
            }
        }

        private void GenerateRevenueRecognitionSchedules(int companyId, Lease lease, string ownershipType)
        {
            var monthlyRent = Math.Round(lease.AnnualRent / 12m, 2);
            var startMonth = new DateTime(lease.StartDate.Year, lease.StartDate.Month, 1);
            var endMonth = new DateTime(lease.EndDate.Year, lease.EndDate.Month, 1);

            var currentMonth = startMonth;

            while (currentMonth <= endMonth)
            {
                var recognitionDate = new DateTime(
                    currentMonth.Year,
                    currentMonth.Month,
                    DateTime.DaysInMonth(currentMonth.Year, currentMonth.Month));

                _db.RevenueRecognitionSchedules.Add(new RevenueRecognitionSchedule
                {
                    CompanyId = companyId,
                    LeaseId = lease.Id,
                    RecognitionDate = recognitionDate,
                    Amount = monthlyRent,
                    Status = "Planned"
                });

                currentMonth = currentMonth.AddMonths(1);
            }
        }

        private async Task LoadCreateListsAsync()
        {
            ViewBag.Units = await _db.Units
                .Include(x => x.Property)
                .OrderBy(x => x.Property!.Code)
                .ThenBy(x => x.UnitNumber)
                .ToListAsync();

            ViewBag.Tenants = await _db.Tenants
                .OrderBy(x => x.Code)
                .ToListAsync();
        }

        /// <summary>
        /// Printable contract label: the code, its QR code, and the key lease facts.
        /// Sized to stick on a physical contract file.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ContractLabel(int id)
        {
            var companyId = User.GetRequiredCurrentCompanyId();

            var lease = await _db.Leases
                .AsNoTracking()
                .Include(x => x.Tenant)
                .Include(x => x.Unit)
                    .ThenInclude(x => x!.Property)
                .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);

            if (lease == null)
                return NotFound();

            var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == companyId);

            ViewBag.CompanyName = company?.LegalName ?? company?.Name ?? string.Empty;
            ViewBag.QrDataUri = _contractQrService.GetQrDataUri(lease.ContractNumber);

            return View(lease);
        }

        /// <summary>
        /// Draft tenancy agreement built from the lease data. A working draft for legal
        /// review - NOT the official registered form, which is issued by the authority.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ContractDraft(int id)
        {
            var companyId = User.GetRequiredCurrentCompanyId();

            var lease = await _db.Leases
                .AsNoTracking()
                .Include(x => x.Tenant)
                .Include(x => x.Unit)
                    .ThenInclude(x => x!.Property)
                .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);

            if (lease == null)
                return NotFound();

            var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == companyId);

            // The payment schedule already exists as the generated invoices, so the
            // contract states exactly what the tenant will be billed.
            ViewBag.Instalments = await _db.CustomerInvoices
                .AsNoTracking()
                .Where(x => x.LeaseId == lease.Id && x.Status != "Cancelled")
                .OrderBy(x => x.InvoiceDate)
                .Select(x => new ContractInstalmentViewModel
                {
                    InvoiceNumber = x.InvoiceNumber,
                    DueDate = x.DueDate,
                    Subtotal = x.Subtotal,
                    VatAmount = x.VatAmount,
                    TotalAmount = x.TotalAmount
                })
                .ToListAsync();

            ViewBag.Company = company;
            ViewBag.QrDataUri = _contractQrService.GetQrDataUri(lease.ContractNumber);

            return View(lease);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var lease = await _db.Leases.FirstOrDefaultAsync(x => x.Id == id);
            if (lease == null)
            {
                return NotFound();
            }

            await LoadCreateListsAsync();
            ViewBag.Id = id;

            var model = new LeaseCreateViewModel
            {
                UnitId = lease.UnitId,
                TenantId = lease.TenantId,
                ContractNumber = lease.ContractNumber,
                StartDate = lease.StartDate,
                EndDate = lease.EndDate,
                AnnualRent = lease.AnnualRent,
                BillingFrequencyMonths = lease.BillingFrequencyMonths,
                IsVatApplicable = lease.IsVatApplicable,
                VatRate = lease.VatRate,
                // Only show it as an override if a reason was recorded; otherwise the
                // lease is simply following the unit and the selector stays on inherit.
                VatCategoryOverride = string.IsNullOrWhiteSpace(lease.VatOverrideReason) ? null : lease.VatCategory,
                VatOverrideReason = lease.VatOverrideReason,
                SecurityDepositAmount = lease.SecurityDepositAmount,
                Notes = lease.Notes
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, LeaseCreateViewModel model)
        {
            await LoadCreateListsAsync();
            ViewBag.Id = id;

            var lease = await _db.Leases.FirstOrDefaultAsync(x => x.Id == id);
            if (lease == null)
            {
                return BadRequest("Record not found.");
            }

            var unit = await _db.Units
                .Include(x => x.Property)
                .FirstOrDefaultAsync(x => x.Id == model.UnitId);

            if (unit == null)
            {
                ModelState.AddModelError(nameof(model.UnitId), "Selected unit was not found.");
            }

            var tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == model.TenantId);
            if (tenant == null)
            {
                ModelState.AddModelError(nameof(model.TenantId), "Selected tenant was not found.");
            }

            if (model.StartDate >= model.EndDate)
            {
                ModelState.AddModelError(nameof(model.EndDate), "End date must be later than start date.");
            }

            if (model.BillingFrequencyMonths <= 0 || 12 % model.BillingFrequencyMonths != 0)
            {
                ModelState.AddModelError(nameof(model.BillingFrequencyMonths), "Billing frequency must be 1, 2, 3, 4, 6, or 12.");
            }

            // The contract code is fixed once issued - it appears on signed documents
            // and on the QR code, so it is never editable.
            ModelState.Remove(nameof(model.ContractNumber));

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            lease.UnitId = model.UnitId;
            lease.TenantId = model.TenantId;
            lease.StartDate = model.StartDate;
            lease.EndDate = model.EndDate;
            lease.AnnualRent = model.AnnualRent;
            lease.BillingFrequencyMonths = model.BillingFrequencyMonths;

            var derivedCategory = VatDeterminationService.ResolveCategory(unit!.Property!, unit);
            var effectiveCategory = string.IsNullOrWhiteSpace(model.VatCategoryOverride)
                ? derivedCategory
                : model.VatCategoryOverride!;

            VatDeterminationService.ApplyToLease(
                lease,
                effectiveCategory,
                effectiveCategory == derivedCategory ? null : model.VatOverrideReason);

            lease.SecurityDepositAmount = model.SecurityDepositAmount;
            lease.Notes = model.Notes;

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
