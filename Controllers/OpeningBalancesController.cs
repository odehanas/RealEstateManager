using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class OpeningBalancesController : Controller
    {
        private readonly OpeningBalanceService _openingBalanceService;

        public OpeningBalancesController(OpeningBalanceService openingBalanceService)
        {
            _openingBalanceService = openingBalanceService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = await _openingBalanceService.GetReconciliationAsync();
            return View(model);
        }
    }
}
