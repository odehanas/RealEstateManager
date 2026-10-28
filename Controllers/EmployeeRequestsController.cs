using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Documents employees ask for through the portal: HR issues them (giving each a
    /// reference number) or declines them, and the issued letter prints from here or
    /// from the employee's own phone.
    /// </summary>
    public class EmployeeRequestsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public EmployeeRequestsController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? status)
        {
            status ??= EmployeeRequestStatuses.Pending;
            var query = _db.EmployeeRequests.AsNoTracking().Include(x => x.Employee).AsQueryable();
            if (status != "All")
                query = query.Where(x => x.Status == status);

            ViewBag.Status = status;
            return View(await query.OrderByDescending(x => x.RequestedAt).Take(300).ToListAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Issue(int id)
        {
            var request = await _db.EmployeeRequests.Include(x => x.Employee).FirstOrDefaultAsync(x => x.Id == id);
            if (request == null || request.Status != EmployeeRequestStatuses.Pending)
                return Back("That request is no longer waiting.");

            var year = DateTime.Today.Year;
            var prefix = $"HR-CERT-{year}-";
            var count = await _db.EmployeeRequests.CountAsync(x => x.ReferenceNumber != null && x.ReferenceNumber.StartsWith(prefix));
            request.ReferenceNumber = $"{prefix}{count + 1:0000}";
            request.Status = EmployeeRequestStatuses.Issued;
            request.DecidedByName = CurrentUserName();
            request.DecidedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await HttpContext.RequestServices.GetRequiredService<Services.Push.PushNotifier>().ToEmployeeAsync(request.EmployeeId,
                "Your document is ready", Array.Empty<object?>(),
                "{0} - you can print it from My HR.", new object?[] { request.ReferenceNumber },
                "/me/requests", $"hr-request-{request.Id}");

            TempData["HrSuccess"] = $"{EmployeeRequestTypes.Describe(request.RequestType)} {request.ReferenceNumber} issued to {request.Employee?.FullName}. They can print it from the portal.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Decline(int id, string? comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return Back("Say why the request is declined - the employee sees it.");

            var request = await _db.EmployeeRequests.Include(x => x.Employee).FirstOrDefaultAsync(x => x.Id == id);
            if (request == null || request.Status != EmployeeRequestStatuses.Pending)
                return Back("That request is no longer waiting.");

            request.Status = EmployeeRequestStatuses.Declined;
            request.Comment = comment.Trim();
            request.DecidedByName = CurrentUserName();
            request.DecidedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await HttpContext.RequestServices.GetRequiredService<Services.Push.PushNotifier>().ToEmployeeAsync(request.EmployeeId,
                "Your document request was declined", Array.Empty<object?>(),
                "{0}", new object?[] { request.Comment },
                "/me/requests", $"hr-request-{request.Id}");

            TempData["HrSuccess"] = $"Request from {request.Employee?.FullName} declined.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Certificate(int id)
        {
            var request = await _db.EmployeeRequests.AsNoTracking().Include(x => x.Employee).ThenInclude(x => x!.Department)
                .FirstOrDefaultAsync(x => x.Id == id && x.Status == EmployeeRequestStatuses.Issued);
            if (request == null)
                return NotFound();

            ViewBag.Company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == request.CompanyId);
            return View(request);
        }

        private IActionResult Back(string message)
        {
            TempData["HrError"] = message;
            return RedirectToAction(nameof(Index));
        }

        private string CurrentUserName() =>
            User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value ?? "User";
    }
}
