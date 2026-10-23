using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using RealEstateManager.Domain;

namespace RealEstateManager.Data
{
    /// <summary>
    /// Period closing: the model of the closing tables, and the lock that keeps a closed
    /// period closed whichever screen or service is saving.
    /// </summary>
    public partial class ApplicationDbContext
    {
        private void ConfigurePeriodClosing(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AccountingPeriod>(entity =>
            {
                entity.Property(x => x.ClosedByName).HasMaxLength(200);
                entity.Property(x => x.ReopenedByName).HasMaxLength(200);
            });

            modelBuilder.Entity<FiscalYearClosing>(entity =>
            {
                entity.Property(x => x.TotalRevenue).HasPrecision(18, 2);
                entity.Property(x => x.TotalExpenses).HasPrecision(18, 2);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.ClosedByName).HasMaxLength(200);
                entity.Property(x => x.ReopenedByName).HasMaxLength(200);
            });

            modelBuilder.Entity<FiscalYearCarryForward>(entity =>
            {
                entity.Property(x => x.AccountCode).HasMaxLength(20);
                entity.Property(x => x.AccountName).HasMaxLength(200);
                entity.Property(x => x.AccountType).HasMaxLength(50);
                entity.Property(x => x.DebitBalance).HasPrecision(18, 2);
                entity.Property(x => x.CreditBalance).HasPrecision(18, 2);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.FiscalYearClosing).WithMany().HasForeignKey(x => x.FiscalYearClosingId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new { x.FiscalYearClosingId, x.AccountId }).IsUnique();
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<PeriodReopenRequest>(entity =>
            {
                entity.Property(x => x.Reason).HasMaxLength(1000);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.RequestedByName).HasMaxLength(200);
                entity.Property(x => x.DecidedByName).HasMaxLength(200);
                entity.Property(x => x.DecisionComment).HasMaxLength(1000);
                entity.Property(x => x.ReclosedByName).HasMaxLength(200);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.AccountingPeriod).WithMany().HasForeignKey(x => x.AccountingPeriodId).OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new { x.CompanyId, x.Status });
                entity.HasIndex(x => x.AccountingPeriodId);
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<AccountingPeriodEvent>(entity =>
            {
                entity.Property(x => x.Action).HasMaxLength(50);
                entity.Property(x => x.Note).HasMaxLength(1000);
                entity.Property(x => x.ByName).HasMaxLength(200);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.AccountingPeriod).WithMany().HasForeignKey(x => x.AccountingPeriodId).OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.AccountingPeriodId);
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });
        }

        // =====================================================================
        //  CLOSED PERIOD LOCK
        // =====================================================================

        private int _periodLockBypassDepth;

        /// <summary>
        /// Lets the year-end close write (or undo) its closing entry in the last period of
        /// the year, which is closed by then. Nothing else may use it.
        /// </summary>
        internal IDisposable BypassPeriodLock()
        {
            _periodLockBypassDepth++;
            return new PeriodLockBypass(this);
        }

        private sealed class PeriodLockBypass : IDisposable
        {
            private ApplicationDbContext? _db;

            public PeriodLockBypass(ApplicationDbContext db) => _db = db;

            public void Dispose()
            {
                if (_db != null)
                    _db._periodLockBypassDepth--;
                _db = null;
            }
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            EnforcePeriodLockAsync(async: false, CancellationToken.None).GetAwaiter().GetResult();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            await EnforcePeriodLockAsync(async: true, cancellationToken);
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private readonly record struct TouchedEntry(int? CompanyId, int? PeriodId, DateTime Date, string EntryNumber);

        /// <summary>
        /// The one place that guarantees a closed period stays closed. Whatever is saving,
        /// a journal entry - or a line of one - in a closed period cannot be added, posted,
        /// reset, re-dated, edited or deleted. The posting services check first and give a
        /// friendlier message; this is the backstop, so a screen that forgets to check
        /// still cannot change a closed month.
        /// </summary>
        private async Task EnforcePeriodLockAsync(bool async, CancellationToken cancellationToken)
        {
            if (_periodLockBypassDepth > 0)
                return;

            var touched = new List<TouchedEntry>();
            var lineParentIds = new HashSet<int>();

            foreach (var entry in ChangeTracker.Entries<JournalEntry>())
            {
                var je = entry.Entity;
                switch (entry.State)
                {
                    case EntityState.Added:
                        touched.Add(new TouchedEntry(je.CompanyId, je.AccountingPeriodId, je.EntryDate, je.EntryNumber));
                        break;

                    case EntityState.Deleted:
                        touched.Add(OriginalOf(entry));
                        break;

                    case EntityState.Modified:
                        // Only what changes the accounts. A remark, or the link to a later
                        // reversal, may still be recorded on an entry in a closed period.
                        if (!entry.Property(nameof(JournalEntry.IsPosted)).IsModified &&
                            !entry.Property(nameof(JournalEntry.EntryDate)).IsModified &&
                            !entry.Property(nameof(JournalEntry.AccountingPeriodId)).IsModified &&
                            !entry.Property(nameof(JournalEntry.CompanyId)).IsModified)
                            break;

                        // Both where it was and where it is going.
                        touched.Add(OriginalOf(entry));
                        touched.Add(new TouchedEntry(je.CompanyId, je.AccountingPeriodId, je.EntryDate, je.EntryNumber));
                        break;
                }
            }

            foreach (var entry in ChangeTracker.Entries<JournalEntryLine>())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                    continue;

                var parent = entry.Entity.JournalEntry;
                if (parent != null)
                {
                    var parentEntry = Entry(parent);
                    if (parentEntry.State == EntityState.Added)
                        continue; // checked above as a new entry

                    touched.Add(OriginalOf(parentEntry));
                    continue;
                }

                var journalEntryId = entry.State == EntityState.Deleted
                    ? (int)entry.Property(nameof(JournalEntryLine.JournalEntryId)).OriginalValue!
                    : entry.Entity.JournalEntryId;

                if (journalEntryId > 0)
                    lineParentIds.Add(journalEntryId);
            }

            if (lineParentIds.Count > 0)
            {
                var query = JournalEntries.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => lineParentIds.Contains(x.Id))
                    .Select(x => new TouchedEntry(x.CompanyId, x.AccountingPeriodId, x.EntryDate, x.EntryNumber));

                touched.AddRange(async ? await query.ToListAsync(cancellationToken) : query.ToList());
            }

            if (touched.Count == 0)
                return;

            // An entry is in a closed period if its own period is closed, or if a closed
            // period of its company covers its date - either is enough.
            var periodIds = touched.Where(x => x.PeriodId.HasValue).Select(x => x.PeriodId!.Value).Distinct().ToList();
            var companyIds = touched.Where(x => x.CompanyId.HasValue).Select(x => x.CompanyId!.Value).Distinct().ToList();
            var minDate = touched.Min(x => x.Date).Date;
            var maxDate = touched.Max(x => x.Date).Date;

            var closedQuery = AccountingPeriods.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.IsClosed &&
                            (periodIds.Contains(x.Id) ||
                             (companyIds.Contains(x.CompanyId) && x.StartDate <= maxDate && x.EndDate >= minDate)))
                .Select(x => new { x.Id, x.CompanyId, x.Name, x.FiscalYear, x.StartDate, x.EndDate });

            var closed = async ? await closedQuery.ToListAsync(cancellationToken) : closedQuery.ToList();
            if (closed.Count == 0)
                return;

            foreach (var item in touched)
            {
                var hit = closed.FirstOrDefault(p =>
                    (item.PeriodId.HasValue && p.Id == item.PeriodId.Value) ||
                    (item.CompanyId.HasValue && p.CompanyId == item.CompanyId.Value &&
                     p.StartDate <= item.Date.Date && p.EndDate >= item.Date.Date));

                if (hit == null)
                    continue;

                var what = string.IsNullOrWhiteSpace(item.EntryNumber) ? "A journal entry" : $"Journal {item.EntryNumber}";
                throw new ClosedPeriodException(
                    $"{hit.Name} (fiscal year {hit.FiscalYear}) is closed. {what} dated {item.Date:dd/MM/yyyy} cannot be " +
                    "created, posted, changed or removed in a closed period. Date the transaction in an open period, " +
                    "or ask for the period to be reopened from the Closing Cockpit.");
            }
        }

        private static TouchedEntry OriginalOf(EntityEntry entry) => new(
            (int?)entry.Property(nameof(JournalEntry.CompanyId)).OriginalValue,
            (int?)entry.Property(nameof(JournalEntry.AccountingPeriodId)).OriginalValue,
            (DateTime)entry.Property(nameof(JournalEntry.EntryDate)).OriginalValue!,
            (string?)entry.Property(nameof(JournalEntry.EntryNumber)).OriginalValue ?? string.Empty);
    }

    /// <summary>Thrown when a save would change the accounts of a closed period.</summary>
    public class ClosedPeriodException : InvalidOperationException
    {
        public ClosedPeriodException(string message) : base(message) { }
    }
}
