using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Paid services: the company's price list, the invoice for an order, and the
    /// report of what the services earned. Who may open what is in
    /// PermissionCatalog (ControllerPermissionMap and ActionPermissionMap).
    /// </summary>
    [Authorize]
    public class ServiceCatalogController : Controller
    {
        private readonly PaidServiceService _services;

        public ServiceCatalogController(PaidServiceService services)
        {
            _services = services;
        }

        /// <summary>The price list, with the form to add a service or change the one chosen.</summary>
        [HttpGet]
        public async Task<IActionResult> Index(int? edit)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var items = await _services.ListAsync(companyId);
            ViewBag.Accounts = await _services.IncomeAccountsAsync(companyId);
            ViewBag.Editing = TempData["ServiceForm"] is string posted
                ? System.Text.Json.JsonSerializer.Deserialize<ServiceCatalogItem>(posted)
                : items.FirstOrDefault(x => x.Id == edit);
            return View(items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(ServiceCatalogItem model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var error = await _services.SaveAsync(companyId, model, User.Identity?.Name);
            if (error != null)
            {
                // Give the form back as it was typed.
                model.Company = null;
                model.IncomeAccount = null;
                TempData["ServiceForm"] = System.Text.Json.JsonSerializer.Serialize(model);
                TempData["ErrorMessage"] = error;
                return RedirectToAction(nameof(Index));
            }

            TempData["SuccessMessage"] = model.Id == 0 ? "Service added to the price list." : "Service saved.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Raises the invoice for a tenant's order. Sent from the request's own page.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IssueInvoice(int requestId, decimal? price, string? returnUrl)
        {
            var (ok, message) = await _services.IssueInvoiceAsync(
                User.GetRequiredCurrentCompanyId(), requestId, price, User.GetAppUserId(), User.Identity?.Name);
            TempData[ok ? "ServiceSuccess" : "ServiceError"] = message;

            return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? Redirect(returnUrl)
                : RedirectToAction("Details", "ServiceRequests", new { id = requestId });
        }

        /// <summary>Every order in a period: what was charged, what was paid, what it cost.</summary>
        [HttpGet]
        public async Task<IActionResult> Orders(DateTime? fromDate, DateTime? toDate)
        {
            var to = (toDate ?? DateTime.Today).Date;
            var from = (fromDate ?? new DateTime(to.Year, 1, 1)).Date;
            ViewBag.From = from;
            ViewBag.To = to;
            return View(await _services.OrdersAsync(User.GetRequiredCurrentCompanyId(), from, to));
        }
    }
}
