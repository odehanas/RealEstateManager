using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The kinds of maintenance work a company sorts its jobs by. A company that has
    /// never opened the list works with the standard one
    /// (<see cref="ServiceRequestCategories.Maintenance"/>); opening the list copies the
    /// standard entries in, and from then on the company's own list is what is offered.
    /// </summary>
    public class MaintenanceCategoryService
    {
        /// <summary>Kept at the bottom of every list, whatever is added.</summary>
        private const string Other = "Other";

        private readonly ApplicationDbContext _db;
        private readonly ICurrentCompanyAccessor _company;

        public MaintenanceCategoryService(ApplicationDbContext db, ICurrentCompanyAccessor company)
        {
            _db = db;
            _company = company;
        }

        /// <summary>
        /// The kinds of work offered on a new job, for the company of whoever is signed in.
        /// <paramref name="include"/> is the kind a job already has: it stays in the list
        /// even when it has since been switched off, so opening the job does not lose it.
        /// </summary>
        public async Task<List<string>> NamesAsync(string? include = null)
        {
            var names = _company.CompanyId.HasValue
                ? await NamesAsync(_db, _company.CompanyId.Value)
                : ServiceRequestCategories.Maintenance.ToList();

            if (!string.IsNullOrWhiteSpace(include) && !names.Contains(include, StringComparer.OrdinalIgnoreCase))
                names.Add(include);

            return names;
        }

        public static async Task<List<string>> NamesAsync(ApplicationDbContext db, int companyId)
        {
            var rows = await db.MaintenanceCategories.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .Select(x => new { x.Name, x.IsActive, x.SortOrder })
                .ToListAsync();

            if (rows.Count == 0)
                return ServiceRequestCategories.Maintenance.ToList();

            return rows.Where(x => x.IsActive)
                .OrderBy(x => x.Name == Other ? 1 : 0).ThenBy(x => x.SortOrder).ThenBy(x => x.Name)
                .Select(x => x.Name)
                .ToList();
        }

        /// <summary>Whether a new job of this company may be given this kind of work.</summary>
        public static async Task<bool> IsOfferedAsync(ApplicationDbContext db, int companyId, string name) =>
            (await NamesAsync(db, companyId)).Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);

        public sealed record Row(int Id, string Name, bool IsActive, int Jobs, int Plans);

        /// <summary>The company's list for the setup screen, with how much each entry is used.</summary>
        public async Task<List<Row>> ListAsync(int companyId, string? byName)
        {
            if (!await _db.MaintenanceCategories.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId))
            {
                var order = 0;
                foreach (var name in ServiceRequestCategories.Maintenance)
                {
                    _db.MaintenanceCategories.Add(new MaintenanceCategory
                    {
                        CompanyId = companyId,
                        Name = name,
                        SortOrder = order += 10,
                        CreatedByName = byName
                    });
                }
                await _db.SaveChangesAsync();
            }

            var categories = await _db.MaintenanceCategories.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .ToListAsync();

            var jobs = await _db.TenantServiceRequests.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Category != null)
                .GroupBy(x => x.Category!)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .ToListAsync();
            var plans = await _db.MaintenancePlans.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Category != null)
                .GroupBy(x => x.Category!)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .ToListAsync();

            return categories
                .OrderBy(x => x.Name == Other ? 1 : 0).ThenBy(x => x.SortOrder).ThenBy(x => x.Name)
                .Select(x => new Row(
                    x.Id, x.Name, x.IsActive,
                    jobs.Where(j => string.Equals(j.Name, x.Name, StringComparison.OrdinalIgnoreCase)).Sum(j => j.Count),
                    plans.Where(p => string.Equals(p.Name, x.Name, StringComparison.OrdinalIgnoreCase)).Sum(p => p.Count)))
                .ToList();
        }

        /// <summary>Adds a kind of work. Returns what is wrong, or null when it was added.</summary>
        public async Task<string?> AddAsync(int companyId, string? name, string? byName)
        {
            var (clean, error) = Clean(name);
            if (error != null)
                return error;

            var existing = await _db.MaintenanceCategories.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId)
                .ToListAsync();
            var same = existing.FirstOrDefault(x => string.Equals(x.Name, clean, StringComparison.OrdinalIgnoreCase));
            if (same != null)
            {
                return same.IsActive
                    ? "That kind of work is already in the list."
                    : "That kind of work is in the list but switched off. Switch it on again instead.";
            }

            _db.MaintenanceCategories.Add(new MaintenanceCategory
            {
                CompanyId = companyId,
                Name = clean,
                SortOrder = (existing.Count == 0 ? 0 : existing.Max(x => x.SortOrder)) + 10,
                CreatedByName = byName
            });
            await _db.SaveChangesAsync();
            return null;
        }

        /// <summary>
        /// Renames a kind of work, on the list and on every job and planned maintenance
        /// entry of the company that has it, so past jobs and reports follow the new name.
        /// </summary>
        public async Task<string?> RenameAsync(int companyId, int id, string? name)
        {
            var (clean, error) = Clean(name);
            if (error != null)
                return error;

            var category = await _db.MaintenanceCategories.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
            if (category == null)
                return "That kind of work was not found.";
            if (category.Name == clean)
                return null;

            if (await _db.MaintenanceCategories.IgnoreQueryFilters()
                    .AnyAsync(x => x.CompanyId == companyId && x.Id != id && x.Name == clean))
                return "Another kind of work already has that name.";

            var old = category.Name;
            category.Name = clean;

            var jobs = await _db.TenantServiceRequests.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.Category == old)
                .ToListAsync();
            foreach (var job in jobs)
                job.Category = clean;

            var plans = await _db.MaintenancePlans.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.Category == old)
                .ToListAsync();
            foreach (var plan in plans)
                plan.Category = clean;

            await _db.SaveChangesAsync();
            return null;
        }

        /// <summary>Switches a kind of work off (no longer offered) or back on.</summary>
        public async Task<string?> ToggleAsync(int companyId, int id)
        {
            var category = await _db.MaintenanceCategories.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
            if (category == null)
                return "That kind of work was not found.";

            if (category.IsActive &&
                await _db.MaintenanceCategories.IgnoreQueryFilters()
                    .CountAsync(x => x.CompanyId == companyId && x.IsActive) <= 1)
                return "Keep at least one kind of work switched on.";

            category.IsActive = !category.IsActive;
            await _db.SaveChangesAsync();
            return null;
        }

        private static (string Name, string? Error) Clean(string? name)
        {
            var clean = string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (clean.Length == 0)
                return (clean, "Type the name of the kind of work.");
            if (clean.Length > MaintenanceCategory.MaxNameLength)
                return (clean, $"Keep the name within {MaintenanceCategory.MaxNameLength} characters.");
            return (clean, null);
        }
    }
}
