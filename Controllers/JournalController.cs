using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class JournalController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly LedgerInquiryService _ledgerInquiryService;
        private readonly JournalWorkflowService _journalWorkflowService;

        public JournalController(
            ApplicationDbContext db,
            LedgerInquiryService ledgerInquiryService,
            JournalWorkflowService journalWorkflowService)
        {
            _db = db;
            _ledgerInquiryService = ledgerInquiryService;
            _journalWorkflowService = journalWorkflowService;
        }

        /// <summary>Draft to Posted. The entry begins affecting the accounts.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Post(int id, string? returnUrl)
        {
            var result = await _journalWorkflowService.PostAsync(id);
            SetWorkflowMessage(result);
            return RedirectBack(returnUrl);
        }

        /// <summary>
        /// Posted back to Draft. Removes the entry from the accounts and unwinds the
        /// document effects that went with it.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetToDraft(int id, string? returnUrl)
        {
            var result = await _journalWorkflowService.ResetToDraftAsync(id);
            SetWorkflowMessage(result);
            return RedirectBack(returnUrl);
        }

        /// <summary>
        /// Posts every draft matching the current filter. Needed because revenue
        /// recognition and depreciation runs create drafts in bulk.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PostAllDrafts(DateTime? fromDate, DateTime? toDate, string? sourceType)
        {
            var batch = await _journalWorkflowService.PostAllDraftsAsync(fromDate, toDate, sourceType);

            if (batch.FailedCount > 0)
            {
                TempData["JournalError"] = batch.Summary + " " +
                    string.Join(" | ", batch.Failures.Take(5)) +
                    (batch.FailedCount > 5 ? $" (and {batch.FailedCount - 5} more)" : string.Empty);
            }
            else
            {
                TempData["JournalSuccess"] = batch.Summary;
            }

            return RedirectToAction(nameof(Index), new { fromDate, toDate, sourceType, postedOnly = false });
        }

        private void SetWorkflowMessage(JournalWorkflowResult result)
        {
            if (result.Success)
                TempData["JournalSuccess"] = result.Message;
            else
                TempData["JournalError"] = result.Message;
        }

        private IActionResult RedirectBack(string? returnUrl)
        {
            return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? Redirect(returnUrl)
                : RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? fromDate, DateTime? toDate, string? sourceType, bool postedOnly = true)
        {
            var model = await _ledgerInquiryService.GetJournalInquiryAsync(fromDate, toDate, sourceType, postedOnly);

            ViewBag.SourceTypes = await _db.JournalEntries
                .Where(x => x.SourceType != null && x.SourceType != "")
                .Select(x => x.SourceType!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> GeneralLedger(int? accountId, DateTime? fromDate, DateTime? toDate)
        {
            ViewBag.Accounts = await _db.Accounts
                .OrderBy(x => x.Code)
                .ToListAsync();

            if (!accountId.HasValue)
            {
                return View();
            }

            var model = await _ledgerInquiryService.GetGeneralLedgerAsync(
                accountId.Value,
                (fromDate ?? new DateTime(DateTime.Today.Year, 1, 1)).Date,
                (toDate ?? DateTime.Today).Date);

            return View(model);
        }
    }
}