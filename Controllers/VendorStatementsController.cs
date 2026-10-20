using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>Reporting ▸ Vendor Statement: one vendor's account with us, for a period.</summary>
    public class VendorStatementsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly VendorStatementService _statements;

        public VendorStatementsController(ApplicationDbContext db, VendorStatementService statements)
        {
            _db = db;
            _statements = statements;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? vendorId, DateTime? fromDate, DateTime? toDate)
        {
            var from = (fromDate ?? new DateTime(DateTime.Today.Year, 1, 1)).Date;
            var to = (toDate ?? DateTime.Today).Date;

            ViewBag.Vendors = await _db.Vendors.AsNoTracking().OrderBy(x => x.Code).ToListAsync();

            var model = new VendorStatementViewModel { VendorId = vendorId, FromDate = from, ToDate = to };
            if (!vendorId.HasValue)
                return View(model);

            try
            {
                model = await _statements.GetVendorStatementAsync(vendorId.Value, from, to);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                model = new VendorStatementViewModel { FromDate = from, ToDate = to };
            }

            return View(model);
        }
    }
}
