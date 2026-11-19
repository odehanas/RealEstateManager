using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;

namespace RealEstateManager.Data
{
    /// <summary>The public vacancy website: the company's page, unit photos and visitors' enquiries.</summary>
    public partial class ApplicationDbContext
    {
        public DbSet<ListingSite> ListingSites => Set<ListingSite>();
        public DbSet<UnitPhoto> UnitPhotos => Set<UnitPhoto>();
        public DbSet<UnitEnquiry> UnitEnquiries => Set<UnitEnquiry>();

        private void ConfigureListings(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Unit>(entity =>
            {
                entity.Property(x => x.AskingRent).HasPrecision(18, 2);
                entity.Property(x => x.Amenities).HasMaxLength(600);
                entity.Property(x => x.ListingDescription).HasMaxLength(2000);
                entity.Property(x => x.ListingDescriptionAr).HasMaxLength(2000);
                entity.Ignore(x => x.AdvertisedRent);
            });

            modelBuilder.Entity<ListingSite>(entity =>
            {
                entity.Property(x => x.Slug).HasMaxLength(60);
                entity.Property(x => x.DisplayName).HasMaxLength(200);
                entity.Property(x => x.DisplayNameAr).HasMaxLength(200);
                entity.Property(x => x.Intro).HasMaxLength(600);
                entity.Property(x => x.IntroAr).HasMaxLength(600);
                entity.Property(x => x.ContactPhone).HasMaxLength(30);
                entity.Property(x => x.WhatsAppNumber).HasMaxLength(30);
                entity.Property(x => x.ContactEmail).HasMaxLength(200);
                entity.HasIndex(x => x.CompanyId).IsUnique();
                entity.HasIndex(x => x.Slug).IsUnique();
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<UnitPhoto>(entity =>
            {
                entity.Property(x => x.FileName).HasMaxLength(260);
                entity.Property(x => x.ContentType).HasMaxLength(100);
                entity.Property(x => x.UploadedByName).HasMaxLength(200);
                entity.HasIndex(x => new { x.UnitId, x.SortOrder, x.Id });
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Unit).WithMany(x => x.Photos).HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Cascade);
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<UnitEnquiry>(entity =>
            {
                entity.Property(x => x.Name).HasMaxLength(150);
                entity.Property(x => x.Phone).HasMaxLength(30);
                entity.Property(x => x.Email).HasMaxLength(200);
                entity.Property(x => x.Message).HasMaxLength(1000);
                entity.Property(x => x.Language).HasMaxLength(5);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.Notes).HasMaxLength(1000);
                entity.Property(x => x.HandledByName).HasMaxLength(200);
                entity.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedAt });
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.SetNull);
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });
        }
    }
}
