namespace RealEstateManager.Domain
{
    // =========================================================================
    //  HR & PAYROLL
    //
    //  Employees with their personal, contract and UAE document details; leave;
    //  salary advances; and a monthly payroll that is calculated, approved into
    //  the ledger, and paid by WPS, bank transfer or printed cheque.
    // =========================================================================

    public class Department
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    public class Employee
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>Generated, e.g. EMP-0001.</summary>
        public string EmployeeCode { get; set; } = string.Empty;

        // ---- Personal ----------------------------------------------------------
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? NameArabic { get; set; }
        public string Gender { get; set; } = "Male";
        public DateTime? DateOfBirth { get; set; }
        public string? Nationality { get; set; }
        public string? MaritalStatus { get; set; }
        public string? Email { get; set; }
        public string? Mobile { get; set; }
        public string? Address { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }

        // ---- Job and contract ----------------------------------------------------
        public int? DepartmentId { get; set; }
        public Department? Department { get; set; }
        public string JobTitle { get; set; } = string.Empty;

        /// <summary>
        /// The building the employee works for, if any. Their salary cost is then
        /// charged to that property, so it shows in P&amp;L by Property.
        /// </summary>
        public int? PropertyId { get; set; }
        public Property? Property { get; set; }

        public DateTime JoiningDate { get; set; } = DateTime.Today;
        public DateTime? ProbationEndDate { get; set; }
        public string ContractType { get; set; } = EmployeeContractTypes.Unlimited;
        public DateTime? ContractEndDate { get; set; }

        public string Status { get; set; } = EmployeeStatuses.Active;
        public DateTime? LeavingDate { get; set; }
        public string? LeavingReason { get; set; }

        // ---- UAE documents ------------------------------------------------------
        public string? PassportNumber { get; set; }
        public DateTime? PassportExpiry { get; set; }
        public string? EmiratesIdNumber { get; set; }
        public DateTime? EmiratesIdExpiry { get; set; }
        public string? ResidenceVisaNumber { get; set; }
        public DateTime? ResidenceVisaExpiry { get; set; }
        public string? LabourCardNumber { get; set; }
        public DateTime? LabourCardExpiry { get; set; }

        /// <summary>
        /// The 14-digit personal number issued by the Ministry of Human Resources
        /// (MOHRE). It identifies the employee in the WPS salary file.
        /// </summary>
        public string? MohrePersonCode { get; set; }
        public string? HealthInsuranceNumber { get; set; }
        public DateTime? HealthInsuranceExpiry { get; set; }

        // ---- Pay ------------------------------------------------------------------
        public decimal BasicSalary { get; set; }
        public decimal HousingAllowance { get; set; }
        public decimal TransportAllowance { get; set; }
        public decimal OtherAllowance { get; set; }
        public decimal MonthlyGross => BasicSalary + HousingAllowance + TransportAllowance + OtherAllowance;

        public string PaymentMethod { get; set; } = SalaryPaymentMethods.Wps;
        public string? BankName { get; set; }
        public string? Iban { get; set; }

        /// <summary>The 9-digit routing code (agent ID) of the employee's bank, required by WPS.</summary>
        public string? BankRoutingCode { get; set; }

        // ---- Leave and end of service ----------------------------------------------
        /// <summary>Annual leave days a full year earns. 30 under UAE law after the first year.</summary>
        public decimal AnnualLeaveDaysPerYear { get; set; } = 30m;

        /// <summary>Leave balance brought in from a previous system, as at <see cref="OpeningBalanceDate"/>.</summary>
        public decimal OpeningLeaveBalance { get; set; }

        /// <summary>End-of-service provision already in the books for this employee at go-live.</summary>
        public decimal OpeningGratuityProvision { get; set; }

