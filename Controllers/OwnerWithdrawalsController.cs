using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>Operations ▸ Owner Withdrawals: money the owner takes out, recorded and categorised in one step.</summary>
    public class OwnerWithdrawalsController : Controller
    {
        private readonly OwnerWithdrawalService _withdrawals;

        public OwnerWithdrawalsController(OwnerWithdrawalService withdrawals)
        {
            _withdrawals = withdrawals;
        }

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? fromDate, DateTime? toDate)
        {
            var to = (toDate ?? DateTime.Today).Date;
            var from = (fromDate ?? new DateTime(to.Year, 1, 1)).Date;

            ViewBag.FromDate = from;
            ViewBag.ToDate = to;
            ViewBag.OwnerAccounts = await _withdrawals.GetOwnerAccountsAsync();
            ViewBag.PaidFromAccounts = await _withdrawals.GetPaidFromAccountsAsync();
            ViewBag.Categories = await _withdrawals.GetCategoriesAsync();
            return View(await _withdrawals.GetRegisterAsync(from, to));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Record(
            DateTime date, int ownerAccountId, int categoryId, decimal amount, int paidFromAccountId, string? description, string? reference)
        {
            try
            {
                var (_, warning) = await _withdrawals.RecordAsync(date, ownerAccountId, categoryId, amount, paidFromAccountId, description, reference);
                if (warning != null)
                    TempData["PostingWarning"] = $"The withdrawal was saved but its journal is still a draft: {warning}";
                else
                    TempData["SuccessMessage"] = $"Withdrawal of {amount:N2} recorded and posted.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
