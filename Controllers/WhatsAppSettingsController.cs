using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;
using RealEstateManager.Services;
using RealEstateManager.Services.WhatsApp;

namespace RealEstateManager.Controllers
{
    /// <summary>Connects the current company to its own WhatsApp Business number.</summary>
    public class WhatsAppSettingsController : Controller
    {
        private readonly WhatsAppConnectionService _connections;

        public WhatsAppSettingsController(WhatsAppConnectionService connections)
        {
            _connections = connections;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = await _connections.BuildSettingsAsync(User.GetRequiredCurrentCompanyId());
            return ShowForm(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(WhatsAppSettingsViewModel model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();

            if (await _connections.SaveAsync(ModelState, companyId, model, User.GetAppUserId()))
            {
                TempData["RequestSuccess"] = "WhatsApp connection saved.";
                return RedirectToAction(nameof(Index));
            }

            var saved = await _connections.BuildSettingsAsync(companyId);
            model.Exists = saved.Exists;
            model.PublicKey = saved.PublicKey;
            model.VerifyToken = saved.VerifyToken;

            // Never send a typed secret back to the page.
            model.AppSecret = null;
            ModelState.Remove(nameof(model.AppSecret));
            return ShowForm(model);
        }

        private IActionResult ShowForm(WhatsAppSettingsViewModel model)
        {
            if (model.PublicKey != null)
                ViewBag.CallbackUrl = $"{Request.Scheme}://{Request.Host}/webhooks/whatsapp/{model.PublicKey}";

            return View("Index", model);
        }
    }
}
