using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The employee register: personal and contract details, UAE documents with their
    /// expiry dates, salary and how it is paid, leave balance and end-of-service gratuity.
    /// </summary>
    public class EmployeesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly HrService _hr;

        public EmployeesController(ApplicationDbContext db, HrService hr)
        {
            _db = db;
            _hr = hr;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? status, string? q)
        {
            var query = _db.Employees.AsNoTracking().Include(x => x.Department).Include(x => x.Property).AsQueryable();

            status ??= EmployeeStatuses.Active;
            if (status != "All")
                query = query.Where(x => x.Status == status);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(x => x.EmployeeCode.Contains(term) || x.FirstName.Contains(term) || x.LastName.Contains(term) ||
                                         x.JobTitle.Contains(term) || (x.EmiratesIdNumber != null && x.EmiratesIdNumber.Contains(term)));
            }

            return View(new EmployeeListViewModel
            {
                Employees = await query.OrderBy(x => x.EmployeeCode).ToListAsync(),
                Status = status,
                Search = q,
                ExpiringDocuments = (await _hr.GetExpiringDocumentsAsync(30)).Count
            });
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            return View("Form", await BuildFormAsync(new Employee { JoiningDate = DateTime.Today }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind(Prefix = "Employee")] Employee employee)
        {
            Validate(employee);
            if (!ModelState.IsValid)
                return View("Form", await BuildFormAsync(employee));

            employee.CompanyId = User.GetRequiredCurrentCompanyId();
            employee.EmployeeCode = await _hr.GenerateEmployeeCodeAsync();
            employee.CreatedAt = DateTime.UtcNow;
            Normalise(employee);

            _db.Employees.Add(employee);
            await _db.SaveChangesAsync();

            TempData["HrSuccess"] = $"{employee.EmployeeCode} {employee.FullName} added.";
            return RedirectToAction(nameof(Details), new { id = employee.Id });
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            return employee == null ? NotFound() : View("Form", await BuildFormAsync(employee));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Employee")] Employee posted)
        {
            var employee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id);
            if (employee == null)
                return NotFound();

            Validate(posted);
            if (!ModelState.IsValid)
            {
                posted.Id = id;
                posted.EmployeeCode = employee.EmployeeCode;
                return View("Form", await BuildFormAsync(posted));
            }

            // Everything on the form, never the code, company or creation date.
            var entry = _db.Entry(employee);
            foreach (var property in entry.Properties)
            {
                var name = property.Metadata.Name;
                if (name is nameof(Employee.Id) or nameof(Employee.CompanyId) or nameof(Employee.EmployeeCode) or nameof(Employee.CreatedAt))
                    continue;
                property.CurrentValue = typeof(Employee).GetProperty(name)!.GetValue(posted);
            }

            Normalise(employee);
            await _db.SaveChangesAsync();

            TempData["HrSuccess"] = $"{employee.EmployeeCode} {employee.FullName} updated.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var employee = await _db.Employees.AsNoTracking()
                .Include(x => x.Department).Include(x => x.Property)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (employee == null)
                return NotFound();

            var asOf = employee.LeavingDate.HasValue && employee.LeavingDate.Value.Date < DateTime.Today
                ? employee.LeavingDate.Value.Date
                : DateTime.Today;

            return View(new EmployeeDetailsViewModel
            {
                Employee = employee,
                LeaveBalance = await _hr.GetAnnualLeaveBalanceAsync(employee, asOf),
                Gratuity = HrService.CalculateGratuity(employee.BasicSalary, employee.JoiningDate, asOf),
                GratuityProvisionBooked = await _hr.GetGratuityProvisionBookedAsync(employee),
                Leave = await _db.LeaveRequests.AsNoTracking().Where(x => x.EmployeeId == id)
                    .OrderByDescending(x => x.StartDate).Take(20).ToListAsync(),
                Advances = await _db.EmployeeAdvances.AsNoTracking().Where(x => x.EmployeeId == id)
                    .OrderByDescending(x => x.AdvanceDate).ToListAsync(),
                Payslips = await _db.PayrollRunLines.AsNoTracking().Include(x => x.PayrollRun)
                    .Where(x => x.EmployeeId == id)
                    .OrderByDescending(x => x.PayrollRun!.Year).ThenByDescending(x => x.PayrollRun!.Month)
                    .Take(24).ToListAsync(),
                ExpiringDocuments = (await _hr.GetExpiringDocumentsAsync(60)).Where(x => x.EmployeeId == id).ToList()
            });
        }

        /// <summary>Documents of current employees that have expired or expire soon.</summary>
        [HttpGet]
        public async Task<IActionResult> Documents(int days = 60)
        {
            ViewBag.Days = days;
            return View(await _hr.GetExpiringDocumentsAsync(days));
        }

        private void Validate(Employee employee)
        {
            ModelState.Remove("Employee.EmployeeCode");

            if (employee.BasicSalary <= 0)
                ModelState.AddModelError("Employee.BasicSalary", "Enter the basic salary.");
            if (employee.HousingAllowance < 0 || employee.TransportAllowance < 0 || employee.OtherAllowance < 0)
                ModelState.AddModelError("Employee.HousingAllowance", "Allowances cannot be negative.");

            if (employee.PaymentMethod is SalaryPaymentMethods.Wps or SalaryPaymentMethods.BankTransfer &&
                string.IsNullOrWhiteSpace(employee.Iban))
                ModelState.AddModelError("Employee.Iban", "Salaries paid to a bank need the employee's IBAN.");

            var iban = employee.Iban?.Replace(" ", string.Empty);
            if (!string.IsNullOrEmpty(iban) && (iban.Length != 23 || !iban.StartsWith("AE", StringComparison.OrdinalIgnoreCase)))
                ModelState.AddModelError("Employee.Iban", "A UAE IBAN has 23 characters and starts with AE.");

            if (!string.IsNullOrWhiteSpace(employee.BankRoutingCode) && !IsDigits(employee.BankRoutingCode, 9))
                ModelState.AddModelError("Employee.BankRoutingCode", "The bank routing code has 9 digits.");
            if (!string.IsNullOrWhiteSpace(employee.MohrePersonCode) && !IsDigits(employee.MohrePersonCode, 14))
                ModelState.AddModelError("Employee.MohrePersonCode", "The MOHRE person code has 14 digits.");

            if (employee.ContractType == EmployeeContractTypes.Limited && !employee.ContractEndDate.HasValue)
                ModelState.AddModelError("Employee.ContractEndDate", "A limited contract needs its end date.");

            if (EmployeeStatuses.HasLeft(employee.Status) && !employee.LeavingDate.HasValue)
                ModelState.AddModelError("Employee.LeavingDate", "Enter the last working day.");
            if (employee.LeavingDate.HasValue && employee.LeavingDate.Value.Date < employee.JoiningDate.Date)
                ModelState.AddModelError("Employee.LeavingDate", "The last working day cannot be before the joining date.");

            if (employee.AnnualLeaveDaysPerYear < 0 || employee.AnnualLeaveDaysPerYear > 60)
                ModelState.AddModelError("Employee.AnnualLeaveDaysPerYear", "Annual leave days should be between 0 and 60.");
        }

        private static void Normalise(Employee employee)
        {
            employee.Iban = employee.Iban?.Replace(" ", string.Empty).ToUpperInvariant();
            employee.EmiratesIdNumber = employee.EmiratesIdNumber?.Trim();
            employee.FirstName = employee.FirstName.Trim();
            employee.LastName = employee.LastName.Trim();
        }

        private async Task<EmployeeFormViewModel> BuildFormAsync(Employee employee) => new()
        {
            Employee = employee,
            Departments = await _db.Departments.AsNoTracking().Where(x => x.IsActive || x.Id == employee.DepartmentId)
                .OrderBy(x => x.Name).Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToListAsync(),
            Properties = await _db.Properties.AsNoTracking().OrderBy(x => x.Code)
                .Select(x => new SelectListItem(x.Code + " - " + x.Name, x.Id.ToString())).ToListAsync()
        };

        private static bool IsDigits(string? value, int length) =>
            value != null && value.Length == length && value.All(char.IsDigit);
    }
}
