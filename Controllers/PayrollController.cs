using System.Text;
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
    /// Monthly payroll: prepare, adjust, approve, pay (WPS file, bank transfer, cheques),
    /// payslips and the payroll settings. Approving needs Payroll.Approve.
    /// </summary>
    public class PayrollController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly PayrollService _payroll;
        private readonly PermissionService _permissions;

        public PayrollController(ApplicationDbContext db, PayrollService payroll, PermissionService permissions)
        {
            _db = db;
            _payroll = payroll;
            _permissions = permissions;
        }

        // =====================================================================
        //  RUNS
        // =====================================================================

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var last = await _db.PayrollRuns.AsNoTracking().OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).FirstOrDefaultAsync();
            var next = last == null ? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1) : new DateTime(last.Year, last.Month, 1).AddMonths(1);

            return View(new PayrollIndexViewModel
            {
                Runs = await _db.PayrollRuns.AsNoTracking().Include(x => x.Lines)
                    .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync(),
                NewYear = next.Year,
                NewMonth = next.Month,
                ActiveEmployees = await _db.Employees.CountAsync(x => x.Status == EmployeeStatuses.Active)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int year, int month)
        {
            var (success, message, runId) = await _payroll.CreateRunAsync(
                User.GetRequiredCurrentCompanyId(), year, month, User.GetAppUserId() ?? 0, CurrentUserName());
            Flash(success, message);
            return runId.HasValue ? RedirectToAction(nameof(Run), new { id = runId }) : RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Run(int id)
        {
            var run = await _db.PayrollRuns.AsNoTracking()
                .Include(x => x.Lines)
                .Include(x => x.Payments).ThenInclude(x => x.BankAccount)
                .Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (run == null)
                return NotFound();

            var companyId = User.GetRequiredCurrentCompanyId();
            var canApprove = await _permissions.HasPermissionAsync(User, AppPermissions.PayrollApprove);
            string? fourEyes = null;
            if (canApprove && run.Status == PayrollRunStatuses.Draft && run.PreparedByAppUserId == User.GetAppUserId() &&
                !User.IsInRole("SystemAdmin") && await _payroll.CountApproversAsync(companyId) > 1)
                fourEyes = "You prepared this payroll, so another approver has to approve it.";

            var propertyIds = run.Lines.Where(x => x.PropertyId.HasValue).Select(x => x.PropertyId!.Value).Distinct().ToList();

            return View(new PayrollRunViewModel
            {
                Run = run,
                Settings = await _payroll.GetSettingsAsync(companyId),
                CanApprove = canApprove,
                CanManage = await _permissions.HasPermissionAsync(User, AppPermissions.PayrollManage),
                FourEyesBlock = fourEyes,
                BankAccounts = await BankAccountOptionsAsync(_db),
                PropertyNames = await _db.Properties.AsNoTracking().Where(x => propertyIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name),
                Unpaid = run.Lines.Where(x => !x.IsPaid && x.Net > 0)
                    .GroupBy(x => x.PaymentMethod)
                    .Select(g => (g.Key, g.Count(), g.Sum(x => x.Net)))
                    .ToList()
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateLine(int id, int runId, decimal advanceRecovery)
        {
            var (success, message) = await _payroll.UpdateAdvanceRecoveryAsync(id, advanceRecovery);
            Flash(success, message);
            return RedirectToAction(nameof(Run), new { id = runId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Recalculate(int id)
        {
            var (success, message) = await _payroll.RecalculateAsync(id);
            Flash(success, message);
            return RedirectToAction(nameof(Run), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var (success, message) = await _payroll.DeleteRunAsync(id);
            Flash(success, message);
            return success ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Run), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id)
        {
            var (success, message) = await _payroll.ApproveAsync(id, User.GetAppUserId() ?? 0, CurrentUserName(), User.IsInRole("SystemAdmin"));
            Flash(success, message);
            return RedirectToAction(nameof(Run), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReturnToDraft(int id)
        {
            var (success, message) = await _payroll.ReturnToDraftAsync(id);
            Flash(success, message);
            return RedirectToAction(nameof(Run), new { id });
        }

        // =====================================================================
        //  PAYMENTS
        // =====================================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Pay(int id, string method, DateTime paymentDate, int bankAccountId, string? reference, string? firstChequeNumber)
        {
            var (success, message, _) = await _payroll.PayAsync(id,
                new PayrollService.PaymentRequest(method, paymentDate, bankAccountId, reference, firstChequeNumber, null), CurrentUserName());
            Flash(success, message);
            return RedirectToAction(nameof(Run), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelPayment(int paymentId, int runId)
        {
            var (success, message) = await _payroll.CancelPaymentAsync(paymentId);
            Flash(success, message);
            return RedirectToAction(nameof(Run), new { id = runId });
        }

        /// <summary>The WPS salary file or the bank transfer list, as it was produced.</summary>
        [HttpGet]
        public async Task<IActionResult> Download(int id)
        {
            var payment = await _db.PayrollPayments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (payment?.FileContent == null)
                return NotFound();

            var type = payment.Method == SalaryPaymentMethods.Wps ? "text/plain" : "text/csv";
            return File(Encoding.ASCII.GetBytes(payment.FileContent), type, payment.FileName ?? "payroll.txt");
        }

        /// <summary>A covering letter to the bank listing the transfers to make.</summary>
        [HttpGet]
        public async Task<IActionResult> BankLetter(int id)
        {
            var payment = await _db.PayrollPayments.AsNoTracking().Include(x => x.Lines).Include(x => x.BankAccount)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (payment == null)
                return NotFound();

            var companyId = User.GetRequiredCurrentCompanyId();
            return View(new BankLetterViewModel
            {
                Payment = payment,
                Run = await _db.PayrollRuns.AsNoTracking().FirstAsync(x => x.Id == payment.PayrollRunId),
                Company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId),
                Settings = await _payroll.GetSettingsAsync(companyId)
            });
        }

        /// <summary>Cheques for a laser or inkjet printer, one per page, sized to the cheque (or on A4).</summary>
        [HttpGet]
        public async Task<IActionResult> Cheques(int id, bool a4 = false)
        {
            var (settings, cheques, payment) = await LoadChequesAsync(id);
            if (payment == null)
                return NotFound();

            return View(new ChequePrintViewModel { Settings = settings, Cheques = cheques, OnA4 = a4, Title = $"Cheques {payment.Reference}" });
        }

        /// <summary>The same cheques as a text file for a dot matrix printer's Generic / Text Only driver.</summary>
        [HttpGet]
        public async Task<IActionResult> ChequesDotMatrix(int id)
        {
            var (settings, cheques, payment) = await LoadChequesAsync(id);
            if (payment == null)
                return NotFound();

            var text = ChequePrintService.BuildDotMatrixText(settings, cheques);
            return File(Encoding.ASCII.GetBytes(text), "text/plain", $"cheques-{payment.Id}.txt");
        }

        /// <summary>A sample cheque to line the layout up with a printer before real cheques are printed.</summary>
        [HttpGet]
        public async Task<IActionResult> TestCheque(bool dotMatrix = false, bool a4 = false)
        {
            var settings = await _payroll.GetSettingsAsync(User.GetRequiredCurrentCompanyId());
            var sample = new List<ChequePrintService.ChequeItem> { new("000000", "Sample Payee Name", DateTime.Today, 12345.50m) };

            if (dotMatrix)
                return File(Encoding.ASCII.GetBytes(ChequePrintService.BuildDotMatrixText(settings, sample)), "text/plain", "cheque-test.txt");

            return View("Cheques", new ChequePrintViewModel { Settings = settings, Cheques = sample, IsTest = true, OnA4 = a4, Title = "Cheque alignment test" });
        }

        private async Task<(PayrollSettings Settings, List<ChequePrintService.ChequeItem> Cheques, PayrollPayment? Payment)> LoadChequesAsync(int paymentId)
        {
            var settings = await _payroll.GetSettingsAsync(User.GetRequiredCurrentCompanyId());
            var payment = await _db.PayrollPayments.AsNoTracking().Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == paymentId && x.Method == SalaryPaymentMethods.Cheque);
            if (payment == null)
                return (settings, new(), null);

            var cheques = payment.Lines.OrderBy(x => x.ChequeNumber)
                .Select(x => new ChequePrintService.ChequeItem(x.ChequeNumber ?? "", x.EmployeeName, payment.PaymentDate, x.Net))
                .ToList();
            return (settings, cheques, payment);
        }

        // =====================================================================
        //  PAYSLIPS
        // =====================================================================

        [HttpGet]
        public async Task<IActionResult> Payslip(int id)
        {
            var line = await _db.PayrollRunLines.AsNoTracking().Include(x => x.PayrollRun).FirstOrDefaultAsync(x => x.Id == id);
            if (line?.PayrollRun == null)
                return NotFound();
            return await PayslipsView(line.PayrollRun, new List<PayrollRunLine> { line });
        }

        [HttpGet]
        public async Task<IActionResult> Payslips(int id)
        {
            var run = await _db.PayrollRuns.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
            return run == null ? NotFound() : await PayslipsView(run, run.Lines.OrderBy(x => x.EmployeeCode).ToList());
        }

        private async Task<IActionResult> PayslipsView(PayrollRun run, List<PayrollRunLine> lines)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var settings = await _payroll.GetSettingsAsync(companyId);
            var employeeIds = lines.Select(x => x.EmployeeId).ToList();
            var transactions = await _db.PayrollAdjustments.AsNoTracking().Include(x => x.Employee)
                .Where(x => x.PayrollRunId == run.Id && employeeIds.Contains(x.EmployeeId))
                .OrderBy(x => x.Date)
                .ToListAsync();

            return View("Payslips", new PayslipsViewModel
            {
                Run = run,
                Lines = lines,
                Company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId),
                Transactions = transactions.GroupBy(x => x.EmployeeId).ToDictionary(
                    g => g.Key,
                    g => g.Select(t => (t, PayrollService.PriceTransaction(t, t.Employee!, settings))).ToList())
            });
        }

        // =====================================================================
        //  SETTINGS
        // =====================================================================

        [HttpGet]
        public async Task<IActionResult> Settings()
        {
            var settings = await _payroll.GetSettingsAsync(User.GetRequiredCurrentCompanyId());
            return View(await BuildSettingsAsync(settings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Settings([Bind(Prefix = "Settings")] PayrollSettings posted)
        {
            var settings = await _payroll.GetSettingsAsync(User.GetRequiredCurrentCompanyId());

            if (!string.IsNullOrWhiteSpace(posted.EmployerEstablishmentId) && !IsDigits(posted.EmployerEstablishmentId, 13))
                ModelState.AddModelError("Settings.EmployerEstablishmentId", "The MOHRE establishment number has 13 digits.");
            if (!string.IsNullOrWhiteSpace(posted.EmployerBankRoutingCode) && !IsDigits(posted.EmployerBankRoutingCode, 9))
                ModelState.AddModelError("Settings.EmployerBankRoutingCode", "The bank routing code has 9 digits.");
            if (posted.OvertimeMultiplier < 1m || posted.OvertimeMultiplier > 3m)
                ModelState.AddModelError("Settings.OvertimeMultiplier", "Overtime is normally paid at 1.25 (125%) or 1.5 (150%).");
            if (posted.ChequeWidthMm < 100 || posted.ChequeHeightMm < 50)
                ModelState.AddModelError("Settings.ChequeWidthMm", "Enter the cheque size in millimetres.");

            if (!ModelState.IsValid)
            {
                posted.Id = settings.Id;
                return View(await BuildSettingsAsync(posted));
            }

            var entry = _db.Entry(settings);
            foreach (var property in entry.Properties)
            {
                var name = property.Metadata.Name;
                if (name is nameof(PayrollSettings.Id) or nameof(PayrollSettings.CompanyId))
                    continue;
                property.CurrentValue = typeof(PayrollSettings).GetProperty(name)!.GetValue(posted);
            }

            await _db.SaveChangesAsync();
            TempData["HrSuccess"] = "Payroll settings saved.";
            return RedirectToAction(nameof(Settings));
        }

        private async Task<PayrollSettingsViewModel> BuildSettingsAsync(PayrollSettings settings) => new()
        {
            Settings = settings,
            Accounts = await _db.Accounts.AsNoTracking().Where(x => x.IsPostable && x.IsActive).OrderBy(x => x.Code)
                .Select(x => new SelectListItem(x.Code + " - " + x.Name, x.Id.ToString())).ToListAsync(),
            BankAccounts = await BankAccountOptionsAsync(_db)
        };

        /// <summary>Postable asset accounts salaries and advances can be paid from (bank and cash).</summary>
        public static Task<List<SelectListItem>> BankAccountOptionsAsync(ApplicationDbContext db) =>
            db.Accounts.AsNoTracking()
                .Where(x => x.IsPostable && x.IsActive && x.AccountType == "Asset" && x.Code.StartsWith("111"))
                .OrderBy(x => x.Code)
                .Select(x => new SelectListItem(x.Code + " - " + x.Name, x.Id.ToString()))
                .ToListAsync();

        private static bool IsDigits(string? value, int length) =>
            value != null && value.Length == length && value.All(char.IsDigit);

        private void Flash(bool success, string message) => TempData[success ? "HrSuccess" : "HrError"] = message;

        private string CurrentUserName() =>
            User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value ?? "User";
    }
}
