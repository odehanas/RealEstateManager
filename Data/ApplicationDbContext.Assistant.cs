using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;

namespace RealEstateManager.Data
{
    /// <summary>The AI assistant: its sessions, conversation log and actions awaiting confirmation.</summary>
    public partial class ApplicationDbContext
    {
        public DbSet<AssistantSession> AssistantSessions => Set<AssistantSession>();
        public DbSet<AssistantMessage> AssistantMessages => Set<AssistantMessage>();
        public DbSet<AssistantPendingAction> AssistantPendingActions => Set<AssistantPendingAction>();

        private void ConfigureAssistant(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AssistantSession>(entity =>
            {
                entity.Property(x => x.UserName).HasMaxLength(200);
                entity.Property(x => x.Model).HasMaxLength(100);
                entity.Property(x => x.Mode).HasMaxLength(10);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.StartedAt });
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<AssistantMessage>(entity =>
            {
                entity.Property(x => x.Role).HasMaxLength(20);
                entity.Property(x => x.Text).HasMaxLength(4000);
                entity.Property(x => x.ToolName).HasMaxLength(100);
                entity.Property(x => x.ToolArgsJson).HasMaxLength(4000);
                entity.Property(x => x.ToolResultSummary).HasMaxLength(1000);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(x => x.SessionId);
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<AssistantPendingAction>(entity =>
            {
                entity.Property(x => x.ToolName).HasMaxLength(100);
                entity.Property(x => x.Summary).HasMaxLength(2000);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.ErrorMessage).HasMaxLength(1000);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.Status });
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });
        }
    }
}
