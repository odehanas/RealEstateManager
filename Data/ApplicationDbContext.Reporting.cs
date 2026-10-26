using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;

namespace RealEstateManager.Data
{
    /// <summary>Reports saved from the Report Builder.</summary>
    public partial class ApplicationDbContext
    {
        public DbSet<SavedReport> SavedReports => Set<SavedReport>();

        private void ConfigureReporting(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SavedReport>(entity =>
            {
                entity.Property(x => x.Name).HasMaxLength(150);
                entity.Property(x => x.OwnerName).HasMaxLength(200);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.AppUserId });
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });
        }
    }
}
