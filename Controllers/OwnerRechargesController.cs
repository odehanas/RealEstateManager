using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Costs recharged to the owners of managed properties, and the tax invoice the
    /// company issues for each (cost plus 5% VAT, under the company's TRN).
    /// </summary>
    public class OwnerRechargesController : Controller
    {
        private readonly ApplicationDbContext _db;

        public OwnerRechargesController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? fromDate, DateTime? toDate, string? owner)
        {
            var to = (toDate ?? DateTime.Today).Date;
            var from = (fromDate ?? new DateTime(to.Year, 1, 1)).Date;
            if (from > to)
                (from, to) = (to, from);

            var query = _db.OwnerRecharges.AsNoTracking()
                .Include(x => x.Property).Include(x => x.JournalEntry)
                .Where(x => x.RechargeDate >= from && x.RechargeDate <= to);
            if (!string.IsNullOrWhiteSpace(owner))
                query = query.Where(x => x.OwnerName == owner);

            ViewBag.From = from;
            ViewBag.To = to;
            ViewBag.Owner = owner;
            ViewBag.Owners = await _db.OwnerRecharges.AsNoTracking().Select(x => x.OwnerName).Distinct().OrderBy(x => x).ToListAsync();
            return View(await query.OrderByDescending(x => x.RechargeDate).ThenByDescending(x => x.Id).ToListAsync());
        }

        /// <summary>The recharge tax invoice to the owner, ready to print.</summary>
        [HttpGet]
        public async Task<IActionResult> Document(int id)
        {
            var recharge = await _db.OwnerRecharges.AsNoTracking()
                .Include(x => x.Property).Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (recharge == null)
                return NotFound();

            ViewBag.Company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == recharge.CompanyId);
            ViewBag.Unit = recharge.UnitId.HasValue
                ? await _db.Units.AsNoTracking().Where(x => x.Id == recharge.UnitId).Select(x => x.UnitNumber).FirstOrDefaultAsync()
                : null;
            return View(recharge);
        }
    }
}
