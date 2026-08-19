using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class TenantStatementsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly TenantStatementService _tenantStatementService;

        public TenantStatementsController(
            ApplicationDbContext db,
            TenantStatementService tenantStatementService)
        {
            _db = db;
            _tenantStatementService = tenantStatementService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? tenantId, int? leaseId, DateTime? fromDate, DateTime? toDate)
        {
            var reportFrom = (fromDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
            var reportTo = (toDate ?? DateTime.Today).Date;

            await LoadLookupsAsync(tenantId);

            var model = new TenantStatementViewModel
            {
                TenantId = tenantId,
                LeaseId = leaseId,
                FromDate = reportFrom,
                ToDate = reportTo
            };

            if (!tenantId.HasValue)
            {
                return View(model);
            }

            try
            {
                model = await _tenantStatementService.GetTenantStatementAsync(
                    tenantId.Value,
                    leaseId,
                    reportFrom,
                    reportTo);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            return View(model);
        }

        private async Task LoadLookupsAsync(int? selectedTenantId)
        {
            var tenants = await _db.Tenants
                .AsNoTracking()
                .OrderBy(x => x.Code)
                .ToListAsync();

            var leasesQuery = _db.Leases
                .AsNoTracking()
                .Include(x => x.Unit)
                    .ThenInclude(x => x!.Property)
                .Include(x => x.Tenant)
                .AsQueryable();

            if (selectedTenantId.HasValue)
            {
                leasesQuery = leasesQuery.Where(x => x.TenantId == selectedTenantId.Value);
            }

            var leases = await leasesQuery
                .OrderByDescending(x => x.StartDate)
                .ToListAsync();

            ViewBag.Tenants = tenants;
            ViewBag.Leases = leases;
        }
    }
}
