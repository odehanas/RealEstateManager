using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// QuickBooks-Desktop style workflow home screen. Each tile links to the
    /// screen that performs that step of the operating cycle:
    /// Properties -> Units -> Tenants -> Contracts -> PDCs -> Invoicing -> Payments Received.
    ///
    /// No permission is required to open the page itself; individual tiles are
    /// hidden by the view according to the signed-in user's permissions.
    /// </summary>
    [Authorize]
    public class WorkflowController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }
    }
}
