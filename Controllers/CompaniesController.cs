using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    [Authorize(Roles = "SystemAdmin")]
    public class CompaniesController : Controller
    {
        private readonly AdminManagementService _adminManagementService;
        private readonly SubscriptionService _subscriptionService;

        public CompaniesController(
            AdminManagementService adminManagementService,
            SubscriptionService subscriptionService)
        {
            _adminManagementService = adminManagementService;
            _subscriptionService = subscriptionService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = await _adminManagementService.GetCompaniesAsync();
            return View(model);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new CompanyFormViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CompanyFormViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                await _adminManagementService.CreateCompanyAsync(model);
                TempData["SuccessMessage"] = "Company created successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var model = await _adminManagementService.GetCompanyFormAsync(id);
            if (model == null)
                return NotFound();

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(CompanyFormViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                await _adminManagementService.UpdateCompanyAsync(model);
                TempData["SuccessMessage"] = "Company updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateActivationCode(CompanyFormViewModel model)
        {
            if (!model.Id.HasValue)
                return NotFound();

            try
            {
                var company = await _adminManagementService.GetCompanyFormAsync(model.Id.Value);
                if (company == null)
                    return NotFound();

                var code = await _subscriptionService.IssueActivationCodeAsync(
                    model.Id.Value,
                    model.ActivationCodeDurationDays,
                    model.ActivationCodeNotes);

                TempData["SuccessMessage"] = $"Activation code generated: {code}";
                TempData["GeneratedActivationCode"] = code;
                TempData["GeneratedActivationMessage"] =
                    $"Dear {company.Name} user,{Environment.NewLine}{Environment.NewLine}" +
                    $"Your subscription activation code is:{Environment.NewLine}{Environment.NewLine}" +
                    $"{code}{Environment.NewLine}{Environment.NewLine}" +
                    $"This code adds {model.ActivationCodeDurationDays} day(s) to your subscription.{Environment.NewLine}" +
                    $"Please sign in, open the Subscription page, enter the code, and click Apply Code.{Environment.NewLine}{Environment.NewLine}" +
                    $"Thank you.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Edit), new { id = model.Id.Value });
        }
    }
}
