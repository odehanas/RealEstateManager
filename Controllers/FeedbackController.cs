using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    [Authorize]
    public class FeedbackController : Controller
    {
        private readonly FeedbackService _feedbackService;

        public FeedbackController(FeedbackService feedbackService)
        {
            _feedbackService = feedbackService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = await _feedbackService.BuildUserPageAsync(User);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(FeedbackPageViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var refreshed = await _feedbackService.BuildUserPageAsync(User);
                refreshed.Form = model.Form;
                return View("Index", refreshed);
            }

            await _feedbackService.SubmitAsync(User, model.Form);
            TempData["SuccessMessage"] = "Thank you. Your feedback has been submitted.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "SystemAdmin")]
        [HttpGet]
        public async Task<IActionResult> Manage([FromQuery] FeedbackAdminFilterViewModel filter)
        {
            var model = await _feedbackService.GetAdminListAsync(filter ?? new FeedbackAdminFilterViewModel());
            return View(model);
        }

        [Authorize(Roles = "SystemAdmin")]
        [HttpGet]
        public async Task<IActionResult> ExportCsv([FromQuery] FeedbackAdminFilterViewModel filter)
        {
            var file = await _feedbackService.ExportCsvAsync(filter ?? new FeedbackAdminFilterViewModel());
            var fileName = $"feedback-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
            return File(file, "text/csv", fileName);
        }

        [Authorize(Roles = "SystemAdmin")]
        [HttpGet]
        public async Task<IActionResult> Review(int id)
        {
            try
            {
                var model = await _feedbackService.GetReviewAsync(id);
                return View(model);
            }
            catch (InvalidOperationException)
            {
                return NotFound();
            }
        }

        [Authorize(Roles = "SystemAdmin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Review(FeedbackReviewViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var reviewerUserId = User.GetAppUserId()
                    ?? throw new InvalidOperationException("Current user is not recognized.");

                await _feedbackService.ReviewAsync(model.Id, reviewerUserId, model);
                TempData["SuccessMessage"] = "Feedback status updated successfully.";
                return RedirectToAction(nameof(Manage));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }
    }
}
