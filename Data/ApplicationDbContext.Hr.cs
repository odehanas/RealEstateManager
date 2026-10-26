using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;

namespace RealEstateManager.Data
{
    /// <summary>HR and payroll tables.</summary>
    public partial class ApplicationDbContext
    {
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<Employee> Employees => Set<Employee>();
        public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
        public DbSet<EmployeeAdvance> EmployeeAdvances => Set<EmployeeAdvance>();
        public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
        public DbSet<PayrollRunLine> PayrollRunLines => Set<PayrollRunLine>();
        public DbSet<PayrollPayment> PayrollPayments => Set<PayrollPayment>();
        public DbSet<PayrollSettings> PayrollSettings => Set<PayrollSettings>();
        public DbSet<PayrollAdjustment> PayrollAdjustments => Set<PayrollAdjustment>();
        public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();

        private void ConfigureHr(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PayrollAdjustment>(entity =>
            {
                entity.Property(x => x.Category).HasMaxLength(30);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.Source).HasMaxLength(20);
                entity.Property(x => x.Description).HasMaxLength(300);
                entity.Property(x => x.CreatedByName).HasMaxLength(200);
                entity.Property(x => x.DecidedByName).HasMaxLength(200);
                entity.Property(x => x.Quantity).HasPrecision(9, 2);
                entity.Property(x => x.Amount).HasPrecision(18, 2);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.PayrollRun).WithMany().HasForeignKey(x => x.PayrollRunId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.EmployeeId, x.Date });
                entity.HasIndex(x => x.PayrollRunId);
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<AttendanceRecord>(entity =>
            {
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.Notes).HasMaxLength(300);
                entity.Property(x => x.Source).HasMaxLength(20);
                entity.Property(x => x.WorkedHours).HasPrecision(9, 2);
                entity.Property(x => x.OvertimeHours).HasPrecision(9, 2);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique();
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<Department>(entity =>
            {
                entity.Property(x => x.Name).HasMaxLength(100);
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<Employee>(entity =>
            {
                entity.Property(x => x.EmployeeCode).HasMaxLength(20);
                entity.Property(x => x.FirstName).HasMaxLength(100);
                entity.Property(x => x.LastName).HasMaxLength(100);
                entity.Property(x => x.NameArabic).HasMaxLength(200);
                entity.Property(x => x.Gender).HasMaxLength(10);
                entity.Property(x => x.Nationality).HasMaxLength(100);
                entity.Property(x => x.MaritalStatus).HasMaxLength(20);
                entity.Property(x => x.Email).HasMaxLength(200);
                entity.Property(x => x.Mobile).HasMaxLength(50);
                entity.Property(x => x.Address).HasMaxLength(300);
                entity.Property(x => x.EmergencyContactName).HasMaxLength(200);
                entity.Property(x => x.EmergencyContactPhone).HasMaxLength(50);
                entity.Property(x => x.JobTitle).HasMaxLength(100);
                entity.Property(x => x.ContractType).HasMaxLength(20);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.LeavingReason).HasMaxLength(300);
                entity.Property(x => x.PassportNumber).HasMaxLength(30);
                entity.Property(x => x.EmiratesIdNumber).HasMaxLength(30);
                entity.Property(x => x.ResidenceVisaNumber).HasMaxLength(30);
                entity.Property(x => x.LabourCardNumber).HasMaxLength(30);
                entity.Property(x => x.MohrePersonCode).HasMaxLength(20);
                entity.Property(x => x.HealthInsuranceNumber).HasMaxLength(50);
                entity.Property(x => x.PaymentMethod).HasMaxLength(20);
                entity.Property(x => x.BankName).HasMaxLength(100);
                entity.Property(x => x.Iban).HasMaxLength(34);
                entity.Property(x => x.BankRoutingCode).HasMaxLength(9);
                entity.Property(x => x.Notes).HasMaxLength(1000);
                entity.Property(x => x.BasicSalary).HasPrecision(18, 2);
                entity.Property(x => x.HousingAllowance).HasPrecision(18, 2);
                entity.Property(x => x.TransportAllowance).HasPrecision(18, 2);
                entity.Property(x => x.OtherAllowance).HasPrecision(18, 2);
                entity.Property(x => x.AnnualLeaveDaysPerYear).HasPrecision(9, 2);
                entity.Property(x => x.OpeningLeaveBalance).HasPrecision(9, 2);
                entity.Property(x => x.OpeningGratuityProvision).HasPrecision(18, 2);
                entity.Ignore(x => x.MonthlyGross);
                entity.Ignore(x => x.FullName);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Property).WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new { x.CompanyId, x.EmployeeCode }).IsUnique();
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<LeaveRequest>(entity =>
            {
                entity.Property(x => x.LeaveType).HasMaxLength(20);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.Reason).HasMaxLength(500);
                entity.Property(x => x.RequestedByName).HasMaxLength(200);
                entity.Property(x => x.DecidedByName).HasMaxLength(200);
                entity.Property(x => x.DecisionComment).HasMaxLength(500);
                entity.Property(x => x.Days).HasPrecision(9, 2);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.EmployeeId, x.StartDate });
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<EmployeeAdvance>(entity =>
            {
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.MonthlyInstalment).HasPrecision(18, 2);
                entity.Property(x => x.RecoveredAmount).HasPrecision(18, 2);
                entity.Property(x => x.Reference).HasMaxLength(100);
                entity.Property(x => x.Notes).HasMaxLength(500);
                entity.Ignore(x => x.Outstanding);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.PaidFromAccount).WithMany().HasForeignKey(x => x.PaidFromAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.JournalEntry).WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<PayrollRun>(entity =>
            {
                entity.Property(x => x.RunNumber).HasMaxLength(20);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.PreparedByName).HasMaxLength(200);
                entity.Property(x => x.ApprovedByName).HasMaxLength(200);
                entity.Property(x => x.Notes).HasMaxLength(500);
                entity.Property(x => x.TotalGross).HasPrecision(18, 2);
                entity.Property(x => x.TotalDeductions).HasPrecision(18, 2);
                entity.Property(x => x.TotalNet).HasPrecision(18, 2);
                entity.Property(x => x.TotalGratuityAccrual).HasPrecision(18, 2);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.JournalEntry).WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CompanyId, x.Year, x.Month }).IsUnique();
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<PayrollRunLine>(entity =>
            {
                entity.Property(x => x.EmployeeCode).HasMaxLength(20);
                entity.Property(x => x.EmployeeName).HasMaxLength(200);
                entity.Property(x => x.JobTitle).HasMaxLength(100);
                entity.Property(x => x.DepartmentName).HasMaxLength(100);
                entity.Property(x => x.PaymentMethod).HasMaxLength(20);
                entity.Property(x => x.BankName).HasMaxLength(100);
                entity.Property(x => x.Iban).HasMaxLength(34);
                entity.Property(x => x.BankRoutingCode).HasMaxLength(9);
                entity.Property(x => x.MohrePersonCode).HasMaxLength(20);
                entity.Property(x => x.DeductionNote).HasMaxLength(200);
                entity.Property(x => x.ChequeNumber).HasMaxLength(30);
                foreach (var money in new[]
                         {
                             nameof(PayrollRunLine.FullBasic), nameof(PayrollRunLine.FullAllowances), nameof(PayrollRunLine.Basic),
                             nameof(PayrollRunLine.Housing), nameof(PayrollRunLine.Transport), nameof(PayrollRunLine.OtherAllowance),
                             nameof(PayrollRunLine.OvertimeAmount), nameof(PayrollRunLine.Bonus), nameof(PayrollRunLine.AdvanceRecovery),
                             nameof(PayrollRunLine.OtherDeductions), nameof(PayrollRunLine.GratuityAccrual)
                         })
                    entity.Property(money).HasPrecision(18, 2);
                entity.Property(x => x.UnpaidLeaveDays).HasPrecision(9, 2);
                entity.Property(x => x.AbsenceDays).HasPrecision(9, 2);
                entity.Property(x => x.OvertimeHours).HasPrecision(9, 2);
                entity.Ignore(x => x.DaysPaid);
                entity.Ignore(x => x.Allowances);
                entity.Ignore(x => x.Gross);
                entity.Ignore(x => x.TotalDeductions);
                entity.Ignore(x => x.Net);
                entity.Ignore(x => x.IsPaid);

                entity.HasOne(x => x.PayrollRun).WithMany(x => x.Lines).HasForeignKey(x => x.PayrollRunId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.PayrollPayment).WithMany(x => x.Lines).HasForeignKey(x => x.PayrollPaymentId).OnDelete(DeleteBehavior.NoAction);
                entity.HasIndex(x => new { x.PayrollRunId, x.EmployeeId }).IsUnique();
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.PayrollRun != null && x.PayrollRun.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<PayrollPayment>(entity =>
            {
                entity.Property(x => x.Method).HasMaxLength(20);
                entity.Property(x => x.Reference).HasMaxLength(100);
                entity.Property(x => x.FileName).HasMaxLength(100);
                entity.Property(x => x.CreatedByName).HasMaxLength(200);
                entity.Property(x => x.TotalAmount).HasPrecision(18, 2);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.PayrollRun).WithMany(x => x.Payments).HasForeignKey(x => x.PayrollRunId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.JournalEntry).WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });

            modelBuilder.Entity<PayrollSettings>(entity =>
            {
                entity.Property(x => x.EmployerEstablishmentId).HasMaxLength(13);
                entity.Property(x => x.EmployerBankRoutingCode).HasMaxLength(9);
                entity.Property(x => x.SalaryBankName).HasMaxLength(100);
                entity.Property(x => x.DateFormat).HasMaxLength(20);
                entity.Property(x => x.OvertimeMultiplier).HasPrecision(9, 4);
                entity.Property(x => x.RestDayOvertimeMultiplier).HasPrecision(9, 4);
                entity.Property(x => x.WeeklyOffDays).HasMaxLength(100);
                entity.Property(x => x.WorkingHoursPerDay).HasPrecision(9, 2);
                foreach (var mm in new[]
                         {
                             nameof(Domain.PayrollSettings.ChequeWidthMm), nameof(Domain.PayrollSettings.ChequeHeightMm),
                             nameof(Domain.PayrollSettings.DateXMm), nameof(Domain.PayrollSettings.DateYMm),
                             nameof(Domain.PayrollSettings.PayeeXMm), nameof(Domain.PayrollSettings.PayeeYMm),
                             nameof(Domain.PayrollSettings.AmountWordsXMm), nameof(Domain.PayrollSettings.AmountWordsYMm),
                             nameof(Domain.PayrollSettings.AmountWordsWidthMm), nameof(Domain.PayrollSettings.AmountXMm),
                             nameof(Domain.PayrollSettings.AmountYMm), nameof(Domain.PayrollSettings.ChequeFontSizePt),
                             nameof(Domain.PayrollSettings.OffsetXMm), nameof(Domain.PayrollSettings.OffsetYMm)
                         })
                    entity.Property(mm).HasPrecision(9, 2);

                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.CompanyId).IsUnique();
                entity.HasQueryFilter(x => !IsCompanyFilterEnabled || x.CompanyId == CurrentCompanyIdValue);
            });
        }
    }
}
