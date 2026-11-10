using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    [Authorize]
    public class SubscriptionController : Controller
    {
        private readonly SubscriptionService _subscriptionService;

        public SubscriptionController(SubscriptionService subscriptionService)
        {
            _subscriptionService = subscriptionService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(bool expired = false, string? returnUrl = null)
        {
            var companyId = User.GetRequiredCurrentCompanyId();

            // The subscription is the company's business, not the maintenance team's. While it
            // runs they go to their jobs; once it has lapsed the jobs screen sends them here, so
            // sending them back would loop forever ("the page isn't redirecting properly").
            if (await HttpContext.RequestServices.GetRequiredService<PermissionService>().IsMaintenanceOnlyAsync(User))
            {
                return await _subscriptionService.IsCompanyAccessibleAsync(companyId)
                    ? RedirectToAction("Index", "MaintenanceJobs")
                    : View("Unavailable");
            }

            var model = await _subscriptionService.BuildCurrentCompanyViewModelAsync(companyId, returnUrl);
            // The "expired" flag comes from the address; only believe it when access really has lapsed.
            ViewBag.ExpiredMessage = expired && !await _subscriptionService.IsCompanyAccessibleAsync(companyId);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Activate(string activationCode, string? returnUrl = null)
        {
            if (await HttpContext.RequestServices.GetRequiredService<PermissionService>().IsMaintenanceOnlyAsync(User))
                return RedirectToAction("Index", "MaintenanceJobs");

            var companyId = User.GetRequiredCurrentCompanyId();

            try
            {
                var appliedBy = $"{User.FindFirst(AppClaimTypes.DisplayName)?.Value} " +
                                $"({User.FindFirst(AppClaimTypes.Username)?.Value}) on {DateTime.Today:dd/MM/yyyy}";
                await _subscriptionService.ActivateCodeAsync(companyId, activationCode, appliedBy);
                TempData["SuccessMessage"] = "Activation code applied successfully. Subscription access has been restored.";

                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                return RedirectToAction("Index", "Home");
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                var model = await _subscriptionService.BuildCurrentCompanyViewModelAsync(companyId, returnUrl);
                model.ActivationCode = activationCode;
                // Only a company that really has no access is told it has expired.
                ViewBag.ExpiredMessage = !await _subscriptionService.IsCompanyAccessibleAsync(companyId);
                return View("Index", model);
            }
        }
    }
}
