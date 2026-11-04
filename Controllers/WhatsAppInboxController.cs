using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Services;
using RealEstateManager.Services.WhatsApp;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The staff review list: messages from WhatsApp that could not safely go on one
    /// tenant's record. Logging one for a tenant happens on the ordinary Log a Request screen.
    /// </summary>
    public class WhatsAppInboxController : Controller
    {
        private readonly WhatsAppIntakeService _intake;

        public WhatsAppInboxController(WhatsAppIntakeService intake)
        {
            _intake = intake;
        }

        [HttpGet]
        public async Task<IActionResult> Index(bool handled = false)
        {
            var model = await _intake.GetInboxAsync(User.GetRequiredCurrentCompanyId(), handled);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Dismiss(int id)
        {
            var dismissed = await _intake.DismissAsync(User.GetRequiredCurrentCompanyId(), id, User.GetAppUserId());

            TempData[dismissed ? "RequestSuccess" : "RequestError"] = dismissed
                ? "Message dismissed. No request was created."
                : "Someone has already dealt with this WhatsApp message.";

            return RedirectToAction(nameof(Index));
        }
    }
}
