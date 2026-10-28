using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;

namespace RealEstateManager.Data
{
    /// <summary>The devices that receive phone and browser notifications.</summary>
    public partial class ApplicationDbContext
    {
        public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

        private void ConfigurePush(ModelBuilder modelBuilder)
        {
            // No company filter: a tenant or employee principal carries no company claim,
            // and every query here names the person it is for.
            modelBuilder.Entity<PushSubscription>(entity =>
            {
                entity.Property(x => x.Audience).HasMaxLength(10);
                entity.Property(x => x.Endpoint).HasMaxLength(1000);
                entity.Property(x => x.EndpointHash).HasMaxLength(64);
                entity.Property(x => x.P256dh).HasMaxLength(200);
                entity.Property(x => x.Auth).HasMaxLength(100);
                entity.Property(x => x.Language).HasMaxLength(5);
                entity.Property(x => x.UserAgent).HasMaxLength(300);
                entity.HasIndex(x => x.EndpointHash).IsUnique();
                entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AppUserId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<TenantPortalUser>().WithMany().HasForeignKey(x => x.TenantPortalUserId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<EmployeePortalUser>().WithMany().HasForeignKey(x => x.EmployeePortalUserId).OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
