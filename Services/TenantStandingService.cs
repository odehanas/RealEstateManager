using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Whether a tenant's account is in good standing: nothing overdue and no returned
    /// cheque waiting to be replaced. A tenant who is not must contact the office before
    /// asking for maintenance through the portal.
    /// </summary>
    public static class TenantStandingService
    {
        public sealed record Standing(decimal Overdue, int OverdueInvoices, int BouncedCheques)
        {
            public bool IsHealthy => Overdue <= 0m && BouncedCheques == 0;

            /// <summary>What the tenant is told, or null when nothing is wrong.</summary>
            public string? Message => IsHealthy
                ? null
                : "Maintenance requests are on hold for your account because " +
                  string.Join(" and ", new[]
                  {
                      Overdue > 0m ? $"{Overdue:N2} is overdue on {OverdueInvoices} invoice(s)" : null,
                      BouncedCheques > 0 ? $"{BouncedCheques} returned cheque(s) still need to be replaced" : null
                  }.Where(x => x != null)) +
                  ". Please contact the administration office to settle it; you can then request maintenance here.";
        }

        public static async Task<Standing> GetAsync(ApplicationDbContext db, int tenantId)
        {
            var today = DateTime.Today;

            // Posted invoices past their due date with money still owed. An invoice a
            // post-dated cheque on hand is waiting to settle is not treated as overdue.
            var overdue = await db.CustomerInvoices.AsNoTracking()
                .Where(x => x.Lease!.TenantId == tenantId
                            && x.JournalEntry != null && x.JournalEntry.IsPosted
                            && x.DueDate < today
                            && x.TotalAmount - x.AmountPaid > 0.009m
                            && !db.PdcReceipts.Any(p => p.CustomerInvoiceId == x.Id && p.Status == "Received"))
                .Select(x => x.TotalAmount - x.AmountPaid)
                .ToListAsync();

            var bounced = await db.PdcReceipts.AsNoTracking()
                .CountAsync(x => x.Lease!.TenantId == tenantId && x.Status == "Bounced");

            return new Standing(overdue.Sum(), overdue.Count, bounced);
        }
    }
}
