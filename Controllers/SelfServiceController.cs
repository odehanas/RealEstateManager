using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The employee self-service portal at /me: own pay, leave and documents only. Every
    /// action reads the employee from the sign-in, never from the address, so nobody can
    /// open a colleague's payslip by changing a number.
    /// </summary>
    [Route("me")]
    public class SelfServiceController : Controller
    {
        private readonly EmployeePortalService _portal;
        private readonly ApplicationDbContext _db;

        public SelfServiceController(EmployeePortalService portal, ApplicationDbContext db)
        {
            _portal = portal;
            _db = db;
        }

        private int EmployeeId => User.GetPortalEmployeeId() ?? 0;

        /// <summary>A temporary password HR handed over is only good for choosing a new one.</summary>
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var portalUserId = User.GetEmployeePortalUserId();
            var account = portalUserId.HasValue
                ? await _db.EmployeePortalUsers.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == portalUserId.Value)
                : null;
            if (account == null || !account.IsActive)
            {
                await _portal.SignOutAsync();
                context.Result = RedirectToAction("Login", "SelfServiceAccount");
                return;
            }

            if (account.MustChangePassword)
            {
                context.Result = RedirectToAction("ChangePassword", "SelfServiceAccount");
                return;
            }

            await next();
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var employee = await _portal.GetEmployeeAsync(EmployeeId);
            if (employee == null)
                return RedirectToAction("Login", "SelfServiceAccount");

            ViewBag.Balance = await _portal.GetLeaveBalanceAsync(employee);
            ViewBag.LastPayslip = (await _portal.GetPayslipsAsync(EmployeeId)).FirstOrDefault();
            ViewBag.Advances = await _portal.GetAdvancesAsync(EmployeeId);
            ViewBag.Gratuity = HrService.CalculateGratuity(employee.BasicSalary, employee.JoiningDate, DateTime.Today);
            return View(employee);
        }

        [HttpGet("payslips")]
        public async Task<IActionResult> Payslips() => View(await _portal.GetPayslipsAsync(EmployeeId));

        [HttpGet("payslips/{id:int}")]
        public async Task<IActionResult> Payslip(int id)
        {
            var line = (await _portal.GetPayslipsAsync(EmployeeId)).FirstOrDefault(x => x.Id == id);
            if (line?.PayrollRun == null)
                return NotFound();

            var companyId = line.PayrollRun.CompanyId;
            var transactions = await _db.PayrollAdjustments.AsNoTracking().Include(x => x.Employee)
                .Where(x => x.PayrollRunId == line.PayrollRunId && x.EmployeeId == EmployeeId).OrderBy(x => x.Date).ToListAsync();
            ViewBag.BackUrl = Url.Action(nameof(Payslips));
            var settings = await _db.PayrollSettings.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId) ?? new PayrollSettings();

            return View("~/Views/Payroll/Payslips.cshtml", new PayslipsViewModel
            {
                Run = line.PayrollRun,
                Lines = new List<PayrollRunLine> { line },
                Company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId),
                Transactions = new Dictionary<int, List<(PayrollAdjustment, decimal)>>
                {
                    [EmployeeId] = transactions.Select(t => (t, PayrollService.PriceTransaction(t, t.Employee!, settings))).ToList()
                }
            });
        }

        [HttpGet("leave")]
        public async Task<IActionResult> Leave()
        {
            var employee = await _portal.GetEmployeeAsync(EmployeeId);
            if (employee == null)
                return RedirectToAction("Login", "SelfServiceAccount");
            ViewBag.Balance = await _portal.GetLeaveBalanceAsync(employee);
            return View(await _portal.GetLeaveAsync(EmployeeId));
        }

        [HttpPost("leave")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Leave(string leaveType, DateTime startDate, DateTime endDate, string? reason)
        {
            var (success, message) = await _portal.RequestLeaveAsync(EmployeeId, leaveType, startDate, endDate, reason);
            TempData[success ? "SelfServiceMessage" : "SelfServiceError"] = message;
            return RedirectToAction(nameof(Leave));
        }

        [HttpGet("requests")]
        public async Task<IActionResult> Requests() => View(await _portal.GetRequestsAsync(EmployeeId));

        [HttpPost("requests")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Requests(string requestType, string? addressedTo, string? purpose)
        {
            var (success, message) = await _portal.RequestDocumentAsync(EmployeeId, requestType, addressedTo, purpose);
            TempData[success ? "SelfServiceMessage" : "SelfServiceError"] = message;
            return RedirectToAction(nameof(Requests));
        }

        [HttpGet("requests/{id:int}/document")]
        public async Task<IActionResult> Document(int id)
        {
            var request = await _db.EmployeeRequests.AsNoTracking().Include(x => x.Employee).ThenInclude(x => x!.Department)
                .FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == EmployeeId && x.Status == EmployeeRequestStatuses.Issued);
            if (request == null)
                return NotFound();

            ViewBag.Company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == request.CompanyId);
            return View("~/Views/EmployeeRequests/Certificate.cshtml", request);
        }
    }
}
