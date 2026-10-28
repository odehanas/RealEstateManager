using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;

namespace RealEstateManager.Data
{
    /// <summary>Maintenance jobs: the materials used on them.</summary>
    public partial class ApplicationDbContext
    {
        public DbSet<MaintenanceMaterial> MaintenanceMaterials => Set<MaintenanceMaterial>();
        public DbSet<OwnerRecharge> OwnerRecharges => Set<OwnerRecharge>();

        private void ConfigureMaintenance(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MaintenanceMaterial>(entity =>
            {
                entity.Property(x => x.Description).HasMaxLength(300);
                entity.Property(x => x.Quantity).HasPrecision(18, 3);
                entity.Property(x => x.UnitCost).HasPrecision(18, 2);
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.Source).HasMaxLength(20);
                entity.Property(x => x.Reference).HasMaxLength(100);
                entity.Property(x => x.ChargedTo).HasMaxLength(20);
                entity.Property(x => x.CreatedByName).HasMaxLength(200);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.ServiceRequest).WithMany().HasForeignKey(x => x.TenantServiceRequestId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.JournalEntry).WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.TenantServiceRequestId });

                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<OwnerRecharge>(entity =>
            {
                entity.Property(x => x.SourceType).HasMaxLength(30);
                entity.Property(x => x.SourceReference).HasMaxLength(100);
                entity.Property(x => x.OwnerName).HasMaxLength(200);
                entity.Property(x => x.OwnerTrn).HasMaxLength(20);
                entity.Property(x => x.Description).HasMaxLength(500);
                entity.Property(x => x.NetAmount).HasPrecision(18, 2);
                entity.Property(x => x.VatRate).HasPrecision(5, 2);
                entity.Property(x => x.VatAmount).HasPrecision(18, 2);
                entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
                entity.Property(x => x.TaxInvoiceNumber).HasMaxLength(30);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Property).WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.JournalEntry).WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId, x.PropertyId }).IsUnique();

                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });
        }
    }
}
