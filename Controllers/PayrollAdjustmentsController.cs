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
    /// Payroll transactions: additions (overtime, bonus, commission) and deductions
    /// (penalties, late arrival, unauthorised absence, damage recovery) per employee.
    /// Entered here or generated from attendance; approved ones are paid by the next payroll.
    /// </summary>
    public class PayrollAdjustmentsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly PayrollService _payroll;

        public PayrollAdjustmentsController(ApplicationDbContext db, PayrollService payroll)
        {
            _db = db;
            _payroll = payroll;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? status, int? employeeId, string? month)
        {
            status ??= "Open";
            var query = _db.PayrollAdjustments.AsNoTracking().Include(x => x.Employee).Include(x => x.PayrollRun).AsQueryable();

            query = status switch
            {
                "Open" => query.Where(x => x.Status == PayrollAdjustmentStatuses.Pending ||
                                           (x.Status == PayrollAdjustmentStatuses.Approved && (x.PayrollRunId == null || x.PayrollRun!.Status == PayrollRunStatuses.Draft))),
                "All" => query,
                _ => query.Where(x => x.Status == status)
            };
            if (employeeId.HasValue)
                query = query.Where(x => x.EmployeeId == employeeId.Value);
            if (DateTime.TryParse(month + "-01", out var m))
                query = query.Where(x => x.Date >= m && x.Date < m.AddMonths(1));

            var companyId = User.GetRequiredCurrentCompanyId();
            var settings = await _payroll.GetSettingsAsync(companyId);
            var list = await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).Take(500).ToListAsync();

            return View(new PayrollAdjustmentListViewModel
            {
                Adjustments = list,
                Values = list.ToDictionary(x => x.Id, x => x.Employee == null ? 0m : PayrollService.PriceTransaction(x, x.Employee, settings)),
                Status = status,
                EmployeeId = employeeId,
                Month = month,
                Employees = await _db.Employees.AsNoTracking()
                    .Where(x => x.Status == EmployeeStatuses.Active || x.Status == EmployeeStatuses.Suspended)
                    .OrderBy(x => x.EmployeeCode)
                    .Select(x => new SelectListItem(x.EmployeeCode + " - " + x.FirstName + " " + x.LastName, x.Id.ToString()))
                    .ToListAsync(),
                NewAdjustment = new PayrollAdjustment { Date = DateTime.Today, EmployeeId = employeeId ?? 0 }
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind(Prefix = "NewAdjustment")] PayrollAdjustment adjustment, bool approveNow = false)
        {
            var definition = PayrollAdjustmentCategories.All.FirstOrDefault(x => x.Key == adjustment.Category);
            var employee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == adjustment.EmployeeId);

            string? problem = null;
            if (employee == null) problem = "Choose the employee.";
            else if (definition == null) problem = "Choose the kind of transaction.";
            else if (definition.Unit == PayrollAdjustmentCategories.Money && adjustment.Amount <= 0) problem = "Enter the amount.";
            else if (definition.Unit != PayrollAdjustmentCategories.Money && adjustment.Quantity <= 0) problem = $"Enter the number of {definition.Unit}.";
            else if (definition.Unit == PayrollAdjustmentCategories.Days && adjustment.Quantity > 31) problem = "No more than 31 days.";
            else if (adjustment.Date.Date < employee.JoiningDate.Date) problem = $"{employee.FullName} joined on {employee.JoiningDate:dd/MM/yyyy}.";

            if (problem != null)
            {
                TempData["HrError"] = problem;
                return RedirectToAction(nameof(Index), new { employeeId = adjustment.EmployeeId });
            }

            if (definition!.Unit == PayrollAdjustmentCategories.Money) adjustment.Quantity = 0;
            else adjustment.Amount = 0;

            var name = CurrentUserName();
            adjustment.CompanyId = User.GetRequiredCurrentCompanyId();
            adjustment.Source = PayrollAdjustmentSources.Manual;
            adjustment.Status = approveNow ? PayrollAdjustmentStatuses.Approved : PayrollAdjustmentStatuses.Pending;
            adjustment.CreatedByName = name;
            adjustment.CreatedAt = DateTime.UtcNow;
            adjustment.Description = adjustment.Description?.Trim();
            adjustment.PayrollRunId = null;
            if (approveNow)
            {
                adjustment.DecidedByName = name;
                adjustment.DecidedAt = DateTime.UtcNow;
            }

            _db.PayrollAdjustments.Add(adjustment);
            await _db.SaveChangesAsync();
            var refreshed = approveNow ? await _payroll.RefreshDraftRunsAsync(adjustment.CompanyId) : new List<string>();

            TempData["HrSuccess"] = $"{definition.Name} for {employee!.FullName} " +
                                    (approveNow ? "approved" : "saved, waiting for approval") +
                                    (refreshed.Count > 0 ? $"; draft payroll {string.Join(", ", refreshed)} recalculated." : ".");
            return RedirectToAction(nameof(Index), new { employeeId = adjustment.EmployeeId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Approve(int[] ids) => Decide(ids, PayrollAdjustmentStatuses.Approved);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Reject(int[] ids) => Decide(ids, PayrollAdjustmentStatuses.Rejected);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var adjustment = await _db.PayrollAdjustments.Include(x => x.PayrollRun).FirstOrDefaultAsync(x => x.Id == id);
            if (adjustment == null)
                return NotFound();
            if (adjustment.PayrollRun != null && adjustment.PayrollRun.Status != PayrollRunStatuses.Draft)
            {
                TempData["HrError"] = $"Payroll {adjustment.PayrollRun.RunNumber} has paid this transaction; it cannot be removed.";
                return RedirectToAction(nameof(Index));
            }

            _db.PayrollAdjustments.Remove(adjustment);
            await _db.SaveChangesAsync();
            await _payroll.RefreshDraftRunsAsync(adjustment.CompanyId);
            TempData["HrSuccess"] = "Transaction removed.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<IActionResult> Decide(int[] ids, string status)
        {
            var adjustments = await _db.PayrollAdjustments.Include(x => x.PayrollRun)
                .Where(x => ids.Contains(x.Id)).ToListAsync();

            var changed = 0;
            foreach (var a in adjustments)
            {
                // Paid by an approved payroll: no longer open to a decision.
                if (a.PayrollRun != null && a.PayrollRun.Status != PayrollRunStatuses.Draft)
                    continue;
                if (a.Status == status)
                    continue;

                a.Status = status;
                a.DecidedByName = CurrentUserName();
                a.DecidedAt = DateTime.UtcNow;
                if (status != PayrollAdjustmentStatuses.Approved)
                    a.PayrollRunId = null;
                changed++;
            }

            await _db.SaveChangesAsync();
            var refreshed = changed > 0 ? await _payroll.RefreshDraftRunsAsync(User.GetRequiredCurrentCompanyId()) : new List<string>();
            TempData["HrSuccess"] = $"{changed} transaction(s) {status.ToLowerInvariant()}" +
                                    (refreshed.Count > 0 ? $"; draft payroll {string.Join(", ", refreshed)} recalculated." : ".");
            return RedirectToAction(nameof(Index));
        }

        private string CurrentUserName() =>
            User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value ?? "User";
    }
}
