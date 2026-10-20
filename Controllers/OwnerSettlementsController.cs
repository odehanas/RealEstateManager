using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Operations ▸ Owner Settlements: paying the landlords of managed properties the rent
    /// we collected for them, less our commission.
    /// </summary>
    public class OwnerSettlementsController : Controller
    {
        private readonly OwnerSettlementService _settlements;
        private readonly OwnerCommissionInvoiceService _commissionInvoices;

        public OwnerSettlementsController(
            OwnerSettlementService settlements,
            OwnerCommissionInvoiceService commissionInvoices)
        {
            _settlements = settlements;
            _commissionInvoices = commissionInvoices;
        }

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? asOfDate, DateTime? invoiceMonthEnd)
        {
            var model = await _settlements.GetOverviewAsync((asOfDate ?? DateTime.Today).Date);

            // The month just finished, unless today is itself a month end.
            var today = DateTime.Today;
            model.InvoiceMonthEnd = OwnerCommissionInvoiceService.MonthEnd(
                invoiceMonthEnd ?? (today == OwnerCommissionInvoiceService.MonthEnd(today) ? today : today.AddMonths(-1)));
            model.PendingCommission = await _commissionInvoices.GetPendingAsync(model.InvoiceMonthEnd);
            model.CommissionInvoices = await _commissionInvoices.GetInvoicesAsync();
            return View(model);
        }

        /// <summary>Month end: one commission tax invoice per landlord.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IssueCommissionInvoices(DateTime monthEnd)
        {
            var userName = User.FindFirst(AppClaimTypes.DisplayName)?.Value
                           ?? User.FindFirst(AppClaimTypes.Username)?.Value;

            var result = await _commissionInvoices.IssueAsync(monthEnd, userName);

            if (result.Issued.Count > 0)
                TempData["SuccessMessage"] = $"{result.Issued.Count} commission tax invoice(s) issued at {result.MonthEnd:dd/MM/yyyy}: {string.Join(", ", result.Issued)}.";
            else if (result.Warnings.Count == 0)
                TempData["SuccessMessage"] = $"Nothing to invoice: all commission up to {result.MonthEnd:dd/MM/yyyy} has been invoiced already.";

            if (result.Warnings.Count > 0)
                TempData["ErrorMessage"] = string.Join(" ", result.Warnings);

            return RedirectToAction(nameof(Index), new { invoiceMonthEnd = result.MonthEnd.ToString("yyyy-MM-dd") });
        }

        /// <summary>The commission tax invoice handed to the landlord.</summary>
        [HttpGet]
        public async Task<IActionResult> CommissionInvoice(int id)
        {
            var invoice = await _commissionInvoices.GetAsync(id);
            if (invoice == null)
                return NotFound();

            return View(invoice);
        }

        [HttpGet]
        public async Task<IActionResult> Statement(string? owner, DateTime? asOfDate)
        {
            if (string.IsNullOrWhiteSpace(owner))
                return RedirectToAction(nameof(Index));

            var statement = await _settlements.BuildStatementAsync(owner, (asOfDate ?? DateTime.Today).Date);
            if (!statement.HasProperties)
            {
                TempData["ErrorMessage"] = $"No managed property is recorded for \"{owner}\".";
                return RedirectToAction(nameof(Index));
            }

            var accounts = await _settlements.GetPaymentAccountsAsync();
            var input = new OwnerSettlementPaymentInput
            {
                OwnerName = statement.OwnerName,
                AsOfDate = statement.AsOfDate,
                SettlementDate = DateTime.Today,
                // Default to the cash actually held for the owner, never more than may be paid.
                Amount = Math.Min(statement.AvailableFromCollections, statement.MaximumPayable),
                PaidFromAccountId = accounts.FirstOrDefault(x => x.Code == "11100")?.Id ?? accounts.FirstOrDefault()?.Id ?? 0
            };

            await FillPaymentListsAsync(input);
            ViewBag.Payment = input;
            ViewBag.CommissionInvoices = await _commissionInvoices.GetInvoicesAsync(statement.OwnerName, 24);
            return View(statement);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Pay(OwnerSettlementPaymentInput input)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Check the payment details: the amount must be greater than zero.";
                return RedirectToAction(nameof(Statement), new { owner = input.OwnerName, asOfDate = input.AsOfDate.ToString("yyyy-MM-dd") });
            }

            try
            {
                var userName = User.FindFirst(AppClaimTypes.DisplayName)?.Value
                               ?? User.FindFirst(AppClaimTypes.Username)?.Value;

                var (id, warning) = await _settlements.PayAsync(input, User.GetAppUserId(), userName);

                if (warning != null)
                    TempData["PostingWarning"] = $"The settlement was saved but its journal is still a draft: {warning}";
                else
                    TempData["SuccessMessage"] = $"Settlement of {input.Amount:N2} to {input.OwnerName} recorded and posted.";

                return RedirectToAction(nameof(Print), new { id });
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Statement), new { owner = input.OwnerName, asOfDate = input.AsOfDate.ToString("yyyy-MM-dd") });
            }
        }

        /// <summary>The settlement advice handed to the owner.</summary>
        [HttpGet]
        public async Task<IActionResult> Print(int id)
        {
            var settlement = await _settlements.GetAsync(id);
            if (settlement == null)
                return NotFound();

            // The contract detail as it stood at the cut-off the payment was based on.
            var statement = await _settlements.BuildStatementAsync(settlement.OwnerName, settlement.AsOfDate);
            ViewBag.Statement = statement;
            return View(settlement);
        }

        private async Task FillPaymentListsAsync(OwnerSettlementPaymentInput input)
        {
            var accounts = await _settlements.GetPaymentAccountsAsync();
            ViewBag.PaymentAccounts = new SelectList(
                accounts.Select(x => new { x.Id, Label = $"{x.Code} - {x.Name}" }),
                "Id", "Label", input.PaidFromAccountId);
            ViewBag.PaymentMethods = new SelectList(OwnerSettlementService.PaymentMethods, input.PaymentMethod);
        }
    }
}
