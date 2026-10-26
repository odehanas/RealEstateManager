using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;

namespace RealEstateManager.Data
{
    /// <summary>Approval levels, delegations, payment approvals and the employee portal.</summary>
    public partial class ApplicationDbContext
    {
        public DbSet<ApprovalLevel> ApprovalLevels => Set<ApprovalLevel>();
        public DbSet<ApprovalDelegation> ApprovalDelegations => Set<ApprovalDelegation>();
        public DbSet<PaymentApproval> PaymentApprovals => Set<PaymentApproval>();
        public DbSet<EmployeePortalUser> EmployeePortalUsers => Set<EmployeePortalUser>();
        public DbSet<EmployeeRequest> EmployeeRequests => Set<EmployeeRequest>();

        private void ConfigureControls(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ApprovalLevel>(entity =>
            {
                entity.Property(x => x.Name).HasMaxLength(50);
                entity.Property(x => x.MaxAmount).HasPrecision(18, 2);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<AppUserCompany>()
                .HasOne(x => x.ApprovalLevel).WithMany().HasForeignKey(x => x.ApprovalLevelId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ApprovalDelegation>(entity =>
            {
                entity.Property(x => x.FromName).HasMaxLength(200);
                entity.Property(x => x.ToName).HasMaxLength(200);
                entity.Property(x => x.Reason).HasMaxLength(300);
                entity.Property(x => x.RevokedByName).HasMaxLength(200);
                entity.Property(x => x.MaxAmount).HasPrecision(18, 2);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.ToAppUserId });
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<PaymentApproval>(entity =>
            {
                entity.Property(x => x.SourceType).HasMaxLength(50);
                entity.Property(x => x.Description).HasMaxLength(300);
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.RequiredLevel).HasMaxLength(50);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.RequestedByName).HasMaxLength(200);
                entity.Property(x => x.DecidedByName).HasMaxLength(200);
                entity.Property(x => x.Comment).HasMaxLength(500);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.JournalEntry).WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(x => new { x.CompanyId, x.Status });
                entity.HasIndex(x => x.JournalEntryId);
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<EmployeePortalUser>(entity =>
            {
                entity.Property(x => x.Username).HasMaxLength(100);
                entity.Property(x => x.PasswordHash).HasMaxLength(500);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.Username).IsUnique();
                entity.HasIndex(x => x.EmployeeId).IsUnique();
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<EmployeeRequest>(entity =>
            {
                entity.Property(x => x.RequestType).HasMaxLength(30);
                entity.Property(x => x.AddressedTo).HasMaxLength(200);
                entity.Property(x => x.Purpose).HasMaxLength(300);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.DecidedByName).HasMaxLength(200);
                entity.Property(x => x.Comment).HasMaxLength(500);
                entity.Property(x => x.ReferenceNumber).HasMaxLength(30);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.Status });
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });
        }
    }
}
