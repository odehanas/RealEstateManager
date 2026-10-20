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
            var model = await _subscriptionService.BuildCurrentCompanyViewModelAsync(companyId, returnUrl);
            ViewBag.ExpiredMessage = expired;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Activate(string activationCode, string? returnUrl = null)
        {
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
                ViewBag.ExpiredMessage = true;
                return View("Index", model);
            }
        }
    }
}
