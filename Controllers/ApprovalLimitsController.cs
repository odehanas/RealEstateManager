using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The owners' controls over payment authority: whether payments need approval, the
    /// levels and their limits, who sits on which level, and temporary delegation while an
    /// owner is away. Needs Owner.Oversight.
    /// </summary>
    public class ApprovalLimitsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ApprovalAuthorityService _authority;

        public ApprovalLimitsController(ApplicationDbContext db, ApprovalAuthorityService authority)
        {
            _db = db;
            _authority = authority;
        }

        public sealed class UserRow
        {
            public int AppUserId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public int? LevelId { get; set; }
            public string? Authority { get; set; }
        }

        public sealed class LimitsViewModel
        {
            public Company Company { get; set; } = new();
            public List<ApprovalLevel> Levels { get; set; } = new();
            public List<UserRow> Users { get; set; } = new();
            public List<ApprovalDelegation> Delegations { get; set; } = new();
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var levels = await _authority.GetLevelsAsync(companyId);
            var links = await _db.AppUserCompanies.IgnoreQueryFilters().AsNoTracking()
                .Include(x => x.AppUser)
                .Where(x => x.CompanyId == companyId && x.AppUser != null && x.AppUser.IsActive)
                .OrderBy(x => x.AppUser!.DisplayName)
                .ToListAsync();

            var users = new List<UserRow>();
            foreach (var l in links)
            {
                var authority = await _authority.GetAuthorityAsync(l.AppUserId, companyId, l.AppUser!.IsSystemAdmin);
                users.Add(new UserRow
                {
                    AppUserId = l.AppUserId,
                    Name = l.AppUser.DisplayName,
                    Username = l.AppUser.Username,
                    LevelId = l.ApprovalLevelId,
                    Authority = authority == null ? "None" : $"{authority.Describe()} ({authority.Source})"
                });
            }

            return View(new LimitsViewModel
            {
                Company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId),
                Levels = levels,
                Users = users,
                Delegations = await _db.ApprovalDelegations.AsNoTracking()
                    .Where(x => x.CompanyId == companyId)
                    .OrderByDescending(x => x.StartDate).Take(50).ToListAsync()
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Switches(bool requirePaymentApproval, bool allowSelfRelease)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstAsync(x => x.Id == companyId);
            company.RequirePaymentApproval = requirePaymentApproval;
            company.AllowSelfReleaseWithinLimit = allowSelfRelease;
            await _db.SaveChangesAsync();
            Flash(true, requirePaymentApproval
                ? "Payment approval is ON: every payment needs someone whose limit covers it."
                : "Payment approval is OFF: payments post when they are saved.");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveLevels(List<int> ids, List<string> names, List<string?> limits, string? newName, string? newLimit)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var levels = await _db.ApprovalLevels.Where(x => x.CompanyId == companyId).ToListAsync();

            for (var i = 0; i < ids.Count && i < names.Count; i++)
            {
                var level = levels.FirstOrDefault(x => x.Id == ids[i]);
                if (level == null || string.IsNullOrWhiteSpace(names[i]))
                    continue;
                level.Name = names[i].Trim();
                level.MaxAmount = ParseLimit(i < limits.Count ? limits[i] : null);
            }

            if (!string.IsNullOrWhiteSpace(newName))
            {
                if (levels.Any(x => x.Name.Equals(newName.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    Flash(false, $"A level called {newName.Trim()} already exists.");
                    return RedirectToAction(nameof(Index));
                }

                _db.ApprovalLevels.Add(new ApprovalLevel
                {
                    CompanyId = companyId,
                    Name = newName.Trim(),
                    MaxAmount = ParseLimit(newLimit),
                    SortOrder = (levels.Count + 1) * 10
                });
            }

            // Order the ladder by limit, unlimited last, so "the lowest level that covers" is well defined.
            await _db.SaveChangesAsync();
            var ordered = (await _db.ApprovalLevels.Where(x => x.CompanyId == companyId).ToListAsync())
                .OrderBy(x => x.MaxAmount ?? decimal.MaxValue).ToList();
            for (var i = 0; i < ordered.Count; i++)
                ordered[i].SortOrder = (i + 1) * 10;
            await _db.SaveChangesAsync();

            Flash(true, "Approval levels saved. New limits apply to payments entered from now on.");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignLevels(List<int> userIds, List<int?> levelIds)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var links = await _db.AppUserCompanies.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToListAsync();
            var validLevels = await _db.ApprovalLevels.Where(x => x.CompanyId == companyId).Select(x => x.Id).ToListAsync();

            for (var i = 0; i < userIds.Count && i < levelIds.Count; i++)
            {
                var link = links.FirstOrDefault(x => x.AppUserId == userIds[i]);
                if (link != null)
                    link.ApprovalLevelId = levelIds[i].HasValue && validLevels.Contains(levelIds[i]!.Value) ? levelIds[i] : null;
            }

            await _db.SaveChangesAsync();
            Flash(true, "Approval levels assigned.");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delegate(int toAppUserId, string? maxAmount, DateTime startDate, DateTime endDate, string? reason)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var me = User.GetAppUserId() ?? 0;
            var to = await _db.AppUserCompanies.IgnoreQueryFilters().Include(x => x.AppUser)
                .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.AppUserId == toAppUserId);

            string? problem = null;
            if (to?.AppUser == null) problem = "Choose who receives the authority.";
            else if (toAppUserId == me) problem = "You cannot delegate to yourself.";
            else if (endDate.Date < startDate.Date) problem = "The delegation cannot end before it starts.";
            else if (endDate.Date > startDate.Date.AddDays(180)) problem = "A delegation can run for at most 180 days; renew it if needed.";
            else if (string.IsNullOrWhiteSpace(reason)) problem = "Give the reason, e.g. \"Owner travelling 1-15 August\".";

            // Nobody can lend more authority than they hold.
            var mine = await _authority.GetAuthorityAsync(me, companyId, User.IsInRole("SystemAdmin"));
            var limit = ParseLimit(maxAmount) ?? mine?.Limit;
            if (problem == null && (mine == null || (mine.Limit != null && (limit == null || limit > mine.Limit))))
                problem = $"You can delegate at most your own authority ({mine?.Describe() ?? "none"}).";

            if (problem != null)
            {
                Flash(false, problem);
                return RedirectToAction(nameof(Index));
            }

            _db.ApprovalDelegations.Add(new ApprovalDelegation
            {
                CompanyId = companyId,
                FromAppUserId = me,
                FromName = CurrentUserName(),
                ToAppUserId = toAppUserId,
                ToName = to!.AppUser!.DisplayName,
                MaxAmount = limit,
                StartDate = startDate.Date,
                EndDate = endDate.Date,
                Reason = reason!.Trim(),
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            Flash(true, $"{to.AppUser.DisplayName} may release payments {(limit == null ? "of any amount" : $"up to {limit:N2}")} from {startDate:dd/MM/yyyy} to {endDate:dd/MM/yyyy}.");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Revoke(int id)
        {
            var delegation = await _db.ApprovalDelegations.FirstOrDefaultAsync(x => x.Id == id);
            if (delegation != null && delegation.RevokedAt == null)
            {
                delegation.RevokedAt = DateTime.UtcNow;
                delegation.RevokedByName = CurrentUserName();
                await _db.SaveChangesAsync();
                Flash(true, $"Delegation to {delegation.ToName} ended.");
            }

            return RedirectToAction(nameof(Index));
        }

        private static decimal? ParseLimit(string? text) =>
            decimal.TryParse(text?.Replace(",", string.Empty), out var v) && v > 0 ? v : null;

        private void Flash(bool success, string message) => TempData[success ? "ApprovalSuccess" : "ApprovalError"] = message;

        private string CurrentUserName() =>
            User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value ?? "User";
    }
}
