using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;

namespace RealEstateManager.Data
{
    /// <summary>Maintenance jobs: the materials used on them.</summary>
    public partial class ApplicationDbContext
    {
        public DbSet<MaintenanceMaterial> MaintenanceMaterials => Set<MaintenanceMaterial>();
        public DbSet<OwnerRecharge> OwnerRecharges => Set<OwnerRecharge>();
        public DbSet<MaintenanceCategory> MaintenanceCategories => Set<MaintenanceCategory>();
        public DbSet<ServiceCatalogItem> ServiceCatalogItems => Set<ServiceCatalogItem>();
        public DbSet<ServiceRequestPhoto> ServiceRequestPhotos => Set<ServiceRequestPhoto>();
        public DbSet<LeaseHandover> LeaseHandovers => Set<LeaseHandover>();
        public DbSet<LeaseHandoverItem> LeaseHandoverItems => Set<LeaseHandoverItem>();
        public DbSet<LeaseHandoverCharge> LeaseHandoverCharges => Set<LeaseHandoverCharge>();
        public DbSet<HandoverChecklistItem> HandoverChecklistItems => Set<HandoverChecklistItem>();

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

            modelBuilder.Entity<MaintenanceCategory>(entity =>
            {
                entity.Property(x => x.Name).HasMaxLength(MaintenanceCategory.MaxNameLength);
                entity.Property(x => x.CreatedByName).HasMaxLength(200);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();

                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<ServiceCatalogItem>(entity =>
            {
                entity.Property(x => x.Name).HasMaxLength(ServiceCatalogItem.MaxNameLength);
                entity.Property(x => x.Kind).HasMaxLength(30);
                entity.Property(x => x.Category).HasMaxLength(MaintenanceCategory.MaxNameLength);
                entity.Property(x => x.Description).HasMaxLength(500);
                entity.Property(x => x.SellingPrice).HasPrecision(18, 2);
                entity.Property(x => x.ExpectedCost).HasPrecision(18, 2);
                entity.Property(x => x.CreatedByName).HasMaxLength(200);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.IncomeAccount).WithMany().HasForeignKey(x => x.IncomeAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();

                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<TenantServiceRequest>(entity =>
            {
                entity.Property(x => x.ServiceName).HasMaxLength(ServiceCatalogItem.MaxNameLength);
                entity.Property(x => x.ServiceKind).HasMaxLength(30);
                entity.Property(x => x.ServicePrice).HasPrecision(18, 2);
                entity.Property(x => x.ServiceExpectedCost).HasPrecision(18, 2);
                entity.Property(x => x.OriginalLanguage).HasMaxLength(40);
                entity.Property(x => x.SubjectEnglish).HasMaxLength(400);
                entity.Property(x => x.TranslationStatus).HasMaxLength(20);
            });

            modelBuilder.Entity<TenantServiceRequestComment>(entity =>
            {
                entity.Property(x => x.OriginalLanguage).HasMaxLength(40);
                entity.Property(x => x.TranslationStatus).HasMaxLength(20);
            });

            modelBuilder.Entity<ServiceRequestPhoto>(entity =>
            {
                entity.Property(x => x.FileName).HasMaxLength(260);
                entity.Property(x => x.ContentType).HasMaxLength(100);
                entity.Property(x => x.UploadedByName).HasMaxLength(200);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.TenantServiceRequest).WithMany().HasForeignKey(x => x.TenantServiceRequestId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.TenantServiceRequestId, x.Id });

                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<LeaseHandover>(entity =>
            {
                entity.Property(x => x.HandoverNumber).HasMaxLength(30);
                entity.Property(x => x.Reason).HasMaxLength(30);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.ElectricityReading).HasMaxLength(50);
                entity.Property(x => x.WaterReading).HasMaxLength(50);
                entity.Property(x => x.InspectionNotes).HasMaxLength(2000);
                entity.Property(x => x.MaintenanceNotes).HasMaxLength(2000);
                entity.Property(x => x.AccountsNotes).HasMaxLength(2000);
                entity.Property(x => x.InspectedByName).HasMaxLength(200);
                entity.Property(x => x.MaintenanceSignedByName).HasMaxLength(200);
                entity.Property(x => x.SettledByName).HasMaxLength(200);
                entity.Property(x => x.CreatedByName).HasMaxLength(200);
                entity.Property(x => x.DepositHeld).HasPrecision(18, 2);
                entity.Property(x => x.DepositApplied).HasPrecision(18, 2);
                entity.Property(x => x.DepositRefunded).HasPrecision(18, 2);
                entity.Property(x => x.BalanceStillDue).HasPrecision(18, 2);
                entity.Ignore(x => x.IsInspected);
                entity.Ignore(x => x.IsMaintenanceSigned);
                entity.Ignore(x => x.IsOpen);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Lease).WithMany().HasForeignKey(x => x.LeaseId).OnDelete(DeleteBehavior.Restrict);
                entity.HasMany(x => x.Items).WithOne(x => x.LeaseHandover).HasForeignKey(x => x.LeaseHandoverId).OnDelete(DeleteBehavior.Cascade);
                entity.HasMany(x => x.Charges).WithOne(x => x.LeaseHandover).HasForeignKey(x => x.LeaseHandoverId).OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(x => new { x.CompanyId, x.LeaseId });

                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<LeaseHandoverItem>(entity =>
            {
                entity.Property(x => x.Name).HasMaxLength(HandoverChecklistItem.MaxNameLength);
                entity.Property(x => x.Result).HasMaxLength(20);
                entity.Property(x => x.Notes).HasMaxLength(500);
            });

            modelBuilder.Entity<LeaseHandoverCharge>(entity =>
            {
                entity.Property(x => x.Description).HasMaxLength(300);
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.CreatedByName).HasMaxLength(200);
            });

            modelBuilder.Entity<HandoverChecklistItem>(entity =>
            {
                entity.Property(x => x.Name).HasMaxLength(HandoverChecklistItem.MaxNameLength);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.SortOrder });

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
                entity.Property(x => x.Treatment).HasMaxLength(20);
                entity.Property(x => x.SupplierVatAmount).HasPrecision(18, 2);
                entity.Property(x => x.CostCategory).HasMaxLength(40);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Property).WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.JournalEntry).WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId, x.PropertyId }).IsUnique();

                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });
        }
    }
}
