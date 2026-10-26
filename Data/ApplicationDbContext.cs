using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Data
{
    public partial class ApplicationDbContext : DbContext
    {
        private readonly ICurrentCompanyAccessor _currentCompanyAccessor;
        private readonly DatabaseConnectionOptions _databaseConnectionOptions;

        private int CurrentCompanyIdValue => _currentCompanyAccessor.CompanyId ?? 0;
        private bool IsCompanyFilterEnabled => _currentCompanyAccessor.CompanyId.HasValue;

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ICurrentCompanyAccessor currentCompanyAccessor,
            DatabaseConnectionOptions databaseConnectionOptions)
            : base(options)
        {
            _currentCompanyAccessor = currentCompanyAccessor;
            _databaseConnectionOptions = databaseConnectionOptions;
        }

        public DbSet<AppUser> AppUsers => Set<AppUser>();
        public DbSet<AppUserCompany> AppUserCompanies => Set<AppUserCompany>();
        public DbSet<AppUserPermission> AppUserPermissions => Set<AppUserPermission>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<UserFeedback> UserFeedback => Set<UserFeedback>();
        public DbSet<Company> Companies => Set<Company>();
        public DbSet<CompanySubscription> CompanySubscriptions => Set<CompanySubscription>();
        public DbSet<SubscriptionActivationCode> SubscriptionActivationCodes => Set<SubscriptionActivationCode>();
        public DbSet<Account> Accounts => Set<Account>();
        public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
        public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
        public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();
        public DbSet<FiscalYearClosing> FiscalYearClosings => Set<FiscalYearClosing>();
        public DbSet<FiscalYearCarryForward> FiscalYearCarryForwards => Set<FiscalYearCarryForward>();
        public DbSet<PeriodReopenRequest> PeriodReopenRequests => Set<PeriodReopenRequest>();
        public DbSet<AccountingPeriodEvent> AccountingPeriodEvents => Set<AccountingPeriodEvent>();

        public DbSet<Property> Properties => Set<Property>();
        public DbSet<Unit> Units => Set<Unit>();
        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<Lease> Leases => Set<Lease>();

        public DbSet<Vendor> Vendors => Set<Vendor>();
        public DbSet<CustomerInvoice> CustomerInvoices => Set<CustomerInvoice>();
        public DbSet<CustomerReceipt> CustomerReceipts => Set<CustomerReceipt>();
        public DbSet<VendorBill> VendorBills => Set<VendorBill>();
        public DbSet<VendorBillLine> VendorBillLines => Set<VendorBillLine>();
        public DbSet<VendorPayment> VendorPayments => Set<VendorPayment>();
        public DbSet<SecurityDeposit> SecurityDeposits => Set<SecurityDeposit>();
        public DbSet<PrepaymentSchedule> PrepaymentSchedules => Set<PrepaymentSchedule>();
        public DbSet<PrepaymentScheduleLine> PrepaymentScheduleLines => Set<PrepaymentScheduleLine>();
        public DbSet<RevenueRecognitionSchedule> RevenueRecognitionSchedules => Set<RevenueRecognitionSchedule>();
        public DbSet<PdcReceipt> PdcReceipts => Set<PdcReceipt>();
        public DbSet<FixedAsset> FixedAssets => Set<FixedAsset>();
        public DbSet<FixedAssetAcquisitionCost> FixedAssetAcquisitionCosts => Set<FixedAssetAcquisitionCost>();
        public DbSet<FixedAssetDepreciation> FixedAssetDepreciations => Set<FixedAssetDepreciation>();

        public DbSet<CompanyAccountSetting> CompanyAccountSettings => Set<CompanyAccountSetting>();
        public DbSet<OwnerWithdrawalCategory> OwnerWithdrawalCategories => Set<OwnerWithdrawalCategory>();
        public DbSet<TenantPortalUser> TenantPortalUsers => Set<TenantPortalUser>();
        public DbSet<TenantServiceRequest> TenantServiceRequests => Set<TenantServiceRequest>();
        public DbSet<TenantServiceRequestComment> TenantServiceRequestComments => Set<TenantServiceRequestComment>();
        public DbSet<CustomerCreditNote> CustomerCreditNotes => Set<CustomerCreditNote>();
        public DbSet<ReceivedEInvoice> ReceivedEInvoices => Set<ReceivedEInvoice>();
        public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
        public DbSet<OwnerSettlement> OwnerSettlements => Set<OwnerSettlement>();
        public DbSet<OwnerCommissionInvoice> OwnerCommissionInvoices => Set<OwnerCommissionInvoice>();
        public DbSet<OwnerCommissionInvoiceLine> OwnerCommissionInvoiceLines => Set<OwnerCommissionInvoiceLine>();
        public DbSet<PropertyDocument> PropertyDocuments => Set<PropertyDocument>();
        public DbSet<PropertyPhoto> PropertyPhotos => Set<PropertyPhoto>();
        public DbSet<UnitPhotoArea> UnitPhotoAreas => Set<UnitPhotoArea>();
        public DbSet<DevelopmentProject> DevelopmentProjects => Set<DevelopmentProject>();
        public DbSet<DevelopmentBudgetLine> DevelopmentBudgetLines => Set<DevelopmentBudgetLine>();
        public DbSet<DevelopmentTransfer> DevelopmentTransfers => Set<DevelopmentTransfer>();
        public DbSet<DevelopmentTransferLine> DevelopmentTransferLines => Set<DevelopmentTransferLine>();
        public DbSet<UnitSale> UnitSales => Set<UnitSale>();
        public DbSet<UnitSaleInstalment> UnitSaleInstalments => Set<UnitSaleInstalment>();
        public DbSet<UnitSaleReceipt> UnitSaleReceipts => Set<UnitSaleReceipt>();
        public DbSet<CapitalAssetVatRecord> CapitalAssetVatRecords => Set<CapitalAssetVatRecord>();
        public DbSet<CapitalAssetVatAdjustment> CapitalAssetVatAdjustments => Set<CapitalAssetVatAdjustment>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!string.IsNullOrWhiteSpace(_databaseConnectionOptions.ConnectionString))
            {
                optionsBuilder.UseSqlServer(_databaseConnectionOptions.ConnectionString);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Company>()
                .Property(x => x.Name)
                .HasMaxLength(200);

            modelBuilder.Entity<Company>()
                .Property(x => x.BaseCurrency)
                .HasMaxLength(10);

            modelBuilder.Entity<AppUser>()
                .Property(x => x.Username)
                .HasMaxLength(100);

            modelBuilder.Entity<AppUser>()
                .Property(x => x.DisplayName)
                .HasMaxLength(200);

            modelBuilder.Entity<AppUser>()
                .Property(x => x.PasswordHash)
                .HasMaxLength(500);

            modelBuilder.Entity<AppUserPermission>()
                .Property(x => x.PermissionKey)
                .HasMaxLength(100);

            modelBuilder.Entity<CompanySubscription>()
                .Property(x => x.Notes)
                .HasMaxLength(500);

            modelBuilder.Entity<SubscriptionActivationCode>()
                .Property(x => x.Code)
                .HasMaxLength(50);

            modelBuilder.Entity<SubscriptionActivationCode>()
                .Property(x => x.Notes)
                .HasMaxLength(300);

            modelBuilder.Entity<AuditLog>()
                .Property(x => x.Username)
                .HasMaxLength(100);

            modelBuilder.Entity<AuditLog>()
                .Property(x => x.DisplayName)
                .HasMaxLength(200);

            modelBuilder.Entity<AuditLog>()
                .Property(x => x.CompanyName)
                .HasMaxLength(200);

            modelBuilder.Entity<AuditLog>()
                .Property(x => x.HttpMethod)
                .HasMaxLength(20);

            modelBuilder.Entity<AuditLog>()
                .Property(x => x.RequestPath)
                .HasMaxLength(300);

            modelBuilder.Entity<AuditLog>()
                .Property(x => x.QueryString)
                .HasMaxLength(1000);

            modelBuilder.Entity<AuditLog>()
                .Property(x => x.ControllerName)
                .HasMaxLength(100);

            modelBuilder.Entity<AuditLog>()
                .Property(x => x.ActionName)
                .HasMaxLength(100);

            modelBuilder.Entity<AuditLog>()
                .Property(x => x.ActivityName)
                .HasMaxLength(200);

            modelBuilder.Entity<AuditLog>()
                .Property(x => x.ErrorMessage)
                .HasMaxLength(1000);

            modelBuilder.Entity<AuditLog>()
                .Property(x => x.IpAddress)
                .HasMaxLength(100);

            modelBuilder.Entity<UserFeedback>()
                .Property(x => x.Username)
                .HasMaxLength(100);

            modelBuilder.Entity<UserFeedback>()
                .Property(x => x.DisplayName)
                .HasMaxLength(200);

            modelBuilder.Entity<UserFeedback>()
                .Property(x => x.CompanyName)
                .HasMaxLength(200);

            modelBuilder.Entity<UserFeedback>()
                .Property(x => x.Category)
                .HasMaxLength(50);

            modelBuilder.Entity<UserFeedback>()
                .Property(x => x.Subject)
                .HasMaxLength(200);

            modelBuilder.Entity<UserFeedback>()
                .Property(x => x.Message)
                .HasMaxLength(2000);

            modelBuilder.Entity<UserFeedback>()
                .Property(x => x.Status)
                .HasMaxLength(50);

            modelBuilder.Entity<UserFeedback>()
                .Property(x => x.AdminNotes)
                .HasMaxLength(1000);

            modelBuilder.Entity<Account>()
                .Property(x => x.Code)
                .HasMaxLength(20);

            modelBuilder.Entity<Account>()
                .Property(x => x.Name)
                .HasMaxLength(200);

            modelBuilder.Entity<Account>()
                .Property(x => x.AccountType)
                .HasMaxLength(50);

            modelBuilder.Entity<Account>()
                .Property(x => x.ReportingGroup)
                .HasMaxLength(50);

            modelBuilder.Entity<Account>()
                .Property(x => x.OpeningBalance)
                .HasPrecision(18, 2);

            modelBuilder.Entity<JournalEntry>()
                .Property(x => x.EntryNumber)
                .HasMaxLength(50);

            modelBuilder.Entity<JournalEntry>()
                .Property(x => x.Description)
                .HasMaxLength(500);

            modelBuilder.Entity<JournalEntry>()
                .Property(x => x.SourceType)
                .HasMaxLength(50);

            modelBuilder.Entity<JournalEntryLine>()
                .Property(x => x.Description)
                .HasMaxLength(500);

            modelBuilder.Entity<JournalEntryLine>()
                .Property(x => x.Debit)
                .HasPrecision(18, 2);

            modelBuilder.Entity<JournalEntryLine>()
                .Property(x => x.Credit)
                .HasPrecision(18, 2);

            // ----- VAT -------------------------------------------------------
            modelBuilder.Entity<Company>().Property(x => x.LegalName).HasMaxLength(200);
            modelBuilder.Entity<Company>().Property(x => x.TrnNumber).HasMaxLength(30);
            modelBuilder.Entity<Company>().Property(x => x.AddressLine1).HasMaxLength(200);
            modelBuilder.Entity<Company>().Property(x => x.AddressLine2).HasMaxLength(200);
            modelBuilder.Entity<Company>().Property(x => x.City).HasMaxLength(100);
            modelBuilder.Entity<Company>().Property(x => x.Country).HasMaxLength(100);
            modelBuilder.Entity<Company>().Property(x => x.Phone).HasMaxLength(50);
            modelBuilder.Entity<Company>().Property(x => x.Email).HasMaxLength(200);
            modelBuilder.Entity<Company>().Property(x => x.ProvisionalVatRecoveryRate).HasPrecision(9, 4);

            modelBuilder.Entity<Property>().Property(x => x.VatCategory).HasMaxLength(20);
            modelBuilder.Entity<Property>().Property(x => x.OwnerTrn).HasMaxLength(20);

            // Registration details on the property record.
            modelBuilder.Entity<Property>(entity =>
            {
                entity.Property(x => x.Emirate).HasMaxLength(30);
                entity.Property(x => x.AreaName).HasMaxLength(100);
                entity.Property(x => x.PlotNumber).HasMaxLength(50);
                entity.Property(x => x.MunicipalityNumber).HasMaxLength(50);
                entity.Property(x => x.LocationReference).HasMaxLength(50);
                entity.Property(x => x.Latitude).HasPrecision(9, 6);
                entity.Property(x => x.Longitude).HasPrecision(9, 6);
                entity.Property(x => x.TitleDeedNumber).HasMaxLength(50);
                entity.Property(x => x.TitleDeedAuthority).HasMaxLength(100);
                entity.Property(x => x.TenureType).HasMaxLength(30);
                entity.Property(x => x.LandUse).HasMaxLength(30);
                entity.Property(x => x.PlotAreaSqFt).HasPrecision(18, 2);
                entity.Property(x => x.OwnershipPercentage).HasPrecision(9, 4);
                entity.Property(x => x.MortgageDetails).HasMaxLength(300);
                entity.Property(x => x.BuildingPermitNumber).HasMaxLength(50);
                entity.Property(x => x.CompletionCertificateNumber).HasMaxLength(50);
                entity.Property(x => x.BuiltUpAreaSqFt).HasPrecision(18, 2);
                entity.Property(x => x.DeveloperName).HasMaxLength(150);
                entity.Property(x => x.MainContractor).HasMaxLength(150);
                entity.Property(x => x.ConsultantName).HasMaxLength(150);
                entity.Property(x => x.UtilityAccountNumber).HasMaxLength(100);
                entity.Property(x => x.Insurer).HasMaxLength(150);
                entity.Property(x => x.InsurancePolicyNumber).HasMaxLength(50);
                entity.Property(x => x.RegistrationNotes).HasMaxLength(1000);
                entity.Property(x => x.FairValue).HasPrecision(18, 2);
                entity.Property(x => x.ValuerName).HasMaxLength(150);
            });

            modelBuilder.Entity<DevelopmentProject>(entity =>
            {
                entity.Property(x => x.Code).HasMaxLength(20);
                entity.Property(x => x.Name).HasMaxLength(150);
                entity.Property(x => x.IntendedUse).HasMaxLength(20);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.CompletionCertificateNumber).HasMaxLength(50);
                entity.Property(x => x.Notes).HasMaxLength(1000);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Property).WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.WipAccount).WithMany().HasForeignKey(x => x.WipAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.MainContractorVendor).WithMany().HasForeignKey(x => x.MainContractorVendorId).OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(x => x.BudgetLines)
                    .WithOne(x => x.DevelopmentProject)
                    .HasForeignKey(x => x.DevelopmentProjectId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();

                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<DevelopmentBudgetLine>(entity =>
            {
                entity.Property(x => x.CostCategory).HasMaxLength(30);
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.HasIndex(x => new { x.DevelopmentProjectId, x.CostCategory }).IsUnique();
            });

            modelBuilder.Entity<DevelopmentTransfer>(entity =>
            {
                entity.Ignore(x => x.TotalCost);
                entity.Property(x => x.LandCost).HasPrecision(18, 2);
                entity.Property(x => x.BuildingCost).HasPrecision(18, 2);
                entity.Property(x => x.CreatedByName).HasMaxLength(200);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.DevelopmentProject).WithMany().HasForeignKey(x => x.DevelopmentProjectId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.RentalFixedAsset).WithMany().HasForeignKey(x => x.RentalFixedAssetId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.OwnUseFixedAsset).WithMany().HasForeignKey(x => x.OwnUseFixedAssetId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.JournalEntry).WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(x => x.Lines)
                    .WithOne(x => x.DevelopmentTransfer)
                    .HasForeignKey(x => x.DevelopmentTransferId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<DevelopmentTransferLine>(entity =>
            {
                entity.Ignore(x => x.TotalCost);
                entity.Property(x => x.IntendedUse).HasMaxLength(20);
                entity.Property(x => x.AreaSqFt).HasPrecision(18, 2);
                entity.Property(x => x.LandCost).HasPrecision(18, 2);
                entity.Property(x => x.BuildingCost).HasPrecision(18, 2);
                entity.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<JournalEntryLine>(entity =>
            {
                entity.Property(x => x.DevelopmentCostCategory).HasMaxLength(30);
                entity.HasOne(x => x.DevelopmentProject).WithMany().HasForeignKey(x => x.DevelopmentProjectId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.DevelopmentProjectId);
            });

            modelBuilder.Entity<VendorBillLine>(entity =>
            {
                entity.Property(x => x.DevelopmentCostCategory).HasMaxLength(30);
                entity.HasOne(x => x.DevelopmentProject).WithMany().HasForeignKey(x => x.DevelopmentProjectId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Unit>().Property(x => x.FirstSupplyType).HasMaxLength(10);
            modelBuilder.Entity<Unit>().Property(x => x.FloorSide).HasMaxLength(10);
            modelBuilder.Entity<Unit>().Property(x => x.FirstSupplyReference).HasMaxLength(100);

            modelBuilder.Entity<UnitSale>(entity =>
            {
                entity.Property(x => x.ContractNumber).HasMaxLength(30);
                entity.Property(x => x.VatCategory).HasMaxLength(20);
                entity.Property(x => x.VatReason).HasMaxLength(300);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.BuyerTitleDeedNumber).HasMaxLength(50);
                entity.Property(x => x.Notes).HasMaxLength(1000);
                entity.Property(x => x.CreatedByName).HasMaxLength(200);
                entity.Property(x => x.SalePrice).HasPrecision(18, 2);
                entity.Property(x => x.VatRate).HasPrecision(9, 4);
                entity.Property(x => x.VatAmount).HasPrecision(18, 2);
                entity.Property(x => x.TotalPrice).HasPrecision(18, 2);
                entity.Property(x => x.CostOfSales).HasPrecision(18, 2);
                entity.Property(x => x.HandoverNetAmount).HasPrecision(18, 2);
                entity.Property(x => x.HandoverVatAmount).HasPrecision(18, 2);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Buyer).WithMany().HasForeignKey(x => x.BuyerId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.HandoverJournalEntry).WithMany().HasForeignKey(x => x.HandoverJournalEntryId).OnDelete(DeleteBehavior.Restrict);
                entity.HasMany(x => x.Instalments).WithOne(x => x.UnitSale).HasForeignKey(x => x.UnitSaleId).OnDelete(DeleteBehavior.Cascade);
                entity.HasMany(x => x.Receipts).WithOne(x => x.UnitSale).HasForeignKey(x => x.UnitSaleId).OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new { x.CompanyId, x.ContractNumber }).IsUnique();
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<UnitSaleInstalment>(entity =>
            {
                entity.Property(x => x.Description).HasMaxLength(150);
                entity.Property(x => x.Amount).HasPrecision(18, 2);
            });

            modelBuilder.Entity<UnitSaleReceipt>(entity =>
            {
                entity.Property(x => x.ReceiptNumber).HasMaxLength(30);
                entity.Property(x => x.TaxInvoiceNumber).HasMaxLength(30);
                entity.Property(x => x.PaymentMethod).HasMaxLength(50);
                entity.Property(x => x.Reference).HasMaxLength(100);
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.NetAmount).HasPrecision(18, 2);
                entity.Property(x => x.VatAmount).HasPrecision(18, 2);
                entity.HasOne(x => x.JournalEntry).WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.ReceiptNumber }).IsUnique();
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<CapitalAssetVatRecord>(entity =>
            {
                entity.Property(x => x.Name).HasMaxLength(150);
                entity.Property(x => x.Notes).HasMaxLength(1000);
                entity.Property(x => x.AssetValue).HasPrecision(18, 2);
                entity.Property(x => x.InputVat).HasPrecision(18, 2);
                entity.Property(x => x.RecoveredVat).HasPrecision(18, 2);
                entity.Property(x => x.OriginalRecoveryPercent).HasPrecision(9, 4);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Property).WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.DevelopmentProject).WithMany().HasForeignKey(x => x.DevelopmentProjectId).OnDelete(DeleteBehavior.Restrict);
                entity.HasMany(x => x.Adjustments).WithOne(x => x.CapitalAssetVatRecord).HasForeignKey(x => x.CapitalAssetVatRecordId).OnDelete(DeleteBehavior.Cascade);
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<CapitalAssetVatAdjustment>(entity =>
            {
                entity.Property(x => x.Notes).HasMaxLength(500);
                entity.Property(x => x.TaxableUsePercent).HasPrecision(9, 4);
                entity.Property(x => x.Adjustment).HasPrecision(18, 2);
                entity.HasOne(x => x.JournalEntry).WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CapitalAssetVatRecordId, x.YearNumber }).IsUnique();
            });

            modelBuilder.Entity<PropertyPhoto>(entity =>
            {
                entity.Property(x => x.Title).HasMaxLength(100);
                entity.Property(x => x.FileName).HasMaxLength(260);
                entity.Property(x => x.ContentType).HasMaxLength(100);
                entity.Property(x => x.UploadedByName).HasMaxLength(200);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Property).WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasMany(x => x.Areas).WithOne(x => x.PropertyPhoto).HasForeignKey(x => x.PropertyPhotoId).OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new { x.CompanyId, x.PropertyId });
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<UnitPhotoArea>(entity =>
            {
                entity.Property(x => x.PointsJson).HasMaxLength(4000);
                entity.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);

                // One outline per unit per photo.
                entity.HasIndex(x => new { x.PropertyPhotoId, x.UnitId }).IsUnique();
            });

            modelBuilder.Entity<Unit>().Property(x => x.TitleDeedNumber).HasMaxLength(50);
            modelBuilder.Entity<Unit>().Property(x => x.UtilityAccountNumber).HasMaxLength(50);

            modelBuilder.Entity<PropertyDocument>(entity =>
            {
                entity.Property(x => x.DocumentType).HasMaxLength(50);
                entity.Property(x => x.DocumentNumber).HasMaxLength(100);
                entity.Property(x => x.Description).HasMaxLength(300);
                entity.Property(x => x.FileName).HasMaxLength(260);
                entity.Property(x => x.ContentType).HasMaxLength(100);
                entity.Property(x => x.UploadedByName).HasMaxLength(200);

                entity.HasOne(x => x.Company)
                    .WithMany()
                    .HasForeignKey(x => x.CompanyId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Property)
                    .WithMany(x => x.Documents)
                    .HasForeignKey(x => x.PropertyId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Unit)
                    .WithMany()
                    .HasForeignKey(x => x.UnitId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new { x.CompanyId, x.PropertyId });

                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });
            modelBuilder.Entity<Unit>().Property(x => x.VatCategory).HasMaxLength(20);
            modelBuilder.Entity<Tenant>().Property(x => x.TrnNumber).HasMaxLength(30);
            modelBuilder.Entity<Vendor>().Property(x => x.TrnNumber).HasMaxLength(30);

            // Bounded so the unique indexes below stay within SQL Server's key size.
            modelBuilder.Entity<Tenant>().Property(x => x.TradeLicenseNumber).HasMaxLength(60);
            modelBuilder.Entity<Vendor>().Property(x => x.TradeLicenseNumber).HasMaxLength(60);

            modelBuilder.Entity<Lease>().Property(x => x.VatCategory).HasMaxLength(20);
            modelBuilder.Entity<Lease>().Property(x => x.VatOverrideReason).HasMaxLength(300);

            modelBuilder.Entity<CustomerInvoice>().Property(x => x.VatCategory).HasMaxLength(20);
            modelBuilder.Entity<CustomerInvoice>().Property(x => x.VatRate).HasPrecision(9, 4);
            modelBuilder.Entity<CustomerInvoice>().Property(x => x.TaxInvoiceNumber).HasMaxLength(40);
            modelBuilder.Entity<CustomerInvoice>().Property(x => x.TaxInvoiceType).HasMaxLength(20);
            modelBuilder.Entity<CustomerInvoice>().Property(x => x.RecipientTrn).HasMaxLength(30);

            modelBuilder.Entity<VendorBill>().Property(x => x.InputTaxAttribution).HasMaxLength(20);
            modelBuilder.Entity<VendorBill>().Property(x => x.RecoverableVatAmount).HasPrecision(18, 2);
            modelBuilder.Entity<VendorBill>().Property(x => x.NonRecoverableVatAmount).HasPrecision(18, 2);
            modelBuilder.Entity<VendorBill>().Property(x => x.AppliedRecoveryRate).HasPrecision(9, 4);
            modelBuilder.Entity<VendorBill>().Property(x => x.SupplierTrn).HasMaxLength(30);

            modelBuilder.Entity<CustomerInvoice>()
                .HasIndex(x => new { x.CompanyId, x.TaxInvoiceNumber })
                .IsUnique()
                .HasFilter("[TaxInvoiceNumber] IS NOT NULL");

            // ----- E-invoicing -----------------------------------------------
            modelBuilder.Entity<Company>().Property(x => x.TaxIdentificationNumber).HasMaxLength(20);
            modelBuilder.Entity<Company>().Property(x => x.TradeLicenseNumber).HasMaxLength(60);
            modelBuilder.Entity<Company>().Property(x => x.LegalRegistrationType).HasMaxLength(10);
            modelBuilder.Entity<Company>().Property(x => x.LicensingAuthority).HasMaxLength(200);
            modelBuilder.Entity<Company>().Property(x => x.Emirate).HasMaxLength(10);
            modelBuilder.Entity<Company>().Property(x => x.CountryCode).HasMaxLength(2);
            modelBuilder.Entity<Company>().Property(x => x.BankIban).HasMaxLength(40);
            modelBuilder.Entity<Company>().Property(x => x.BankAccountName).HasMaxLength(200);
            modelBuilder.Entity<Company>().Property(x => x.EInvoicingRevenueBand).HasMaxLength(20);
            modelBuilder.Entity<Company>().Property(x => x.EInvoicingProviderName).HasMaxLength(200);
            modelBuilder.Entity<Company>().Property(x => x.EInvoicePaymentMeansCode).HasMaxLength(10);

            modelBuilder.Entity<Tenant>().Property(x => x.PartyType).HasMaxLength(20);
            modelBuilder.Entity<Tenant>().Property(x => x.TaxIdentificationNumber).HasMaxLength(20);
            modelBuilder.Entity<Tenant>().Property(x => x.LegalRegistrationType).HasMaxLength(10);
            modelBuilder.Entity<Tenant>().Property(x => x.LicensingAuthority).HasMaxLength(200);
            modelBuilder.Entity<Tenant>().Property(x => x.PassportIssuingCountry).HasMaxLength(2);
            modelBuilder.Entity<Tenant>().Property(x => x.City).HasMaxLength(100);
            modelBuilder.Entity<Tenant>().Property(x => x.Emirate).HasMaxLength(10);
            modelBuilder.Entity<Tenant>().Property(x => x.CountryCode).HasMaxLength(2);

            modelBuilder.Entity<Vendor>().Property(x => x.TaxIdentificationNumber).HasMaxLength(20);

            modelBuilder.Entity<CustomerInvoice>().Property(x => x.CreditedAmount).HasPrecision(18, 2);
            modelBuilder.Entity<CustomerInvoice>().Property(x => x.EInvoiceStatus).HasMaxLength(20);
            modelBuilder.Entity<CustomerInvoice>().Property(x => x.EInvoiceProviderReference).HasMaxLength(200);
            modelBuilder.Entity<CustomerInvoice>().Property(x => x.EInvoiceRejectionReason).HasMaxLength(2000);

            // The monitor lists what is still to be sent, company by company.
            modelBuilder.Entity<CustomerInvoice>()
                .HasIndex(x => new { x.CompanyId, x.EInvoiceStatus });

            modelBuilder.Entity<CustomerCreditNote>().Property(x => x.CreditNoteNumber).HasMaxLength(40);
            modelBuilder.Entity<CustomerCreditNote>().Property(x => x.ReasonCode).HasMaxLength(20);
            modelBuilder.Entity<CustomerCreditNote>().Property(x => x.Reason).HasMaxLength(500);
            modelBuilder.Entity<CustomerCreditNote>().Property(x => x.VatCategory).HasMaxLength(20);
            modelBuilder.Entity<CustomerCreditNote>().Property(x => x.VatRate).HasPrecision(9, 4);
            modelBuilder.Entity<CustomerCreditNote>().Property(x => x.Subtotal).HasPrecision(18, 2);
            modelBuilder.Entity<CustomerCreditNote>().Property(x => x.VatAmount).HasPrecision(18, 2);
            modelBuilder.Entity<CustomerCreditNote>().Property(x => x.TotalAmount).HasPrecision(18, 2);
            modelBuilder.Entity<CustomerCreditNote>().Property(x => x.Status).HasMaxLength(20);
            modelBuilder.Entity<CustomerCreditNote>().Property(x => x.EInvoiceStatus).HasMaxLength(20);
            modelBuilder.Entity<CustomerCreditNote>().Property(x => x.EInvoiceProviderReference).HasMaxLength(200);
            modelBuilder.Entity<CustomerCreditNote>().Property(x => x.EInvoiceRejectionReason).HasMaxLength(2000);

            modelBuilder.Entity<CustomerCreditNote>()
                .HasIndex(x => new { x.CompanyId, x.CreditNoteNumber })
                .IsUnique();

            modelBuilder.Entity<CustomerCreditNote>()
                .HasIndex(x => new { x.CompanyId, x.EInvoiceStatus });

            modelBuilder.Entity<CustomerCreditNote>()
                .HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerCreditNote>()
                .HasOne(x => x.CustomerInvoice)
                .WithMany(x => x.CreditNotes)
                .HasForeignKey(x => x.CustomerInvoiceId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerCreditNote>()
                .HasOne(x => x.DebitAccount)
                .WithMany()
                .HasForeignKey(x => x.DebitAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerCreditNote>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ReceivedEInvoice>().Property(x => x.Uuid).HasMaxLength(100);
            modelBuilder.Entity<ReceivedEInvoice>().Property(x => x.DocumentTypeCode).HasMaxLength(10);
            modelBuilder.Entity<ReceivedEInvoice>().Property(x => x.DocumentNumber).HasMaxLength(100);
            modelBuilder.Entity<ReceivedEInvoice>().Property(x => x.SupplierName).HasMaxLength(300);
            modelBuilder.Entity<ReceivedEInvoice>().Property(x => x.SupplierTrn).HasMaxLength(30);
            modelBuilder.Entity<ReceivedEInvoice>().Property(x => x.SupplierTin).HasMaxLength(20);
            modelBuilder.Entity<ReceivedEInvoice>().Property(x => x.Currency).HasMaxLength(3);
            modelBuilder.Entity<ReceivedEInvoice>().Property(x => x.NetAmount).HasPrecision(18, 2);
            modelBuilder.Entity<ReceivedEInvoice>().Property(x => x.VatAmount).HasPrecision(18, 2);
            modelBuilder.Entity<ReceivedEInvoice>().Property(x => x.TotalAmount).HasPrecision(18, 2);
            modelBuilder.Entity<ReceivedEInvoice>().Property(x => x.FileName).HasMaxLength(260);
            modelBuilder.Entity<ReceivedEInvoice>().Property(x => x.Status).HasMaxLength(20);

            // The same supplier document can never be booked twice.
            modelBuilder.Entity<ReceivedEInvoice>()
                .HasIndex(x => new { x.CompanyId, x.Uuid })
                .IsUnique();

            modelBuilder.Entity<ReceivedEInvoice>()
                .HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ReceivedEInvoice>()
                .HasOne(x => x.Vendor)
                .WithMany()
                .HasForeignKey(x => x.VendorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ReceivedEInvoice>()
                .HasOne(x => x.VendorBill)
                .WithMany()
                .HasForeignKey(x => x.VendorBillId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<OwnerWithdrawalCategory>()
                .Property(x => x.Code)
                .HasMaxLength(30)
                .IsRequired();

            modelBuilder.Entity<OwnerWithdrawalCategory>()
                .Property(x => x.Name)
                .HasMaxLength(150)
                .IsRequired();

            modelBuilder.Entity<OwnerWithdrawalCategory>()
                .Property(x => x.Description)
                .HasMaxLength(500);

            modelBuilder.Entity<TenantPortalUser>()
                .Property(x => x.Username)
                .HasMaxLength(200)
                .IsRequired();

            modelBuilder.Entity<TenantPortalUser>()
                .Property(x => x.PasswordHash)
                .HasMaxLength(500)
                .IsRequired();

            modelBuilder.Entity<AccountingPeriod>()
                .Property(x => x.Name)
                .HasMaxLength(100);

            modelBuilder.Entity<FiscalYearClosing>()
                .Property(x => x.ProfitOrLossAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<FiscalYearClosing>()
                .Property(x => x.Notes)
                .HasMaxLength(500);

            modelBuilder.Entity<Property>()
                .Property(x => x.Code)
                .HasMaxLength(50);

            modelBuilder.Entity<Property>()
                .Property(x => x.Name)
                .HasMaxLength(200);

            modelBuilder.Entity<Property>()
                .Property(x => x.Address)
                .HasMaxLength(500);

            modelBuilder.Entity<Property>()
                .Property(x => x.PropertyType)
                .HasMaxLength(50);

            modelBuilder.Entity<Property>()
                .Property(x => x.OwnershipType)
                .HasMaxLength(20);

            modelBuilder.Entity<Property>()
                .Property(x => x.OwnerName)
                .HasMaxLength(200);

            modelBuilder.Entity<Property>()
                .Property(x => x.AssetValue)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Property>()
                .Property(x => x.CommissionPercentage)
                .HasPrecision(5, 2);

            modelBuilder.Entity<Unit>()
                .Property(x => x.UnitNumber)
                .HasMaxLength(50);

            modelBuilder.Entity<Unit>()
                .Property(x => x.Floor)
                .HasMaxLength(50);

            modelBuilder.Entity<Unit>()
                .Property(x => x.UsageType)
                .HasMaxLength(50);

            modelBuilder.Entity<Unit>()
                .Property(x => x.AreaSqFt)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Unit>()
                .Property(x => x.StandardAnnualRent)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Unit>()
                .Property(x => x.Status)
                .HasMaxLength(30);

            modelBuilder.Entity<Tenant>()
                .Property(x => x.Code)
                .HasMaxLength(50);

            modelBuilder.Entity<Tenant>()
                .Property(x => x.FullName)
                .HasMaxLength(200);

            modelBuilder.Entity<Tenant>()
                .Property(x => x.Email)
                .HasMaxLength(200);

            modelBuilder.Entity<Tenant>()
                .Property(x => x.Phone)
                .HasMaxLength(50);

            modelBuilder.Entity<Tenant>()
                .Property(x => x.Nationality)
                .HasMaxLength(100);

            modelBuilder.Entity<Tenant>()
                .Property(x => x.IdNumber)
                .HasMaxLength(100);

            modelBuilder.Entity<Tenant>()
                .Property(x => x.PassportNumber)
                .HasMaxLength(100);

            modelBuilder.Entity<Tenant>()
                .Property(x => x.Address)
                .HasMaxLength(500);

            modelBuilder.Entity<Tenant>()
                .Property(x => x.OpeningBalance)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Vendor>()
                .Property(x => x.OpeningBalance)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Lease>()
                .Property(x => x.ContractNumber)
                .HasMaxLength(100);

            modelBuilder.Entity<Lease>()
                .Property(x => x.AnnualRent)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Lease>()
                .Property(x => x.VatRate)
                .HasPrecision(5, 2);

            modelBuilder.Entity<Lease>()
                .Property(x => x.SecurityDepositAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Lease>()
                .Property(x => x.Status)
                .HasMaxLength(30);

            modelBuilder.Entity<Lease>()
                .Property(x => x.Notes)
                .HasMaxLength(1000);

            modelBuilder.Entity<Vendor>()
                .Property(x => x.Code)
                .HasMaxLength(50);

            modelBuilder.Entity<Vendor>()
                .Property(x => x.Name)
                .HasMaxLength(200);

            modelBuilder.Entity<Vendor>()
                .Property(x => x.ServiceType)
                .HasMaxLength(100);

            modelBuilder.Entity<Vendor>()
                .Property(x => x.ContactPerson)
                .HasMaxLength(200);

            modelBuilder.Entity<Vendor>()
                .Property(x => x.Email)
                .HasMaxLength(200);

            modelBuilder.Entity<Vendor>()
                .Property(x => x.Phone)
                .HasMaxLength(50);

            modelBuilder.Entity<CustomerInvoice>()
                .Property(x => x.InvoiceNumber)
                .HasMaxLength(100);

            modelBuilder.Entity<CustomerInvoice>()
                .Property(x => x.Subtotal)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CustomerInvoice>()
                .Property(x => x.VatAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CustomerInvoice>()
                .Property(x => x.TotalAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CustomerInvoice>()
                .Property(x => x.AmountPaid)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CustomerInvoice>()
                .Property(x => x.Status)
                .HasMaxLength(30);

            modelBuilder.Entity<CustomerReceipt>()
                .Property(x => x.ReceiptNumber)
                .HasMaxLength(100);

            modelBuilder.Entity<CustomerReceipt>()
                .Property(x => x.Amount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CustomerReceipt>()
                .Property(x => x.PaymentMethod)
                .HasMaxLength(30);

            modelBuilder.Entity<VendorBill>()
                .Property(x => x.BillNumber)
                .HasMaxLength(100);

            modelBuilder.Entity<VendorBill>()
                .Property(x => x.Subtotal)
                .HasPrecision(18, 2);

            modelBuilder.Entity<VendorBill>()
                .Property(x => x.VatAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<VendorBill>()
                .Property(x => x.TotalAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<VendorBill>()
                .Property(x => x.AmountPaid)
                .HasPrecision(18, 2);

            modelBuilder.Entity<VendorBill>()
                .Property(x => x.ExpenseCategory)
                .HasMaxLength(100);

            modelBuilder.Entity<VendorBill>()
                .Property(x => x.Status)
                .HasMaxLength(30);

            modelBuilder.Entity<VendorBill>()
                .Property(x => x.VendorReference)
                .HasMaxLength(100);

            modelBuilder.Entity<VendorBill>()
                .Property(x => x.Notes)
                .HasMaxLength(1000);

            // ---- Vendor bill lines ------------------------------------------
            modelBuilder.Entity<VendorBillLine>()
                .Property(x => x.Description)
                .HasMaxLength(300);

            modelBuilder.Entity<VendorBillLine>()
                .Property(x => x.InputTaxAttribution)
                .HasMaxLength(20);

            modelBuilder.Entity<VendorBillLine>().Property(x => x.Quantity).HasPrecision(18, 4);
            modelBuilder.Entity<VendorBillLine>().Property(x => x.UnitPrice).HasPrecision(18, 4);
            modelBuilder.Entity<VendorBillLine>().Property(x => x.LineSubtotal).HasPrecision(18, 2);
            modelBuilder.Entity<VendorBillLine>().Property(x => x.VatRate).HasPrecision(9, 4);
            modelBuilder.Entity<VendorBillLine>().Property(x => x.VatAmount).HasPrecision(18, 2);
            modelBuilder.Entity<VendorBillLine>().Property(x => x.RecoverableVatAmount).HasPrecision(18, 2);
            modelBuilder.Entity<VendorBillLine>().Property(x => x.NonRecoverableVatAmount).HasPrecision(18, 2);
            modelBuilder.Entity<VendorBillLine>().Property(x => x.AppliedRecoveryRate).HasPrecision(9, 4);
            modelBuilder.Entity<VendorBillLine>().Property(x => x.LineTotal).HasPrecision(18, 2);

            // Cascade is right here and nowhere else in this schema: a line has no
            // meaning apart from its bill, and a draft bill is routinely re-saved with
            // rows removed.
            modelBuilder.Entity<VendorBillLine>()
                .HasOne(x => x.VendorBill)
                .WithMany(x => x.Lines)
                .HasForeignKey(x => x.VendorBillId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<VendorBillLine>()
                .HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VendorBillLine>()
                .HasOne(x => x.Property)
                .WithMany()
                .HasForeignKey(x => x.PropertyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VendorBillLine>()
                .HasOne(x => x.Unit)
                .WithMany()
                .HasForeignKey(x => x.UnitId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VendorBillLine>()
                .HasIndex(x => new { x.VendorBillId, x.LineNumber });

            modelBuilder.Entity<VendorPayment>()
                .Property(x => x.PaymentNumber)
                .HasMaxLength(100);

            modelBuilder.Entity<VendorPayment>()
                .Property(x => x.Amount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<VendorPayment>()
                .Property(x => x.PaymentMethod)
                .HasMaxLength(30);

            modelBuilder.Entity<SecurityDeposit>()
                .Property(x => x.ReferenceNumber)
                .HasMaxLength(100);

            modelBuilder.Entity<SecurityDeposit>()
                .Property(x => x.Amount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SecurityDeposit>()
                .Property(x => x.PaymentMethod)
                .HasMaxLength(30);

            modelBuilder.Entity<SecurityDeposit>()
                .Property(x => x.Status)
                .HasMaxLength(30);

            modelBuilder.Entity<PdcReceipt>()
                .Property(x => x.ChequeNumber)
                .HasMaxLength(100);

            modelBuilder.Entity<PdcReceipt>()
                .Property(x => x.BankName)
                .HasMaxLength(200);

            modelBuilder.Entity<PdcReceipt>()
                .Property(x => x.Amount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PdcReceipt>()
                .Property(x => x.SettlementMode)
                .HasMaxLength(30);

            modelBuilder.Entity<PdcReceipt>()
                .Property(x => x.Status)
                .HasMaxLength(30);

            modelBuilder.Entity<PdcReceipt>()
                .Property(x => x.Notes)
                .HasMaxLength(1000);

            modelBuilder.Entity<FixedAsset>()
                .Property(x => x.AssetCode)
                .HasMaxLength(100);

            modelBuilder.Entity<FixedAsset>()
                .Property(x => x.Name)
                .HasMaxLength(200);

            modelBuilder.Entity<FixedAsset>()
                .Property(x => x.AssetCategory)
                .HasMaxLength(100);

            modelBuilder.Entity<FixedAsset>()
                .Property(x => x.Cost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<FixedAsset>()
                .Property(x => x.ResidualValue)
                .HasPrecision(18, 2);

            modelBuilder.Entity<FixedAsset>()
                .Property(x => x.OpeningCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<FixedAsset>()
                .Property(x => x.OpeningAccumulatedDepreciation)
                .HasPrecision(18, 2);

            modelBuilder.Entity<FixedAsset>()
                .Property(x => x.DepreciationMethod)
                .HasMaxLength(30);

            modelBuilder.Entity<FixedAsset>()
                .Property(x => x.Status)
                .HasMaxLength(30);

            modelBuilder.Entity<FixedAsset>()
                .Property(x => x.Notes)
                .HasMaxLength(1000);

            modelBuilder.Entity<FixedAssetAcquisitionCost>()
                .Property(x => x.CostType)
                .HasMaxLength(50);

            modelBuilder.Entity<FixedAssetAcquisitionCost>()
                .Property(x => x.Description)
                .HasMaxLength(300);

            modelBuilder.Entity<FixedAssetAcquisitionCost>()
                .Property(x => x.Amount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<FixedAssetAcquisitionCost>()
                .Property(x => x.Status)
                .HasMaxLength(30);

            modelBuilder.Entity<FixedAssetAcquisitionCost>()
                .Property(x => x.SourceDocumentNumber)
                .HasMaxLength(100);

            modelBuilder.Entity<FixedAssetAcquisitionCost>()
                .Property(x => x.ReferenceNumber)
                .HasMaxLength(100);

            modelBuilder.Entity<FixedAssetAcquisitionCost>()
                .Property(x => x.Notes)
                .HasMaxLength(1000);

            modelBuilder.Entity<FixedAssetDepreciation>()
                .Property(x => x.Amount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<FixedAssetDepreciation>()
                .Property(x => x.Status)
                .HasMaxLength(30);

            modelBuilder.Entity<PrepaymentSchedule>()
                .Property(x => x.ScheduleNumber)
                .HasMaxLength(30);

            modelBuilder.Entity<PrepaymentSchedule>()
                .Property(x => x.Description)
                .HasMaxLength(300);

            modelBuilder.Entity<PrepaymentSchedule>()
                .Property(x => x.PrepaymentType)
                .HasMaxLength(30);

            modelBuilder.Entity<PrepaymentSchedule>()
                .Property(x => x.TotalAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PrepaymentSchedule>()
                .Property(x => x.Status)
                .HasMaxLength(30);

            modelBuilder.Entity<PrepaymentSchedule>()
                .Property(x => x.ReferenceNumber)
                .HasMaxLength(100);

            modelBuilder.Entity<PrepaymentSchedule>()
                .Property(x => x.Notes)
                .HasMaxLength(1000);

            modelBuilder.Entity<PrepaymentScheduleLine>()
                .Property(x => x.Amount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PrepaymentScheduleLine>()
                .Property(x => x.Status)
                .HasMaxLength(30);

            modelBuilder.Entity<RevenueRecognitionSchedule>()
                .Property(x => x.Amount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<RevenueRecognitionSchedule>()
                .Property(x => x.Status)
                .HasMaxLength(30);

            modelBuilder.Entity<Account>()
                .HasIndex(x => new { x.CompanyId, x.Code })
                .IsUnique();

            modelBuilder.Entity<AppUser>()
                .HasIndex(x => x.Username)
                .IsUnique();

            modelBuilder.Entity<AppUserCompany>()
                .HasIndex(x => new { x.AppUserId, x.CompanyId })
                .IsUnique();

            modelBuilder.Entity<AppUserPermission>()
                .HasIndex(x => new { x.AppUserId, x.PermissionKey })
                .IsUnique();

            modelBuilder.Entity<CompanySubscription>()
                .HasIndex(x => x.CompanyId)
                .IsUnique();

            modelBuilder.Entity<SubscriptionActivationCode>()
                .HasIndex(x => x.Code)
                .IsUnique();

            modelBuilder.Entity<SubscriptionActivationCode>()
                .HasIndex(x => new { x.CompanyId, x.IssuedAt });

            modelBuilder.Entity<AuditLog>()
                .HasIndex(x => x.OccurredAt);

            modelBuilder.Entity<UserFeedback>()
                .HasIndex(x => new { x.CompanyId, x.SubmittedAt });

            modelBuilder.Entity<UserFeedback>()
                .HasIndex(x => x.Status);

            modelBuilder.Entity<AccountingPeriod>()
                .HasIndex(x => new { x.CompanyId, x.FiscalYear, x.PeriodNumber })
                .IsUnique();

            modelBuilder.Entity<FiscalYearClosing>()
                .HasIndex(x => new { x.CompanyId, x.FiscalYear })
                .IsUnique();

            modelBuilder.Entity<Property>()
                .HasIndex(x => new { x.CompanyId, x.Code })
                .IsUnique();

            modelBuilder.Entity<Unit>()
                .HasIndex(x => new { x.PropertyId, x.UnitNumber })
                .IsUnique();

            modelBuilder.Entity<Tenant>()
                .HasIndex(x => new { x.CompanyId, x.Code })
                .IsUnique();

            // Legal identifiers of the party. Filtered so that records which have not
            // captured the identifier yet - the common case - are all left alone; only
            // supplied values have to be distinct. MasterDataUniquenessService checks
            // the same rules first so the user gets a message naming the clash rather
            // than a bare index violation.
            modelBuilder.Entity<Tenant>()
                .HasIndex(x => new { x.CompanyId, x.TradeLicenseNumber })
                .IsUnique()
                .HasFilter("[TradeLicenseNumber] IS NOT NULL AND [TradeLicenseNumber] <> ''");

            modelBuilder.Entity<Tenant>()
                .HasIndex(x => new { x.CompanyId, x.IdNumber })
                .IsUnique()
                .HasFilter("[IdNumber] IS NOT NULL AND [IdNumber] <> ''");

            modelBuilder.Entity<Tenant>()
                .HasIndex(x => new { x.CompanyId, x.PassportNumber })
                .IsUnique()
                .HasFilter("[PassportNumber] IS NOT NULL AND [PassportNumber] <> ''");

            modelBuilder.Entity<Tenant>()
                .HasIndex(x => new { x.CompanyId, x.TrnNumber })
                .IsUnique()
                .HasFilter("[TrnNumber] IS NOT NULL AND [TrnNumber] <> ''");

            modelBuilder.Entity<Lease>()
                .HasIndex(x => new { x.CompanyId, x.ContractNumber })
                .IsUnique();

            modelBuilder.Entity<Vendor>()
                .HasIndex(x => new { x.CompanyId, x.Code })
                .IsUnique();

            modelBuilder.Entity<Vendor>()
                .HasIndex(x => new { x.CompanyId, x.TradeLicenseNumber })
                .IsUnique()
                .HasFilter("[TradeLicenseNumber] IS NOT NULL AND [TradeLicenseNumber] <> ''");

            modelBuilder.Entity<Vendor>()
                .HasIndex(x => new { x.CompanyId, x.TrnNumber })
                .IsUnique()
                .HasFilter("[TrnNumber] IS NOT NULL AND [TrnNumber] <> ''");

            modelBuilder.Entity<CustomerInvoice>()
                .HasIndex(x => new { x.CompanyId, x.InvoiceNumber })
                .IsUnique();

            modelBuilder.Entity<CustomerReceipt>()
                .HasIndex(x => new { x.CompanyId, x.ReceiptNumber })
                .IsUnique();

            modelBuilder.Entity<VendorBill>()
                .HasIndex(x => new { x.CompanyId, x.BillNumber })
                .IsUnique();

            modelBuilder.Entity<VendorPayment>()
                .HasIndex(x => new { x.CompanyId, x.PaymentNumber })
                .IsUnique();

            modelBuilder.Entity<PdcReceipt>()
                .HasIndex(x => new { x.CompanyId, x.ChequeNumber })
                .IsUnique();

            modelBuilder.Entity<FixedAsset>()
                .HasIndex(x => new { x.CompanyId, x.AssetCode })
                .IsUnique();

            modelBuilder.Entity<FixedAssetAcquisitionCost>()
                .HasIndex(x => new { x.FixedAssetId, x.CostDate, x.Id });

            modelBuilder.Entity<FixedAssetDepreciation>()
                .HasIndex(x => new { x.FixedAssetId, x.PeriodYear, x.PeriodMonth })
                .IsUnique();

            modelBuilder.Entity<PrepaymentSchedule>()
                .HasIndex(x => new { x.CompanyId, x.ScheduleNumber })
                .IsUnique();

            modelBuilder.Entity<PrepaymentScheduleLine>()
                .HasIndex(x => new { x.PrepaymentScheduleId, x.RecognitionDate })
                .IsUnique();

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasIndex(x => x.CompanyId)
                .IsUnique();

            modelBuilder.Entity<Account>()
                .HasOne(x => x.Company)
                .WithMany(x => x.Accounts)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Account>()
                .HasOne(x => x.ParentAccount)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<JournalEntryLine>()
                .HasOne(x => x.JournalEntry)
                .WithMany(x => x.Lines)
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<JournalEntryLine>()
                .HasOne(x => x.Account)
                .WithMany(x => x.JournalEntryLines)
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<JournalEntryLine>()
                .HasOne(x => x.OwnerWithdrawalCategory)
                .WithMany(x => x.JournalEntryLines)
                .HasForeignKey(x => x.OwnerWithdrawalCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<OwnerWithdrawalCategory>()
                .HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<OwnerWithdrawalCategory>()
                .HasIndex(x => new { x.CompanyId, x.Code })
                .IsUnique();

            modelBuilder.Entity<TenantPortalUser>()
                .HasOne(x => x.Tenant)
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TenantPortalUser>()
                .HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Login names are unique across the whole system, not per company - a tenant
            // types only a username, with no company to disambiguate it.
            modelBuilder.Entity<TenantPortalUser>()
                .HasIndex(x => x.Username)
                .IsUnique();

            // One portal login per tenant.
            modelBuilder.Entity<TenantPortalUser>()
                .HasIndex(x => x.TenantId)
                .IsUnique();

            // ---- Tenant service requests ------------------------------------
            modelBuilder.Entity<TenantServiceRequest>().Property(x => x.RequestNumber).HasMaxLength(30);
            modelBuilder.Entity<TenantServiceRequest>().Property(x => x.RequestType).HasMaxLength(30);
            modelBuilder.Entity<TenantServiceRequest>().Property(x => x.Category).HasMaxLength(50);
            modelBuilder.Entity<TenantServiceRequest>().Property(x => x.Subject).HasMaxLength(200);
            modelBuilder.Entity<TenantServiceRequest>().Property(x => x.Description).HasMaxLength(4000);
            modelBuilder.Entity<TenantServiceRequest>().Property(x => x.Priority).HasMaxLength(20);
            modelBuilder.Entity<TenantServiceRequest>().Property(x => x.Status).HasMaxLength(30);
            modelBuilder.Entity<TenantServiceRequest>().Property(x => x.ResolutionNotes).HasMaxLength(4000);

            modelBuilder.Entity<TenantServiceRequest>()
                .HasIndex(x => new { x.CompanyId, x.RequestNumber })
                .IsUnique();

            // The staff queue filters on status and the tenant's list filters on tenant.
            modelBuilder.Entity<TenantServiceRequest>()
                .HasIndex(x => new { x.CompanyId, x.Status });

            modelBuilder.Entity<TenantServiceRequest>()
                .HasIndex(x => new { x.TenantId, x.SubmittedAt });

            modelBuilder.Entity<TenantServiceRequest>()
                .HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TenantServiceRequest>()
                .HasOne(x => x.Tenant)
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TenantServiceRequest>()
                .HasOne(x => x.Lease)
                .WithMany()
                .HasForeignKey(x => x.LeaseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TenantServiceRequest>()
                .HasOne(x => x.Property)
                .WithMany()
                .HasForeignKey(x => x.PropertyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TenantServiceRequest>()
                .HasOne(x => x.Unit)
                .WithMany()
                .HasForeignKey(x => x.UnitId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TenantServiceRequest>()
                .HasOne(x => x.RelatedInvoice)
                .WithMany()
                .HasForeignKey(x => x.RelatedInvoiceId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TenantServiceRequestComment>().Property(x => x.AuthorType).HasMaxLength(20);
            modelBuilder.Entity<TenantServiceRequestComment>().Property(x => x.AuthorName).HasMaxLength(200);
            modelBuilder.Entity<TenantServiceRequestComment>().Property(x => x.Body).HasMaxLength(4000);

            // A comment has no meaning apart from its request, and a request is never
            // deleted in practice - but if one ever is, its thread goes with it.
            modelBuilder.Entity<TenantServiceRequestComment>()
                .HasOne(x => x.TenantServiceRequest)
                .WithMany(x => x.Comments)
                .HasForeignKey(x => x.TenantServiceRequestId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TenantServiceRequestComment>()
                .HasIndex(x => new { x.TenantServiceRequestId, x.CreatedAt });

            modelBuilder.Entity<AccountingPeriod>()
                .HasOne(x => x.Company)
                .WithMany(x => x.AccountingPeriods)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AppUserCompany>()
                .HasOne(x => x.AppUser)
                .WithMany(x => x.CompanyAccesses)
                .HasForeignKey(x => x.AppUserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AppUserCompany>()
                .HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AppUserPermission>()
                .HasOne(x => x.AppUser)
                .WithMany(x => x.Permissions)
                .HasForeignKey(x => x.AppUserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CompanySubscription>()
                .HasOne(x => x.Company)
                .WithOne(x => x.Subscription)
                .HasForeignKey<CompanySubscription>(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SubscriptionActivationCode>()
                .HasOne(x => x.Company)
                .WithMany(x => x.SubscriptionActivationCodes)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AuditLog>()
                .HasOne(x => x.AppUser)
                .WithMany(x => x.AuditLogs)
                .HasForeignKey(x => x.AppUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<AuditLog>()
                .HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<UserFeedback>()
                .HasOne(x => x.Company)
                .WithMany(x => x.FeedbackItems)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserFeedback>()
                .HasOne(x => x.AppUser)
                .WithMany(x => x.SubmittedFeedback)
                .HasForeignKey(x => x.AppUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserFeedback>()
                .HasOne(x => x.ReviewedByAppUser)
                .WithMany(x => x.ReviewedFeedback)
                .HasForeignKey(x => x.ReviewedByAppUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<FiscalYearClosing>()
                .HasOne(x => x.Company)
                .WithMany(x => x.FiscalYearClosings)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FiscalYearClosing>()
                .HasOne(x => x.RetainedEarningsAccount)
                .WithMany()
                .HasForeignKey(x => x.RetainedEarningsAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FiscalYearClosing>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Property>()
                .HasOne(x => x.Company)
                .WithMany(x => x.Properties)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Unit>()
                .HasOne(x => x.Property)
                .WithMany(x => x.Units)
                .HasForeignKey(x => x.PropertyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Tenant>()
                .HasOne(x => x.Company)
                .WithMany(x => x.Tenants)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Lease>()
                .HasOne(x => x.Company)
                .WithMany(x => x.Leases)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Lease>()
                .HasOne(x => x.Unit)
                .WithMany(x => x.Leases)
                .HasForeignKey(x => x.UnitId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Lease>()
                .HasOne(x => x.Tenant)
                .WithMany(x => x.Leases)
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Vendor>()
                .HasOne(x => x.Company)
                .WithMany(x => x.Vendors)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerInvoice>()
                .HasOne(x => x.Company)
                .WithMany(x => x.CustomerInvoices)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerInvoice>()
                .HasOne(x => x.Lease)
                .WithMany()
                .HasForeignKey(x => x.LeaseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerInvoice>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerReceipt>()
                .HasOne(x => x.Company)
                .WithMany(x => x.CustomerReceipts)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerReceipt>()
                .HasOne(x => x.CustomerInvoice)
                .WithMany(x => x.Receipts)
                .HasForeignKey(x => x.CustomerInvoiceId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerReceipt>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VendorBill>()
                .HasOne(x => x.Company)
                .WithMany(x => x.VendorBills)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VendorBill>()
                .HasOne(x => x.Vendor)
                .WithMany()
                .HasForeignKey(x => x.VendorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VendorBill>()
                .HasOne(x => x.Property)
                .WithMany()
                .HasForeignKey(x => x.PropertyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VendorBill>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VendorPayment>()
                .HasOne(x => x.Company)
                .WithMany(x => x.VendorPayments)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VendorPayment>()
                .HasOne(x => x.VendorBill)
                .WithMany(x => x.Payments)
                .HasForeignKey(x => x.VendorBillId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VendorPayment>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SecurityDeposit>()
                .HasOne(x => x.Company)
                .WithMany(x => x.SecurityDeposits)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SecurityDeposit>()
                .HasOne(x => x.Lease)
                .WithMany()
                .HasForeignKey(x => x.LeaseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SecurityDeposit>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PdcReceipt>()
                .HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PdcReceipt>()
                .HasOne(x => x.Lease)
                .WithMany()
                .HasForeignKey(x => x.LeaseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PdcReceipt>()
                .HasOne(x => x.Tenant)
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PdcReceipt>()
                .HasOne(x => x.CustomerInvoice)
                .WithMany()
                .HasForeignKey(x => x.CustomerInvoiceId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PdcReceipt>()
                .HasOne(x => x.MemorandumJournalEntry)
                .WithMany()
                .HasForeignKey(x => x.MemorandumJournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PdcReceipt>()
                .HasOne(x => x.ClearanceJournalEntry)
                .WithMany()
                .HasForeignKey(x => x.ClearanceJournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FixedAsset>()
                .HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FixedAsset>()
                .HasOne(x => x.Property)
                .WithMany()
                .HasForeignKey(x => x.PropertyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FixedAsset>()
                .HasOne(x => x.AssetAccount)
                .WithMany()
                .HasForeignKey(x => x.AssetAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FixedAsset>()
                .HasOne(x => x.AccumulatedDepreciationAccount)
                .WithMany()
                .HasForeignKey(x => x.AccumulatedDepreciationAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FixedAsset>()
                .HasOne(x => x.DepreciationExpenseAccount)
                .WithMany()
                .HasForeignKey(x => x.DepreciationExpenseAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FixedAsset>()
                .HasOne(x => x.CapitalizationJournalEntry)
                .WithMany()
                .HasForeignKey(x => x.CapitalizationJournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FixedAssetAcquisitionCost>()
                .HasOne(x => x.FixedAsset)
                .WithMany(x => x.AcquisitionCosts)
                .HasForeignKey(x => x.FixedAssetId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<FixedAssetAcquisitionCost>()
                .HasOne(x => x.OffsetAccount)
                .WithMany()
                .HasForeignKey(x => x.OffsetAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FixedAssetAcquisitionCost>()
                .HasOne(x => x.Vendor)
                .WithMany()
                .HasForeignKey(x => x.VendorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FixedAssetAcquisitionCost>()
                .HasOne(x => x.VendorBill)
                .WithMany()
                .HasForeignKey(x => x.VendorBillId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FixedAssetAcquisitionCost>()
                .HasOne(x => x.ExpenseAccount)
                .WithMany()
                .HasForeignKey(x => x.ExpenseAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FixedAssetAcquisitionCost>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FixedAssetDepreciation>()
                .HasOne(x => x.FixedAsset)
                .WithMany(x => x.DepreciationSchedules)
                .HasForeignKey(x => x.FixedAssetId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<FixedAssetDepreciation>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PrepaymentSchedule>()
                .HasOne(x => x.Company)
                .WithMany(x => x.PrepaymentSchedules)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PrepaymentSchedule>()
                .HasOne(x => x.PrepaidAccount)
                .WithMany()
                .HasForeignKey(x => x.PrepaidAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PrepaymentSchedule>()
                .HasOne(x => x.ExpenseAccount)
                .WithMany()
                .HasForeignKey(x => x.ExpenseAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PrepaymentSchedule>()
                .HasOne(x => x.OffsetAccount)
                .WithMany()
                .HasForeignKey(x => x.OffsetAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PrepaymentSchedule>()
                .HasOne(x => x.Vendor)
                .WithMany()
                .HasForeignKey(x => x.VendorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PrepaymentSchedule>()
                .HasOne(x => x.VendorBill)
                .WithMany()
                .HasForeignKey(x => x.VendorBillId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PrepaymentSchedule>()
                .HasOne(x => x.InitialJournalEntry)
                .WithMany()
                .HasForeignKey(x => x.InitialJournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PrepaymentScheduleLine>()
                .HasOne(x => x.PrepaymentSchedule)
                .WithMany(x => x.Lines)
                .HasForeignKey(x => x.PrepaymentScheduleId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PrepaymentScheduleLine>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RevenueRecognitionSchedule>()
                .HasOne(x => x.Company)
                .WithMany(x => x.RevenueRecognitionSchedules)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RevenueRecognitionSchedule>()
                .HasOne(x => x.Lease)
                .WithMany()
                .HasForeignKey(x => x.LeaseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RevenueRecognitionSchedule>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Company>()
                .HasOne(x => x.AccountSetting)
                .WithOne(x => x.Company)
                .HasForeignKey<CompanyAccountSetting>(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.DefaultCashAccount)
                .WithMany()
                .HasForeignKey(x => x.DefaultCashAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.AccountsReceivableAccount)
                .WithMany()
                .HasForeignKey(x => x.AccountsReceivableAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.AccountsPayableAccount)
                .WithMany()
                .HasForeignKey(x => x.AccountsPayableAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.AdvancesFromCustomersAccount)
                .WithMany()
                .HasForeignKey(x => x.AdvancesFromCustomersAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.UnearnedRevenueAccount)
                .WithMany()
                .HasForeignKey(x => x.UnearnedRevenueAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.TenantSecurityDepositLiabilityAccount)
                .WithMany()
                .HasForeignKey(x => x.TenantSecurityDepositLiabilityAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.VatRecoverableAccount)
                .WithMany()
                .HasForeignKey(x => x.VatRecoverableAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.VatPayableAccount)
                .WithMany()
                .HasForeignKey(x => x.VatPayableAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.DueToLandlordsAccount)
                .WithMany()
                .HasForeignKey(x => x.DueToLandlordsAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.RentalRevenueOwnedAccount)
                .WithMany()
                .HasForeignKey(x => x.RentalRevenueOwnedAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.ManagementFeeRevenueAccount)
                .WithMany()
                .HasForeignKey(x => x.ManagementFeeRevenueAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.OtherIncomeAccount)
                .WithMany()
                .HasForeignKey(x => x.OtherIncomeAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.DirectExpenseDefaultAccount)
                .WithMany()
                .HasForeignKey(x => x.DirectExpenseDefaultAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.GeneralExpenseDefaultAccount)
                .WithMany()
                .HasForeignKey(x => x.GeneralExpenseDefaultAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.DepreciationExpenseAccount)
                .WithMany()
                .HasForeignKey(x => x.DepreciationExpenseAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.FinanceCostAccount)
                .WithMany()
                .HasForeignKey(x => x.FinanceCostAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.RetainedEarningsPriorYearsAccount)
                .WithMany()
                .HasForeignKey(x => x.RetainedEarningsPriorYearsAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.CurrentYearProfitOrLossAccount)
                .WithMany()
                .HasForeignKey(x => x.CurrentYearProfitOrLossAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.InvestmentPropertyAccount)
                .WithMany()
                .HasForeignKey(x => x.InvestmentPropertyAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.PdcOnHandAccount)
                .WithMany()
                .HasForeignKey(x => x.PdcOnHandAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.PrepaidExpenseDefaultAccount)
                .WithMany()
                .HasForeignKey(x => x.PrepaidExpenseDefaultAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.PdcContraAccount)
                .WithMany()
                .HasForeignKey(x => x.PdcContraAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.FixedAssetDefaultAccount)
                .WithMany()
                .HasForeignKey(x => x.FixedAssetDefaultAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.FixedAssetClearingAccount)
                .WithMany()
                .HasForeignKey(x => x.FixedAssetClearingAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasOne(x => x.AccumulatedDepreciationDefaultAccount)
                .WithMany()
                .HasForeignKey(x => x.AccumulatedDepreciationDefaultAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Account>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<AccountingPeriod>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<FiscalYearClosing>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<Property>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<Unit>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.Property != null && x.Property.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<Tenant>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<CompanySubscription>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<UserFeedback>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<Lease>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<JournalEntry>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<JournalEntryLine>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.JournalEntry != null && x.JournalEntry.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<OwnerWithdrawalCategory>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<Vendor>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<CustomerInvoice>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<CustomerReceipt>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<VendorBill>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            // A line carries no CompanyId of its own; it inherits the company of its
            // bill. The filter has to be declared anyway, or EF warns that a filtered
            // principal is reachable through an unfiltered dependent.
            modelBuilder.Entity<VendorBillLine>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.VendorBill!.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<VendorPayment>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<SecurityDeposit>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<SubscriptionActivationCode>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<PdcReceipt>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<FixedAsset>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<FixedAssetAcquisitionCost>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.FixedAsset != null && x.FixedAsset.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<FixedAssetDepreciation>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.FixedAsset != null && x.FixedAsset.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<PrepaymentSchedule>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<PrepaymentScheduleLine>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.PrepaymentSchedule != null && x.PrepaymentSchedule.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<RevenueRecognitionSchedule>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<CompanyAccountSetting>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            // Service requests are read by STAFF, who carry a company claim, and by
            // TENANTS, who do not - a tenant principal has no current company, so this
            // filter switches itself off for portal traffic. That is why every portal
            // query filters on TenantId explicitly and must never rely on this one.
            modelBuilder.Entity<TenantServiceRequest>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<TenantServiceRequestComment>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled
                                     || x.TenantServiceRequest!.CompanyId == CurrentCompanyIdValue);

            // Credit notes are read by tenants in the portal too, so - as with service
            // requests - portal queries filter on the tenant explicitly.
            modelBuilder.Entity<CustomerCreditNote>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            modelBuilder.Entity<ReceivedEInvoice>()
                .HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);

            // Approvals (SQL/14_AddContractApproval.sql creates the same table on live).
            modelBuilder.Entity<ApprovalRequest>(entity =>
            {
                entity.Property(x => x.DocumentType).HasMaxLength(50);
                entity.Property(x => x.DocumentNumber).HasMaxLength(100);
                entity.Property(x => x.Summary).HasMaxLength(300);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.RequestedByName).HasMaxLength(200);
                entity.Property(x => x.DecidedByName).HasMaxLength(200);
                entity.Property(x => x.DecisionComment).HasMaxLength(1000);

                entity.HasOne(x => x.Company)
                    .WithMany()
                    .HasForeignKey(x => x.CompanyId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new { x.CompanyId, x.Status });
                entity.HasIndex(x => new { x.DocumentType, x.DocumentId });

                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<OwnerSettlement>(entity =>
            {
                entity.Property(x => x.SettlementNumber).HasMaxLength(30);
                entity.Property(x => x.OwnerName).HasMaxLength(200);
                entity.Property(x => x.PaymentMethod).HasMaxLength(50);
                entity.Property(x => x.ReferenceNumber).HasMaxLength(100);
                entity.Property(x => x.Notes).HasMaxLength(1000);
                entity.Property(x => x.CreatedByName).HasMaxLength(200);

                entity.Property(x => x.GrossRent).HasPrecision(18, 2);
                entity.Property(x => x.Commission).HasPrecision(18, 2);
                entity.Property(x => x.CommissionVat).HasPrecision(18, 2);
                entity.Property(x => x.OwnerShare).HasPrecision(18, 2);
                entity.Property(x => x.CollectedOwnerShare).HasPrecision(18, 2);
                entity.Property(x => x.PreviouslySettled).HasPrecision(18, 2);
                entity.Property(x => x.AmountPaid).HasPrecision(18, 2);

                entity.HasOne(x => x.Company)
                    .WithMany()
                    .HasForeignKey(x => x.CompanyId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.PaidFromAccount)
                    .WithMany()
                    .HasForeignKey(x => x.PaidFromAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.JournalEntry)
                    .WithMany()
                    .HasForeignKey(x => x.JournalEntryId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new { x.CompanyId, x.SettlementNumber }).IsUnique();
                entity.HasIndex(x => new { x.CompanyId, x.OwnerName });

                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<OwnerCommissionInvoice>(entity =>
            {
                entity.Property(x => x.InvoiceNumber).HasMaxLength(30);
                entity.Property(x => x.TaxInvoiceNumber).HasMaxLength(30);
                entity.Property(x => x.OwnerName).HasMaxLength(200);
                entity.Property(x => x.RecipientTrn).HasMaxLength(20);
                entity.Property(x => x.CreatedByName).HasMaxLength(200);

                entity.Property(x => x.Commission).HasPrecision(18, 2);
                entity.Property(x => x.VatRate).HasPrecision(9, 4);
                entity.Property(x => x.VatAmount).HasPrecision(18, 2);
                entity.Property(x => x.TotalAmount).HasPrecision(18, 2);

                entity.HasOne(x => x.Company)
                    .WithMany()
                    .HasForeignKey(x => x.CompanyId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.JournalEntry)
                    .WithMany()
                    .HasForeignKey(x => x.JournalEntryId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(x => x.Lines)
                    .WithOne(x => x.OwnerCommissionInvoice)
                    .HasForeignKey(x => x.OwnerCommissionInvoiceId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new { x.CompanyId, x.InvoiceNumber }).IsUnique();
                entity.HasIndex(x => new { x.CompanyId, x.OwnerName });

                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<OwnerCommissionInvoiceLine>(entity =>
            {
                entity.Property(x => x.Description).HasMaxLength(300);
                entity.Property(x => x.Commission).HasPrecision(18, 2);

                // A month's commission on a contract is invoiced once, never twice.
                entity.HasIndex(x => x.RevenueRecognitionScheduleId).IsUnique();
            });

            ConfigurePeriodClosing(modelBuilder);
            ConfigureHr(modelBuilder);
            ConfigureControls(modelBuilder);
        }
    }
}
