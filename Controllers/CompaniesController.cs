using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    // Companies and their subscriptions are the platform owner's business, not a
    // System Admin's: whoever can issue an activation code can give away paid time.
    [Authorize(Policy = PlatformOwnerRequirement.PolicyName)]
    public class CompaniesController : Controller
    {
        private readonly AdminManagementService _adminManagementService;
        private readonly SubscriptionService _subscriptionService;
        private readonly SampleDataService _sampleDataService;

        public CompaniesController(
            AdminManagementService adminManagementService,
            SubscriptionService subscriptionService,
            SampleDataService sampleDataService)
        {
            _adminManagementService = adminManagementService;
            _subscriptionService = subscriptionService;
            _sampleDataService = sampleDataService;
        }

        /// <summary>
        /// Fills an empty company with a year of fictional business, for training. The
        /// service refuses a company that already holds any records.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoadSampleData(int id)
        {
            try
            {
                var result = await _sampleDataService.FillCompanyAsync(id, User.GetAppUserId());
                TempData["SuccessMessage"] = result.Summary +
                    (result.Warnings.Count > 0 ? " Skipped: " + string.Join(" | ", result.Warnings.Take(5)) : string.Empty);
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Edit), new { id });
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
