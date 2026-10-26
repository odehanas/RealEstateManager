using Microsoft.AspNetCore.Mvc.Rendering;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Models
{
    public class EmployeeFormViewModel
    {
        public Employee Employee { get; set; } = new();
        public List<SelectListItem> Departments { get; set; } = new();
        public List<SelectListItem> Properties { get; set; } = new();
    }

    public class EmployeeListViewModel
    {
        public List<Employee> Employees { get; set; } = new();
        public string? Status { get; set; }
        public string? Search { get; set; }
        public int ExpiringDocuments { get; set; }
    }

    public class EmployeeDetailsViewModel
    {
        public Employee Employee { get; set; } = new();
        public HrService.LeaveBalance LeaveBalance { get; set; } = new(0, 0, 0, 0);
        public HrService.GratuityResult Gratuity { get; set; } = HrService.CalculateGratuity(0, DateTime.Today, DateTime.Today);
        public decimal GratuityProvisionBooked { get; set; }
        public List<LeaveRequest> Leave { get; set; } = new();
        public List<EmployeeAdvance> Advances { get; set; } = new();
        public List<PayrollRunLine> Payslips { get; set; } = new();
        public List<HrService.ExpiringDocument> ExpiringDocuments { get; set; } = new();
    }

    public class LeaveListViewModel
    {
        public List<LeaveRequest> Requests { get; set; } = new();
        public string? Status { get; set; }
        public List<SelectListItem> Employees { get; set; } = new();
        public LeaveRequest NewRequest { get; set; } = new();
    }

    public class AdvanceListViewModel
    {
        public List<EmployeeAdvance> Advances { get; set; } = new();
        public EmployeeAdvance NewAdvance { get; set; } = new();
        public List<SelectListItem> Employees { get; set; } = new();
        public List<SelectListItem> BankAccounts { get; set; } = new();
    }

    public class PayrollIndexViewModel
    {
        public List<PayrollRun> Runs { get; set; } = new();
        public int NewYear { get; set; }
        public int NewMonth { get; set; }
        public int ActiveEmployees { get; set; }
    }

    public class PayrollRunViewModel
    {
        public PayrollRun Run { get; set; } = new();
        public PayrollSettings Settings { get; set; } = new();
        public bool CanApprove { get; set; }
        public bool CanManage { get; set; }
        public string? FourEyesBlock { get; set; }
        public List<SelectListItem> BankAccounts { get; set; } = new();
        public Dictionary<int, string> PropertyNames { get; set; } = new();

        /// <summary>Unpaid net salary per payment method.</summary>
        public List<(string Method, int Count, decimal Amount)> Unpaid { get; set; } = new();
    }

    public class PayrollSettingsViewModel
    {
        public PayrollSettings Settings { get; set; } = new();
        public List<SelectListItem> Accounts { get; set; } = new();
        public List<SelectListItem> BankAccounts { get; set; } = new();
    }

    public class ChequePrintViewModel
    {
        public PayrollSettings Settings { get; set; } = new();
        public List<ChequePrintService.ChequeItem> Cheques { get; set; } = new();
        public bool IsTest { get; set; }
        public string Title { get; set; } = "Cheques";

        /// <summary>Print each cheque on an A4 sheet (cheque at the top) instead of cheque-size paper.</summary>
        public bool OnA4 { get; set; }
    }

    public class PayslipsViewModel
    {
        public PayrollRun Run { get; set; } = new();
        public List<PayrollRunLine> Lines { get; set; } = new();
        public Company Company { get; set; } = new();

        /// <summary>Each employee's payroll transactions in this run, with their value.</summary>
        public Dictionary<int, List<(PayrollAdjustment Transaction, decimal Value)>> Transactions { get; set; } = new();
    }

    public class PayrollAdjustmentListViewModel
    {
        public List<PayrollAdjustment> Adjustments { get; set; } = new();

        /// <summary>Each transaction priced as the payroll would price it today.</summary>
        public Dictionary<int, decimal> Values { get; set; } = new();
        public string? Status { get; set; }
        public int? EmployeeId { get; set; }
        public string? Month { get; set; }
        public List<SelectListItem> Employees { get; set; } = new();
        public PayrollAdjustment NewAdjustment { get; set; } = new();
    }

    public class AttendanceDayViewModel
    {
        public DateTime Date { get; set; }
        public List<AttendanceService.DayRow> Rows { get; set; } = new();
        public bool IsWeeklyOff { get; set; }
        public PayrollSettings Settings { get; set; } = new();
    }

    public class AttendanceMonthViewModel
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public List<AttendanceService.MonthRow> Rows { get; set; } = new();
        public PayrollSettings Settings { get; set; } = new();
    }

    public class BankLetterViewModel
    {
        public PayrollPayment Payment { get; set; } = new();
        public PayrollRun Run { get; set; } = new();
        public Company Company { get; set; } = new();
        public PayrollSettings Settings { get; set; } = new();
    }
}
