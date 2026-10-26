using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Cross references from report lines back to where the figures came from: the journal
    /// entry behind a line, and the original document behind the entry.
    /// </summary>
    public class DocumentsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly DocumentLinkService _links;
        private readonly PermissionService _permissions;

        public DocumentsController(ApplicationDbContext db, DocumentLinkService links, PermissionService permissions)
        {
            _db = db;
            _links = links;
            _permissions = permissions;
        }

        /// <summary>The journal entry: header, every line, and a way on to its document.</summary>
        [HttpGet]
        public async Task<IActionResult> Entry(int id)
        {
            var entry = await _db.JournalEntries.AsNoTracking()
                .Include(x => x.AccountingPeriod)
                .Include(x => x.Lines).ThenInclude(x => x.Account)
                .Include(x => x.Lines).ThenInclude(x => x.Property)
                .Include(x => x.Lines).ThenInclude(x => x.Tenant)
                .Include(x => x.Lines).ThenInclude(x => x.Vendor)
                .AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (entry == null)
                return NotFound();

            var target = await _links.FindAsync(entry.SourceType, entry.SourceId);
            ViewBag.DocumentUrl = target is { } t && await CanOpenAsync(t.Controller, t.Action)
                ? Url.Action(t.Action, t.Controller, new { id = t.Id })
                : null;
            ViewBag.HasDocumentPage = target != null;
            ViewBag.CanOpenLedger = await CanOpenAsync("Journal", "GeneralLedger");

            return View(entry);
        }

        /// <summary>
        /// Opens the original document of a journal entry (entryId), or of a source
        /// (type + id). When the document has no page of its own, or the user may not open
        /// it, the journal entry is shown instead.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Open(int? entryId, string? type, int? id)
        {
            if (entryId.HasValue)
            {
                var entry = await _db.JournalEntries.AsNoTracking()
                    .Where(x => x.Id == entryId.Value)
                    .Select(x => new { x.SourceType, x.SourceId })
                    .FirstOrDefaultAsync();
                if (entry == null)
                    return NotFound();

                type = entry.SourceType;
                id = entry.SourceId;
            }

            var target = await _links.FindAsync(type, id);
            if (target is { } t && await CanOpenAsync(t.Controller, t.Action))
                return RedirectToAction(t.Action, t.Controller, new { id = t.Id });

            entryId ??= string.IsNullOrWhiteSpace(type) || id == null
                ? null
                : await _db.JournalEntries.AsNoTracking()
                    .Where(x => x.SourceType == type && x.SourceId == id)
                    .OrderBy(x => x.Id)
                    .Select(x => (int?)x.Id)
                    .FirstOrDefaultAsync();

            return entryId.HasValue ? RedirectToAction(nameof(Entry), new { id = entryId }) : NotFound();
        }

        private async Task<bool> CanOpenAsync(string controller, string action)
        {
            var required = PermissionCatalog.ResolvePermissions(controller, action);
            return required.Count == 0 || await _permissions.HasAnyPermissionAsync(User, required.ToArray());
        }
    }
}
