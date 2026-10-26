using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>The company's departments, for grouping employees.</summary>
    public class DepartmentsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public DepartmentsController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewBag.Counts = await _db.Employees.AsNoTracking()
                .Where(x => x.DepartmentId != null && x.Status == EmployeeStatuses.Active)
                .GroupBy(x => x.DepartmentId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);
            return View(await _db.Departments.AsNoTracking().OrderBy(x => x.Name).ToListAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string? name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name))
                TempData["HrError"] = "Enter the department name.";
            else if (await _db.Departments.AnyAsync(x => x.Name == name))
                TempData["HrError"] = $"{name} already exists.";
            else
            {
                _db.Departments.Add(new Department { CompanyId = User.GetRequiredCurrentCompanyId(), Name = name, IsActive = true });
                await _db.SaveChangesAsync();
                TempData["HrSuccess"] = $"{name} added.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int id)
        {
            var department = await _db.Departments.FirstOrDefaultAsync(x => x.Id == id);
            if (department != null)
            {
                department.IsActive = !department.IsActive;
                await _db.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
