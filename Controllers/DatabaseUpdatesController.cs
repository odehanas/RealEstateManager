using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    // Read-only: which database scripts the application has applied, and why it stopped if
    // one failed. Platform owner only - the database is shared by every company.
    [Authorize(Policy = PlatformOwnerRequirement.PolicyName)]
    public class DatabaseUpdatesController : Controller
    {
        private readonly SchemaScriptRunner _runner;
        private readonly SchemaUpdateStatus _status;

        public DatabaseUpdatesController(SchemaScriptRunner runner, SchemaUpdateStatus status)
        {
            _runner = runner;
            _status = status;
        }

        public async Task<IActionResult> Index()
        {
            IReadOnlyList<SchemaScriptRunner.AppliedScript>? history = null;
            string? historyError = null;
            try
            {
                history = await _runner.ReadHistoryAsync(HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                historyError = ex.Message;
            }

            var included = new List<string>();
            try
            {
                included = _runner.LoadEmbeddedScripts().Select(s => s.Name).ToList();
            }
            catch (InvalidOperationException ex)
            {
                historyError ??= ex.Message;
            }

            var applied = new HashSet<string>(history?.Select(h => h.ScriptName) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            return View(new DatabaseUpdatesViewModel
            {
                Status = _status,
                History = history,
                HistoryError = historyError,
                IncludedCount = included.Count,
                NotApplied = included.Where(n => !applied.Contains(n)).ToList()
            });
        }
    }
}
