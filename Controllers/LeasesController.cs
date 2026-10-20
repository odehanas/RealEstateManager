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
        private readonly LeaseWorkflowService _workflow;
        private readonly ApprovalService _approvals;

        public LeasesController(
            ApplicationDbContext db,
            CodeGenerationService codeGenerationService,
            ContractQrService contractQrService,
            LeaseWorkflowService workflow,
            ApprovalService approvals)
        {
            _db = db;
            _codeGenerationService = codeGenerationService;
            _contractQrService = contractQrService;
            _workflow = workflow;
            _approvals = approvals;
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

            // The latest approval step of each contract: waiting, approved, or returned with a reason.
            ViewBag.Approvals = await _approvals.GetLatestAsync(ApprovalDocumentTypes.Lease, leases.Select(x => x.Id));
            ViewBag.ApprovalRequired = await _workflow.ApprovalRequiredAsync();
            ViewBag.UserId = User.GetAppUserId();

            return View(leases);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await LoadCreateListsAsync();
            ViewBag.ApprovalRequired = await _workflow.ApprovalRequiredAsync();
            return View(new LeaseCreateViewModel());
        }

        /// <summary>Puts a draft into effect - when the company does not require approval.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Activate(int id)
        {
            var (success, message) = await _workflow.ActivateAsync(id);
            TempData[success ? "LeaseSuccess" : "LeaseError"] = message;
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Sends a draft to a second person for approval.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(int id)
        {
            var (success, message) = await _workflow.SubmitAsync(id, User.GetAppUserId() ?? 0, CurrentUserName());
            TempData[success ? "LeaseSuccess" : "LeaseError"] = message;
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Takes a submitted contract back to draft before anyone decides.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Withdraw(int id)
        {
            var pending = await _approvals.GetPendingAsync(ApprovalDocumentTypes.Lease, id);
            if (pending == null)
            {
                TempData["LeaseError"] = "This contract is not waiting for approval.";
                return RedirectToAction(nameof(Index));
            }

            var (success, message) = await _approvals.WithdrawAsync(pending.Id, User.GetAppUserId() ?? 0, CurrentUserName());
            TempData[success ? "LeaseSuccess" : "LeaseError"] = message;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDraft(int id)
        {
            var (success, message) = await _workflow.DeleteDraftAsync(id);
            TempData[success ? "LeaseSuccess" : "LeaseError"] = message;
            return RedirectToAction(nameof(Index));
        }

        private string CurrentUserName() =>
            User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value ?? "User";

        /// <summary>After saving a draft: keep it, activate it, or submit it, as the button pressed said.</summary>
        private async Task<IActionResult> AfterSaveAsync(Lease lease, string? nextStep)
        {
            if (nextStep == "activate")
            {
                var (ok, message) = await _workflow.ActivateAsync(lease.Id);
                TempData[ok ? "LeaseSuccess" : "LeaseError"] = ok ? message : $"Saved as a draft, but not activated: {message}";
            }
            else if (nextStep == "submit")
            {
                var (ok, message) = await _workflow.SubmitAsync(lease.Id, User.GetAppUserId() ?? 0, CurrentUserName());
                TempData[ok ? "LeaseSuccess" : "LeaseError"] = ok ? message : $"Saved as a draft, but not submitted: {message}";
            }
            else
            {
                TempData["LeaseSuccess"] = $"Contract {lease.ContractNumber} is saved as a draft. It has no effect until it is " +
                    (await _workflow.ApprovalRequiredAsync() ? "submitted and approved." : "activated.");
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(LeaseCreateViewModel model, string? nextStep)
        {
            await LoadCreateListsAsync();
            ViewBag.ApprovalRequired = await _workflow.ApprovalRequiredAsync();

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

            // A contract that is active or awaiting approval holds the unit for its dates.
            var clash = await _workflow.CheckUnitFreeAsync(0, model.UnitId, model.StartDate, model.EndDate);
            if (clash != null)
            {
                ModelState.AddModelError(nameof(model.UnitId), clash);
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
                // Every contract starts as a draft: no invoices, no effect on the unit.
                // It takes effect when activated, or when approved if the company requires it.
                Status = LeaseStatuses.Draft,
                Notes = model.Notes,
                CreatedAt = DateTime.UtcNow,
                CreatedByAppUserId = User.GetAppUserId()
            };

            VatDeterminationService.ApplyToLease(
                lease,
                effectiveCategory,
                effectiveCategory == derivedCategory ? null : model.VatOverrideReason);

            _db.Leases.Add(lease);
            await _db.SaveChangesAsync();

            return await AfterSaveAsync(lease, nextStep);
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
            var instalments = await _db.CustomerInvoices
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

            // A draft or a contract awaiting approval has no invoices yet: show the ones it will create.
            if (instalments.Count == 0 && LeaseStatuses.IsBeforeActivation(lease.Status))
            {
                instalments = LeaseScheduleBuilder.PlanInvoices(companyId, lease)
                    .Select(x => new ContractInstalmentViewModel
                    {
                        InvoiceNumber = x.InvoiceNumber,
                        DueDate = x.DueDate,
                        Subtotal = x.Subtotal,
                        VatAmount = x.VatAmount,
                        TotalAmount = x.TotalAmount
                    })
                    .ToList();
            }

            ViewBag.Instalments = instalments;

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
            await LoadEditContextAsync(lease);

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
        public async Task<IActionResult> Edit(int id, LeaseCreateViewModel model, string? nextStep)
        {
            await LoadCreateListsAsync();

            var lease = await _db.Leases.FirstOrDefaultAsync(x => x.Id == id);
            if (lease == null)
            {
                return BadRequest("Record not found.");
            }

            await LoadEditContextAsync(lease);

            if (lease.Status == LeaseStatuses.PendingApproval)
            {
                TempData["LeaseError"] = $"Contract {lease.ContractNumber} is awaiting approval. Withdraw it first to change it.";
                return RedirectToAction(nameof(Index));
            }

            // Once in effect the terms are locked: its invoices were made from them and must
            // keep matching the signed contract. Only the notes can still be updated.
            if (!LeaseStatuses.IsBeforeActivation(lease.Status))
            {
                lease.Notes = model.Notes;
                await _db.SaveChangesAsync();
                TempData["LeaseSuccess"] = $"Notes for contract {lease.ContractNumber} are saved. Its terms are locked because it is {lease.Status.ToLowerInvariant()}.";
                return RedirectToAction(nameof(Index));
            }

            var clash = await _workflow.CheckUnitFreeAsync(lease.Id, model.UnitId, model.StartDate, model.EndDate);
            if (clash != null)
            {
                ModelState.AddModelError(nameof(model.UnitId), clash);
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

            return await AfterSaveAsync(lease, nextStep);
        }

        /// <summary>What the edit screen needs to know: whether the terms can change, and the approval history.</summary>
        private async Task LoadEditContextAsync(Lease lease)
        {
            ViewBag.Id = lease.Id;
            ViewBag.Status = lease.Status;
            ViewBag.ContractNumber = lease.ContractNumber;
            ViewBag.TermsLocked = !LeaseStatuses.IsBeforeActivation(lease.Status) || lease.Status == LeaseStatuses.PendingApproval;
            ViewBag.ApprovalRequired = await _workflow.ApprovalRequiredAsync();
            ViewBag.ApprovalHistory = await _approvals.GetHistoryAsync(ApprovalDocumentTypes.Lease, lease.Id);
        }
    }
}
