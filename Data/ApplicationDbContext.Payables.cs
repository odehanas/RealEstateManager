using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;

namespace RealEstateManager.Data
{
    /// <summary>Vendor clearing documents and advances (down payments).</summary>
    public partial class ApplicationDbContext
    {
        public DbSet<VendorClearing> VendorClearings => Set<VendorClearing>();
        public DbSet<VendorClearingLine> VendorClearingLines => Set<VendorClearingLine>();

        private void ConfigurePayables(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<VendorPayment>(entity =>
            {
                entity.Property(x => x.AdvanceApplied).HasPrecision(18, 2);
                entity.Property(x => x.Reference).HasMaxLength(100);
                entity.Property(x => x.Notes).HasMaxLength(500);
                entity.Property(x => x.ClearingNumber).HasMaxLength(30);
                entity.HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.PaidFromAccount).WithMany().HasForeignKey(x => x.PaidFromAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<VendorBill>().Property(x => x.ClearingNumber).HasMaxLength(30);

            modelBuilder.Entity<VendorClearing>(entity =>
            {
                entity.Property(x => x.ClearingNumber).HasMaxLength(30);
                entity.Property(x => x.ClearingType).HasMaxLength(20);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.Notes).HasMaxLength(500);
                entity.Property(x => x.CreatedByName).HasMaxLength(200);
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.VendorPayment).WithMany().HasForeignKey(x => x.VendorPaymentId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.AdvancePayment).WithMany().HasForeignKey(x => x.AdvancePaymentId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.JournalEntry).WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.ClearingNumber }).IsUnique();
                entity.HasIndex(x => x.VendorPaymentId);
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<VendorClearingLine>(entity =>
            {
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.HasOne(x => x.VendorClearing).WithMany(x => x.Lines).HasForeignKey(x => x.VendorClearingId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.VendorBill).WithMany().HasForeignKey(x => x.VendorBillId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.VendorBillId);
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.VendorClearing!.CompanyId == CurrentCompanyIdValue);
            });
        }
    }
}
