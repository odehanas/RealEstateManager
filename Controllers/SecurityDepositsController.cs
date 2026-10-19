using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class SecurityDepositsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _postingService;
        private readonly JournalWorkflowService _journalWorkflowService;

        public SecurityDepositsController(
            ApplicationDbContext db,
            AccountingPostingService postingService,
            JournalWorkflowService journalWorkflowService)
        {
            _db = db;
            _postingService = postingService;
            _journalWorkflowService = journalWorkflowService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var deposits = await _db.SecurityDeposits
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .OrderByDescending(x => x.ReceiptDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return View(deposits);
        }

        [HttpGet]
        public async Task<IActionResult> Create(int? leaseId)
        {
            await LoadLeasesAsync(leaseId);

            var model = new SecurityDepositCreateViewModel();

            if (leaseId.HasValue)
            {
                var lease = await _db.Leases.FirstOrDefaultAsync(x => x.Id == leaseId.Value);
                if (lease != null)
                {
                    model.LeaseId = lease.Id;
                    model.Amount = lease.SecurityDepositAmount;
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SecurityDepositCreateViewModel model)
        {
            await LoadLeasesAsync(model.LeaseId);

            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
            {
                ModelState.AddModelError(string.Empty, "No company record found.");
            }

            var lease = await _db.Leases.FirstOrDefaultAsync(x => x.Id == model.LeaseId);
            if (lease == null)
            {
                ModelState.AddModelError(nameof(model.LeaseId), "Selected lease was not found.");
            }

            if (model.Amount <= 0)
            {
                ModelState.AddModelError(nameof(model.Amount), "Deposit amount must be greater than zero.");
            }

            if (!string.IsNullOrWhiteSpace(model.ReferenceNumber))
            {
                var exists = await _db.SecurityDeposits.AnyAsync(x => x.ReferenceNumber == model.ReferenceNumber);
                if (exists)
                {
                    ModelState.AddModelError(nameof(model.ReferenceNumber), "Reference number already exists.");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var deposit = new SecurityDeposit
            {
                CompanyId = companyId,
                LeaseId = model.LeaseId,
                ReferenceNumber = string.IsNullOrWhiteSpace(model.ReferenceNumber)
                    ? await GenerateReferenceNumberAsync()
                    : model.ReferenceNumber.Trim(),
                ReceiptDate = model.ReceiptDate,
                Amount = model.Amount,
                PaymentMethod = model.PaymentMethod,
                Status = model.Status
            };

            _db.SecurityDeposits.Add(deposit);
            await _db.SaveChangesAsync();

            var journal = await _postingService.PostSecurityDepositAsync(deposit.Id);

            // "Save and Post" posts, through the same checks as Journal Inquiry.
            var notPosted = await _journalWorkflowService.PostNewEntryAsync(journal.Id);
            if (notPosted != null)
                TempData["PostingWarning"] = $"Deposit {deposit.ReferenceNumber} was saved as a draft but not posted: {notPosted}";

            return RedirectToAction(nameof(Index));
        }

        private async Task LoadLeasesAsync(int? selectedLeaseId = null)
        {
            var leases = await _db.Leases
                .Include(x => x.Unit)
                    .ThenInclude(x => x!.Property)
                .Include(x => x.Tenant)
                .Where(x => x.Status == "Active")
                .OrderByDescending(x => x.StartDate)
                .ToListAsync();

            ViewBag.Leases = leases;
            ViewBag.SelectedLeaseId = selectedLeaseId;
        }

        private async Task<string> GenerateReferenceNumberAsync()
        {
            var today = DateTime.Today;
            var count = await _db.SecurityDeposits.CountAsync(x => x.ReceiptDate.Date == today) + 1;
            return $"DEP-{today:yyyyMMdd}-{count:D3}";
        }
    }
}