        /// <summary>The date the two opening figures are stated at. Blank means the joining date.</summary>
        public DateTime? OpeningBalanceDate { get; set; }

        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string FullName => $"{FirstName} {LastName}".Trim();
    }

    public static class EmployeeStatuses
    {
        public const string Active = "Active";
        public const string Suspended = "Suspended";
        public const string Resigned = "Resigned";
        public const string Terminated = "Terminated";

        public static readonly string[] All = { Active, Suspended, Resigned, Terminated };
        public static bool HasLeft(string status) => status is Resigned or Terminated;
    }

    public static class EmployeeContractTypes
    {
        public const string Unlimited = "Unlimited";
        public const string Limited = "Limited";
        public const string PartTime = "Part-time";
        public const string Temporary = "Temporary";

        public static readonly string[] All = { Unlimited, Limited, PartTime, Temporary };
    }

    public static class SalaryPaymentMethods
    {
        public const string Wps = "WPS";
        public const string BankTransfer = "BankTransfer";
        public const string Cheque = "Cheque";

        public static readonly string[] All = { Wps, BankTransfer, Cheque };

        public static string Describe(string method) => method switch
        {
            Wps => "WPS (salary file)",
            BankTransfer => "Bank transfer",
            Cheque => "Cheque",
            _ => method
        };
    }

    public class LeaveRequest
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int EmployeeId { get; set; }
        public Employee? Employee { get; set; }

        public string LeaveType { get; set; } = LeaveTypes.Annual;
        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime EndDate { get; set; } = DateTime.Today;

        /// <summary>Calendar days, inclusive of both dates.</summary>
        public decimal Days { get; set; }
        public string? Reason { get; set; }

        public string Status { get; set; } = LeaveStatuses.Pending;
        public string? RequestedByName { get; set; }
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public string? DecidedByName { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? DecisionComment { get; set; }
    }

    public static class LeaveTypes
    {
        public const string Annual = "Annual";
        public const string Sick = "Sick";
        public const string Unpaid = "Unpaid";
        public const string Maternity = "Maternity";
        public const string Parental = "Parental";
        public const string Compassionate = "Compassionate";
        public const string Hajj = "Hajj";
        public const string Other = "Other";

        public static readonly string[] All = { Annual, Sick, Unpaid, Maternity, Parental, Compassionate, Hajj, Other };
    }

    public static class LeaveStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Cancelled = "Cancelled";
    }

    /// <summary>
    /// One addition to or deduction from an employee's pay - overtime, a bonus, a penalty,
    /// a day of unauthorised absence. Entered by hand or generated from attendance, then
    /// approved; the next payroll picks up every approved transaction dated on or before
    /// its month end that no earlier payroll has paid.
    /// </summary>
    public class PayrollAdjustment
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int EmployeeId { get; set; }
        public Employee? Employee { get; set; }

        public DateTime Date { get; set; } = DateTime.Today;
        public string Category { get; set; } = PayrollAdjustmentCategories.Penalty;

        /// <summary>Hours, minutes or days for the categories measured in time; otherwise blank.</summary>
        public decimal Quantity { get; set; }

        /// <summary>The amount for the categories entered as money. Time-based ones are priced by the payroll.</summary>
        public decimal Amount { get; set; }
        public string? Description { get; set; }

        public string Status { get; set; } = PayrollAdjustmentStatuses.Pending;

        /// <summary>Manual, or Attendance when generated from the attendance records.</summary>
        public string Source { get; set; } = PayrollAdjustmentSources.Manual;

        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? DecidedByName { get; set; }
        public DateTime? DecidedAt { get; set; }

        /// <summary>The approved payroll that paid it; null until then.</summary>
        public int? PayrollRunId { get; set; }
        public PayrollRun? PayrollRun { get; set; }
    }

    public static class PayrollAdjustmentStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
    }

    public static class PayrollAdjustmentSources
    {
        public const string Manual = "Manual";
        public const string Attendance = "Attendance";
    }

    /// <summary>What each kind of payroll transaction is, how it is measured and which way it goes.</summary>
    public static class PayrollAdjustmentCategories
    {
        public const string Overtime = "Overtime";
        public const string RestDayOvertime = "RestDayOvertime";
        public const string Bonus = "Bonus";
        public const string Commission = "Commission";
        public const string OtherAddition = "OtherAddition";
        public const string UnauthorisedAbsence = "UnauthorisedAbsence";
        public const string LateArrival = "LateArrival";
        public const string Penalty = "Penalty";
        public const string DamageRecovery = "DamageRecovery";
        public const string OtherDeduction = "OtherDeduction";

        public sealed record Definition(string Key, string Name, bool IsAddition, string Unit);

        public const string Hours = "hours";
        public const string Minutes = "minutes";
        public const string Days = "days";
        public const string Money = "amount";

        public static readonly IReadOnlyList<Definition> All = new List<Definition>
        {
            new(Overtime, "Overtime", true, Hours),
            new(RestDayOvertime, "Overtime on rest day / holiday", true, Hours),
            new(Bonus, "Bonus", true, Money),
            new(Commission, "Commission / incentive", true, Money),
            new(OtherAddition, "Other addition", true, Money),
            new(UnauthorisedAbsence, "Unauthorised absence", false, Days),
            new(LateArrival, "Late arrival", false, Minutes),
            new(Penalty, "Penalty", false, Money),
            new(DamageRecovery, "Damage / loss recovery", false, Money),
            new(OtherDeduction, "Other deduction", false, Money)
        };

        public static Definition Get(string key) => All.FirstOrDefault(x => x.Key == key) ?? All[^1];
    }

    /// <summary>One employee's attendance on one day.</summary>
    public class AttendanceRecord
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int EmployeeId { get; set; }
        public Employee? Employee { get; set; }

        public DateTime Date { get; set; }
        public string Status { get; set; } = AttendanceStatuses.Present;
        public TimeSpan? CheckIn { get; set; }
        public TimeSpan? CheckOut { get; set; }

        public decimal WorkedHours { get; set; }
        public decimal OvertimeHours { get; set; }
        public int LateMinutes { get; set; }
        public string? Notes { get; set; }

        /// <summary>Manual entry, or Import from a time-attendance device file.</summary>
        public string Source { get; set; } = "Manual";
    }

    public static class AttendanceStatuses
    {
        public const string Present = "Present";
        public const string Absent = "Absent";
        public const string OnLeave = "OnLeave";
        public const string WeeklyOff = "WeeklyOff";
        public const string Holiday = "Holiday";

        public static readonly string[] All = { Present, Absent, OnLeave, WeeklyOff, Holiday };

        public static string Describe(string status) => status switch
        {
            OnLeave => "On leave",
            WeeklyOff => "Weekly off",
            _ => status
        };
    }

    /// <summary>
    /// A salary advance or loan paid to an employee and recovered from their salary
    /// in monthly instalments by the payroll.
    /// </summary>
    public class EmployeeAdvance
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int EmployeeId { get; set; }
        public Employee? Employee { get; set; }

        public DateTime AdvanceDate { get; set; } = DateTime.Today;
        public decimal Amount { get; set; }
        public decimal MonthlyInstalment { get; set; }

        /// <summary>Recovered by approved payroll runs so far.</summary>
        public decimal RecoveredAmount { get; set; }
        public decimal Outstanding => Amount - RecoveredAmount;

        public int PaidFromAccountId { get; set; }
        public Account? PaidFromAccount { get; set; }
        public string? Reference { get; set; }
        public string? Notes { get; set; }

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }
    }

    public class PayrollRun
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>PR-2026-10 for October 2026.</summary>
        public string RunNumber { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Month { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        public string Status { get; set; } = PayrollRunStatuses.Draft;

        public int? PreparedByAppUserId { get; set; }
        public string? PreparedByName { get; set; }
        public DateTime PreparedAt { get; set; } = DateTime.UtcNow;
        public string? ApprovedByName { get; set; }
        public DateTime? ApprovedAt { get; set; }

        /// <summary>The salary accrual: expense against salaries payable, posted on approval.</summary>
        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public decimal TotalGross { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal TotalNet { get; set; }
        public decimal TotalGratuityAccrual { get; set; }

        public string? Notes { get; set; }

        public ICollection<PayrollRunLine> Lines { get; set; } = new List<PayrollRunLine>();
        public ICollection<PayrollPayment> Payments { get; set; } = new List<PayrollPayment>();
    }

    public static class PayrollRunStatuses
    {
        public const string Draft = "Draft";

        /// <summary>Approved and in the ledger; salaries payable, not yet (all) paid.</summary>
        public const string Approved = "Approved";
        public const string PartlyPaid = "PartlyPaid";
        public const string Paid = "Paid";
    }

    /// <summary>
    /// One employee's pay for the month. The employee's details are copied in when the
    /// run is calculated, so a later change to the employee never rewrites a payslip.
    /// </summary>
    public class PayrollRunLine
    {
        public int Id { get; set; }

        public int PayrollRunId { get; set; }
        public PayrollRun? PayrollRun { get; set; }

        public int EmployeeId { get; set; }
        public Employee? Employee { get; set; }

        // ---- Snapshot ------------------------------------------------------------
        public string EmployeeCode { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string? JobTitle { get; set; }
        public string? DepartmentName { get; set; }
        public int? PropertyId { get; set; }
        public string PaymentMethod { get; set; } = SalaryPaymentMethods.Wps;
        public string? BankName { get; set; }
        public string? Iban { get; set; }
        public string? BankRoutingCode { get; set; }
        public string? MohrePersonCode { get; set; }

        // ---- Days ------------------------------------------------------------------
        public int DaysInMonth { get; set; }

        /// <summary>Days of the month the employee was employed (joined or left mid-month).</summary>
        public int DaysEmployed { get; set; }
        public decimal UnpaidLeaveDays { get; set; }

        /// <summary>Days of unauthorised absence, from payroll transactions (and so from attendance).</summary>
        public decimal AbsenceDays { get; set; }
        public decimal DaysPaid => Math.Max(0m, DaysEmployed - UnpaidLeaveDays - AbsenceDays);

        // ---- Earnings ----------------------------------------------------------------
        public decimal FullBasic { get; set; }
        public decimal FullAllowances { get; set; }
        public decimal Basic { get; set; }
        public decimal Housing { get; set; }
        public decimal Transport { get; set; }
        public decimal OtherAllowance { get; set; }
        public decimal OvertimeHours { get; set; }
        public decimal OvertimeAmount { get; set; }
        public decimal Bonus { get; set; }
        public decimal Allowances => Housing + Transport + OtherAllowance;
        public decimal Gross => Basic + Allowances + OvertimeAmount + Bonus;

        // ---- Deductions ------------------------------------------------------------
        public decimal AdvanceRecovery { get; set; }
        public decimal OtherDeductions { get; set; }
        public string? DeductionNote { get; set; }
        public decimal TotalDeductions => AdvanceRecovery + OtherDeductions;

        public decimal Net => Gross - TotalDeductions;

        /// <summary>End-of-service provision charged this month.</summary>
        public decimal GratuityAccrual { get; set; }

        // ---- Payment --------------------------------------------------------------
        public int? PayrollPaymentId { get; set; }
        public PayrollPayment? PayrollPayment { get; set; }
        public string? ChequeNumber { get; set; }
        public bool IsPaid => PayrollPaymentId.HasValue;
    }

    /// <summary>
    /// One payment batch of a run: a WPS salary file, a bank transfer list, or a set of
    /// cheques. Posting it clears salaries payable against the bank.
    /// </summary>
    public class PayrollPayment
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int PayrollRunId { get; set; }
        public PayrollRun? PayrollRun { get; set; }

        public string Method { get; set; } = SalaryPaymentMethods.Wps;
        public DateTime PaymentDate { get; set; } = DateTime.Today;

        public int BankAccountId { get; set; }
        public Account? BankAccount { get; set; }

        public string? Reference { get; set; }
        public decimal TotalAmount { get; set; }
        public int EmployeeCount { get; set; }

        /// <summary>The WPS file name and content, kept so the same file can be downloaded again.</summary>
        public string? FileName { get; set; }
        public string? FileContent { get; set; }

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<PayrollRunLine> Lines { get; set; } = new List<PayrollRunLine>();
    }

    /// <summary>
    /// Company payroll settings: WPS employer details, the accounts payroll posts to,
    /// calculation rules and the cheque layout. One row per company, created on first use.
    /// </summary>
    public class PayrollSettings
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        // ---- WPS ------------------------------------------------------------------
        /// <summary>The employer's 13-digit MOHRE establishment number.</summary>
        public string? EmployerEstablishmentId { get; set; }

        /// <summary>The 9-digit routing code of the employer's salary bank (or agent).</summary>
        public string? EmployerBankRoutingCode { get; set; }
        public string? SalaryBankName { get; set; }

        // ---- Accounts -------------------------------------------------------------
        public int? BasicSalaryExpenseAccountId { get; set; }
        public int? AllowancesExpenseAccountId { get; set; }
        public int? OvertimeBonusExpenseAccountId { get; set; }
        public int? GratuityExpenseAccountId { get; set; }
        public int? SalariesPayableAccountId { get; set; }
        public int? GratuityProvisionAccountId { get; set; }
        public int? EmployeeAdvancesAccountId { get; set; }
        public int? DefaultBankAccountId { get; set; }

        // ---- Rules ----------------------------------------------------------------
        public decimal OvertimeMultiplier { get; set; } = 1.25m;

        /// <summary>Overtime on a rest day or public holiday: 150% under UAE law.</summary>
        public decimal RestDayOvertimeMultiplier { get; set; } = 1.5m;
        public decimal WorkingHoursPerDay { get; set; } = 8m;
        public bool AccrueGratuity { get; set; } = true;

        /// <summary>The weekly rest days, comma separated, e.g. "Sunday" or "Saturday,Sunday".</summary>
        public string WeeklyOffDays { get; set; } = "Sunday";

        /// <summary>Minutes after the start time an arrival still counts as on time.</summary>
        public int LateGraceMinutes { get; set; } = 10;

        /// <summary>Deduct late minutes at the basic hourly rate when attendance is turned into payroll transactions.</summary>
        public bool DeductLateTime { get; set; }
        public TimeSpan WorkStartTime { get; set; } = new(8, 0, 0);

        // ---- Cheque layout (millimetres from the cheque's top-left corner) -------
        public decimal ChequeWidthMm { get; set; } = 203m;
        public decimal ChequeHeightMm { get; set; } = 89m;
        public decimal DateXMm { get; set; } = 150m;
        public decimal DateYMm { get; set; } = 12m;
        public decimal PayeeXMm { get; set; } = 22m;
        public decimal PayeeYMm { get; set; } = 27m;
        public decimal AmountWordsXMm { get; set; } = 22m;
        public decimal AmountWordsYMm { get; set; } = 37m;
        public decimal AmountWordsWidthMm { get; set; } = 120m;
        public decimal AmountXMm { get; set; } = 160m;
        public decimal AmountYMm { get; set; } = 41m;
        public decimal ChequeFontSizePt { get; set; } = 11m;

        /// <summary>Nudges every field to match a particular printer, after a test print.</summary>
        public decimal OffsetXMm { get; set; }
        public decimal OffsetYMm { get; set; }
        public bool PrintAccountPayeeOnly { get; set; } = true;
        public string DateFormat { get; set; } = "dd/MM/yyyy";

        /// <summary>Dot matrix: characters per inch and lines per inch of the printer's text mode.</summary>
        public int DotMatrixCpi { get; set; } = 10;
        public int DotMatrixLpi { get; set; } = 6;
    }
}
