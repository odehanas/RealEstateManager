using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class PrepaymentsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _postingService;

        public PrepaymentsController(ApplicationDbContext db, AccountingPostingService postingService)
        {
            _db = db;
            _postingService = postingService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? asOfDate, bool showPosted = true)
        {
            var model = await _postingService.GetPrepaymentSchedulesAsync((asOfDate ?? DateTime.Today).Date, showPosted);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var model = new PrepaymentCreateViewModel
            {
                PaymentDate = DateTime.Today,
                FirstRecognitionDate = DateTime.Today.AddMonths(1),
                RecognitionMonths = 12
            };

            await ApplyDefaultMappingsAsync(model);
            await LoadLookupsAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PrepaymentCreateViewModel model)
        {
            await ApplyDefaultMappingsAsync(model);
            await LoadLookupsAsync();

            if (string.IsNullOrWhiteSpace(model.Description))
                ModelState.AddModelError(nameof(model.Description), "Description is required.");

            if (model.TotalAmount <= 0m)
                ModelState.AddModelError(nameof(model.TotalAmount), "Total amount must be greater than zero.");

            if (model.RecognitionMonths <= 0)
                ModelState.AddModelError(nameof(model.RecognitionMonths), "Recognition months must be greater than zero.");

            if (!model.PrepaidAccountId.HasValue)
                ModelState.AddModelError(nameof(model.PrepaidAccountId), "Prepaid account is required.");

            if (!model.ExpenseAccountId.HasValue)
                ModelState.AddModelError(nameof(model.ExpenseAccountId), "Expense account is required.");

            if (!model.OffsetAccountId.HasValue)
                ModelState.AddModelError(nameof(model.OffsetAccountId), "Paid / payable account is required.");

            if (model.FirstRecognitionDate.Date < model.PaymentDate.Date)
                ModelState.AddModelError(nameof(model.FirstRecognitionDate), "First recognition date cannot be earlier than payment date.");

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                var companyId = User.GetRequiredCurrentCompanyId();
                await _postingService.CreatePrepaymentScheduleAsync(companyId, model);
                TempData["SuccessMessage"] = "Prepayment schedule created and initial prepaid entry posted successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PostDue(DateTime asOfDate, bool showPosted = true)
        {
            try
            {
                var count = await _postingService.ProcessPrepaymentRecognitionsUpToAsync(asOfDate.Date);
                TempData["SuccessMessage"] = count == 0
                    ? "No prepayment recognitions were due."
                    : $"Posted {count} prepayment recognition entry(s).";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Index), new { asOfDate = asOfDate.ToString("yyyy-MM-dd"), showPosted });
        }

        private async Task ApplyDefaultMappingsAsync(PrepaymentCreateViewModel model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies
                .Include(x => x.AccountSetting)
                .FirstOrDefaultAsync(x => x.Id == companyId);

            var setting = company?.AccountSetting;
            if (setting == null)
                return;

            model.PrepaidAccountId ??= setting.PrepaidExpenseDefaultAccountId;
            model.ExpenseAccountId ??= setting.GeneralExpenseDefaultAccountId;
            model.OffsetAccountId ??= setting.AccountsPayableAccountId ?? setting.DefaultCashAccountId;
        }

        private async Task LoadAccountsAsync()
        {
            ViewBag.Accounts = await _db.Accounts
                .Where(x => x.IsActive && x.IsPostable)
                .OrderBy(x => x.Code)
                .ToListAsync();
        }

        private async Task LoadLookupsAsync()
        {
            await LoadAccountsAsync();

            ViewBag.Vendors = await _db.Vendors
                .Where(x => x.IsActive)
                .OrderBy(x => x.Code)
                .ToListAsync();
        }
    }
}
