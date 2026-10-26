using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The owners' view of the business day by day: cash, what was posted, which payments
    /// were released and by whom, what staff changed, and anything unusual. Read only.
    /// Needs Owner.Oversight.
    /// </summary>
    public class OversightController : Controller
    {
        private readonly ApplicationDbContext _db;

        public OversightController(ApplicationDbContext db)
        {
            _db = db;
        }

        public sealed class PostingRow
        {
            public int Id { get; set; }
            public DateTime EntryDate { get; set; }
            public DateTime? PostedAt { get; set; }
            public string EntryNumber { get; set; } = string.Empty;
            public string? SourceType { get; set; }
            public string Description { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public string? PostedBy { get; set; }
        }

        public sealed class OversightViewModel
        {
            public DateTime From { get; set; }
            public DateTime To { get; set; }
            public decimal CashBalance { get; set; }
            public decimal MoneyIn { get; set; }
            public decimal MoneyOut { get; set; }
            public int PendingPayments { get; set; }
            public decimal PendingPaymentAmount { get; set; }
            public decimal LargeAmount { get; set; }
            public List<PostingRow> Postings { get; set; } = new();
            public List<PaymentApproval> Payments { get; set; } = new();
            public List<AuditLog> Changes { get; set; } = new();
            public List<AuditLog> Failures { get; set; } = new();
            public List<AppUser> LockedUsers { get; set; } = new();
            public List<ApprovalDelegation> ActiveDelegations { get; set; } = new();
            public List<JournalEntry> Drafts { get; set; } = new();
        }

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? from, DateTime? to, decimal large = 10000m)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var end = (to ?? DateTime.Today).Date;
            var start = (from ?? end.AddDays(-6)).Date;
            var endExclusive = end.AddDays(1);
            var startUtc = start.ToUniversalTime();
            var endUtc = endExclusive.ToUniversalTime();

            var cashLines = _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.JournalEntry != null && x.JournalEntry.CompanyId == companyId && x.JournalEntry.IsPosted &&
                            x.Account != null && x.Account.Code.StartsWith("111"));

            // Moving money between the company's own cash and bank accounts (topping up
            // petty cash, say) is neither money in nor money out.
            var cashMovements = cashLines.Where(x => x.JournalEntry!.EntryDate >= start && x.JournalEntry.EntryDate < endExclusive &&
                                                     x.JournalEntry.Lines.Any(l => l.Account != null && !l.Account.Code.StartsWith("111")));

            var model = new OversightViewModel
            {
                From = start,
                To = end,
                LargeAmount = large,
                CashBalance = await cashLines.SumAsync(x => (decimal?)(x.Debit - x.Credit)) ?? 0m,
                MoneyIn = await cashMovements.SumAsync(x => (decimal?)x.Debit) ?? 0m,
                MoneyOut = await cashMovements.SumAsync(x => (decimal?)x.Credit) ?? 0m,
                PendingPayments = await _db.PaymentApprovals.CountAsync(x => x.Status == PaymentApprovalStatuses.Pending),
                PendingPaymentAmount = await _db.PaymentApprovals.Where(x => x.Status == PaymentApprovalStatuses.Pending)
                    .SumAsync(x => (decimal?)x.Amount) ?? 0m,

                Postings = await _db.JournalEntries.AsNoTracking()
                    .Where(x => x.CompanyId == companyId && x.IsPosted &&
                                ((x.PostedAt >= startUtc && x.PostedAt < endUtc) || (x.EntryDate >= start && x.EntryDate < endExclusive)))
                    .OrderByDescending(x => x.PostedAt ?? x.CreatedAt)
                    .Take(300)
                    .Select(x => new PostingRow
                    {
                        Id = x.Id,
                        EntryDate = x.EntryDate,
                        PostedAt = x.PostedAt,
                        EntryNumber = x.EntryNumber,
                        SourceType = x.SourceType,
                        Description = x.Description,
                        Amount = x.Lines.Sum(l => l.Debit),
                        PostedBy = x.PostedBy
                    })
                    .ToListAsync(),

                Payments = await _db.PaymentApprovals.AsNoTracking()
                    .Where(x => x.RequestedAt >= startUtc && x.RequestedAt < endUtc || x.Status == PaymentApprovalStatuses.Pending)
                    .OrderByDescending(x => x.RequestedAt).Take(200).ToListAsync(),

                // Every change staff made (form posts), not the pages they only looked at.
                Changes = await _db.AuditLogs.AsNoTracking()
                    .Where(x => x.CompanyId == companyId && x.HttpMethod == "POST" && x.OccurredAt >= startUtc && x.OccurredAt < endUtc)
                    .OrderByDescending(x => x.OccurredAt).Take(300).ToListAsync(),

                Failures = await _db.AuditLogs.AsNoTracking()
                    .Where(x => (x.CompanyId == companyId || x.CompanyId == null) && x.OccurredAt >= startUtc && x.OccurredAt < endUtc &&
                                (x.StatusCode == 403 || x.StatusCode >= 500))
                    .OrderByDescending(x => x.OccurredAt).Take(100).ToListAsync(),

                LockedUsers = await _db.AppUsers.AsNoTracking()
                    .Where(x => x.LockedOutUntil != null && x.LockedOutUntil > DateTime.UtcNow &&
                                x.CompanyAccesses.Any(c => c.CompanyId == companyId))
                    .ToListAsync(),

                ActiveDelegations = (await _db.ApprovalDelegations.AsNoTracking()
                    .Where(x => x.CompanyId == companyId && x.RevokedAt == null && x.EndDate >= DateTime.Today)
                    .ToListAsync()).Where(x => x.IsActiveOn(DateTime.Today)).ToList(),

                Drafts = await _db.JournalEntries.AsNoTracking()
                    .Where(x => x.CompanyId == companyId && !x.IsPosted && x.EntryDate < DateTime.Today.AddDays(-7))
                    .OrderBy(x => x.EntryDate).Take(50).ToListAsync()
            };

            return View(model);
        }
    }
}
