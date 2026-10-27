using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;

namespace RealEstateManager.Data
{
    /// <summary>Documents kept with tenants and contracts.</summary>
    public partial class ApplicationDbContext
    {
        public DbSet<RecordDocument> RecordDocuments => Set<RecordDocument>();

        private void ConfigureRecordDocuments(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RecordDocument>(entity =>
            {
                entity.Property(x => x.EntityType).HasMaxLength(20);
                entity.Property(x => x.DocumentType).HasMaxLength(60);
                entity.Property(x => x.DocumentNumber).HasMaxLength(100);
                entity.Property(x => x.Notes).HasMaxLength(500);
                entity.Property(x => x.FileName).HasMaxLength(255);
                entity.Property(x => x.ContentType).HasMaxLength(100);
                entity.Property(x => x.UploadedByName).HasMaxLength(200);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.EntityType, x.EntityId });

                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });
        }
    }
}
