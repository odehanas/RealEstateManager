using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The payment authority matrix: who may release how much. Each user sits on one of
    /// the company's approval levels (Accountant 1,000 · Manager 5,000 · Owner unlimited,
    /// by default); an active delegation can lift a person above their level for a period.
    /// </summary>
    public class ApprovalAuthorityService
    {
        /// <summary>Journals that pay money out and so need releasing when payment approval is on.</summary>
        public static readonly HashSet<string> ControlledSourceTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "VendorPayment",
            "PettyCashExpense",
            "PettyCashReplenishment",
            "OwnerSettlement",
            "OwnerWithdrawal",
            "DevelopmentCost"
        };

        /// <summary>Always checked by a second person, however small: the person who spends petty cash never approves it.</summary>
        public static readonly HashSet<string> AlwaysSecondPerson = new(StringComparer.OrdinalIgnoreCase)
        {
            "PettyCashExpense",
            "PettyCashReplenishment"
        };

        private readonly ApplicationDbContext _db;

        public ApprovalAuthorityService(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>Creates the standard three levels for a company that has none.</summary>
        public async Task<List<ApprovalLevel>> GetLevelsAsync(int companyId)
        {
            var levels = await _db.ApprovalLevels.Where(x => x.CompanyId == companyId).OrderBy(x => x.SortOrder).ToListAsync();
            if (levels.Count > 0)
                return levels;

            levels = new List<ApprovalLevel>
            {
                new() { CompanyId = companyId, Name = "Accountant", MaxAmount = 1000m, SortOrder = 10 },
                new() { CompanyId = companyId, Name = "Manager", MaxAmount = 5000m, SortOrder = 20 },
                new() { CompanyId = companyId, Name = "Owner", MaxAmount = null, SortOrder = 30 }
            };
            _db.ApprovalLevels.AddRange(levels);
            await _db.SaveChangesAsync();
            return levels;
        }

        public sealed record Authority(decimal? Limit, string Source)
        {
            public bool Covers(decimal amount) => Limit == null || Limit.Value >= amount;
            public string Describe() => Limit == null ? "no limit" : $"up to {Limit.Value:N2}";
        }

        /// <summary>
        /// The most a user may release today: their level's limit, or a running delegation if
        /// that gives more. A system administrator is not limited. Nothing means no authority.
        /// </summary>
        public async Task<Authority?> GetAuthorityAsync(int userId, int companyId, bool isSystemAdmin = false)
        {
            if (isSystemAdmin)
                return new Authority(null, "System administrator");

            var link = await _db.AppUserCompanies.IgnoreQueryFilters().AsNoTracking()
                .Include(x => x.ApprovalLevel)
                .FirstOrDefaultAsync(x => x.AppUserId == userId && x.CompanyId == companyId);

            Authority? best = link?.ApprovalLevel == null ? null : new Authority(link.ApprovalLevel.MaxAmount, link.ApprovalLevel.Name);

            var today = DateTime.Today;
            var delegations = await _db.ApprovalDelegations.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.ToAppUserId == userId && x.RevokedAt == null &&
                            x.StartDate <= today && x.EndDate >= today)
                .ToListAsync();

            foreach (var d in delegations)
            {
                var candidate = new Authority(d.MaxAmount, $"delegated by {d.FromName} until {d.EndDate:dd/MM/yyyy}");
                if (best == null || (best.Limit != null && (candidate.Limit == null || candidate.Limit > best.Limit)))
                    best = candidate;
            }

            return best;
        }

        /// <summary>The lowest level whose limit covers an amount - the one that has to approve it.</summary>
        public async Task<string> RequiredLevelAsync(int companyId, decimal amount)
        {
            var levels = await GetLevelsAsync(companyId);
            return levels.FirstOrDefault(x => x.MaxAmount == null || x.MaxAmount.Value >= amount)?.Name
                   ?? levels.LastOrDefault()?.Name
                   ?? "Owner";
        }

        public async Task<bool> IsPaymentApprovalOnAsync(int companyId) =>
            await _db.Companies.AsNoTracking().Where(x => x.Id == companyId).Select(x => x.RequirePaymentApproval).FirstOrDefaultAsync();
    }
}
