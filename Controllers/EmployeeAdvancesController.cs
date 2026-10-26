using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>Salary advances and staff loans, recovered by the payroll in monthly instalments.</summary>
    public class EmployeeAdvancesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly PayrollService _payroll;

        public EmployeeAdvancesController(ApplicationDbContext db, PayrollService payroll)
        {
            _db = db;
            _payroll = payroll;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var settings = await _payroll.GetSettingsAsync(companyId);
            return View(new AdvanceListViewModel
            {
                Advances = await _db.EmployeeAdvances.AsNoTracking().Include(x => x.Employee)
                    .OrderByDescending(x => x.AdvanceDate).ToListAsync(),
                NewAdvance = new EmployeeAdvance { AdvanceDate = DateTime.Today, PaidFromAccountId = settings.DefaultBankAccountId ?? 0 },
                Employees = await _db.Employees.AsNoTracking().Where(x => x.Status == EmployeeStatuses.Active)
                    .OrderBy(x => x.EmployeeCode)
                    .Select(x => new SelectListItem(x.EmployeeCode + " - " + x.FirstName + " " + x.LastName, x.Id.ToString()))
                    .ToListAsync(),
                BankAccounts = await PayrollController.BankAccountOptionsAsync(_db)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind(Prefix = "NewAdvance")] EmployeeAdvance advance)
        {
            var name = User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value ?? "User";
            var (success, message) = await _payroll.CreateAdvanceAsync(User.GetRequiredCurrentCompanyId(), advance, name);
            TempData[success ? "HrSuccess" : "HrError"] = message;
            return RedirectToAction(nameof(Index));
        }
    }
}
