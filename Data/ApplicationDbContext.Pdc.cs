using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;

namespace RealEstateManager.Data
{
    /// <summary>The later steps of a post-dated cheque: deposit, bounce, withdrawal and its charge.</summary>
    public partial class ApplicationDbContext
    {
        private void ConfigurePdcLifecycle(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PdcReceipt>(entity =>
            {
                entity.Property(x => x.BounceReason).HasMaxLength(300);
                entity.HasOne(x => x.DepositJournalEntry).WithMany().HasForeignKey(x => x.DepositJournalEntryId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.BounceJournalEntry).WithMany().HasForeignKey(x => x.BounceJournalEntryId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.WithdrawalJournalEntry).WithMany().HasForeignKey(x => x.WithdrawalJournalEntryId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.ChargeInvoice).WithMany().HasForeignKey(x => x.ChargeInvoiceId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CompanyAccountSetting>(entity =>
            {
                entity.HasOne(x => x.ChequesUnderCollectionAccount).WithMany().HasForeignKey(x => x.ChequesUnderCollectionAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.PdcChargeIncomeAccount).WithMany().HasForeignKey(x => x.PdcChargeIncomeAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Company>(entity =>
            {
                entity.Property(x => x.PdcWithdrawalCharge).HasPrecision(18, 2);
                entity.Property(x => x.PdcBounceCharge).HasPrecision(18, 2);
            });

            modelBuilder.Entity<CustomerInvoice>().Property(x => x.ChargeType).HasMaxLength(60);
        }
    }
}
