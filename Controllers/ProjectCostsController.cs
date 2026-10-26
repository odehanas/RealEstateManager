using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Project Costs &amp; WIP: spend per development project with the VAT split, what is
    /// committed and paid, and the reconciliation of each WIP account to its projects.
    /// </summary>
    public class ProjectCostsController : Controller
    {
        private readonly ProjectCostReportService _report;
        private readonly ApplicationDbContext _db;

        public ProjectCostsController(ProjectCostReportService report, ApplicationDbContext db)
        {
            _report = report;
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? asOf, int? projectId)
        {
            ViewBag.ProjectOptions = await _db.DevelopmentProjects.AsNoTracking().OrderBy(x => x.Code)
                .Select(x => new { x.Id, Label = x.Code + " " + x.Name }).ToListAsync();
            return View(await _report.BuildAsync(User.GetRequiredCurrentCompanyId(), asOf ?? DateTime.Today, projectId));
        }
    }
}
