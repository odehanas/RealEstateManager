using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;
using RealEstateManager.Services.Reporting;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Build your own report: pick a data source, columns, filters, a grouping and a
    /// chart, run it, and save it for later or for the whole company.
    /// </summary>
    public class ReportBuilderController : Controller
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly ReportEngine _engine;
        private readonly ApplicationDbContext _db;
        private readonly AppAuthService _auth;

        public ReportBuilderController(ReportEngine engine, ApplicationDbContext db, AppAuthService auth)
        {
            _engine = engine;
            _db = db;
            _auth = auth;
        }

        public sealed class SaveRequest
        {
            public int? Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public bool IsShared { get; set; }
            public ReportEngine.ReportDefinition Definition { get; set; } = new();
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? id)
        {
            ViewBag.Sources = await _engine.SourcesAsync(User);
            ViewBag.Saved = await SavedListAsync();
            ViewBag.OpenId = id;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Fields(string source)
        {
            var fields = await _engine.FieldsAsync(User, source);
            return fields == null ? NotFound(new { error = "You do not have access to that data source." }) : Json(fields);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Run([FromBody] ReportEngine.ReportDefinition definition) =>
            Json(await _engine.RunAsync(User, definition));

        [HttpGet]
        public async Task<IActionResult> Load(int id)
        {
            var userId = User.GetAppUserId();
            var report = await _db.SavedReports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && (x.IsShared || x.AppUserId == userId));
            if (report == null)
                return NotFound(new { error = "That report was not found." });

            return Json(new
            {
                report.Id,
                report.Name,
                report.IsShared,
                mine = report.AppUserId == userId,
                definition = JsonSerializer.Deserialize<ReportEngine.ReportDefinition>(report.DefinitionJson, Json)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save([FromBody] SaveRequest request)
        {
            var name = (request.Name ?? string.Empty).Trim();
            if (name.Length == 0)
                return BadRequest(new { error = "Give the report a name." });
            if (name.Length > 150)
                name = name[..150];

            var userId = User.GetAppUserId() ?? 0;
            var companyId = User.GetRequiredCurrentCompanyId();

            // Overwrite only your own report; saving someone else's shared report makes your own copy.
            var report = request.Id.HasValue
                ? await _db.SavedReports.FirstOrDefaultAsync(x => x.Id == request.Id.Value && x.AppUserId == userId)
                : null;
            if (report == null)
            {
                report = new SavedReport { CompanyId = companyId, AppUserId = userId, CreatedAt = DateTime.UtcNow };
                _db.SavedReports.Add(report);
            }

            report.Name = name;
            report.IsShared = request.IsShared;
            report.OwnerName = User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.Identity?.Name ?? "User";
            report.DefinitionJson = JsonSerializer.Serialize(request.Definition, Json);
            report.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Json(new { report.Id, saved = await SavedListAsync() });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = User.GetAppUserId() ?? 0;
            var companyId = User.GetRequiredCurrentCompanyId();
            var isAdmin = User.IsInRole("SystemAdmin") || await _auth.IsCompanyAdminAsync(userId, companyId);

            var report = await _db.SavedReports.FirstOrDefaultAsync(x => x.Id == id && (x.AppUserId == userId || isAdmin));
            if (report == null)
                return NotFound(new { error = "You can only delete your own reports." });

            _db.SavedReports.Remove(report);
            await _db.SaveChangesAsync();
            return Json(new { saved = await SavedListAsync() });
        }

        private async Task<object> SavedListAsync()
        {
            var userId = User.GetAppUserId();
            return await _db.SavedReports.AsNoTracking()
                .Where(x => x.AppUserId == userId || x.IsShared)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name, x.IsShared, owner = x.OwnerName, mine = x.AppUserId == userId })
                .ToListAsync();
        }
    }
}
