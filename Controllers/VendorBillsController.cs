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
    /// Multi-line vendor bills on the Odoo document lifecycle: a bill is entered and
    /// re-entered freely as a DRAFT, POSTED when it is agreed, and RESET TO DRAFT if it
    /// turns out to be wrong. Only a draft may be edited, and the closed-period rules in
    /// JournalWorkflowService govern both directions.
    /// </summary>
    public class VendorBillsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _postingService;
        private readonly JournalWorkflowService _journalWorkflowService;
        private readonly VendorBillService _vendorBillService;

        public VendorBillsController(
            ApplicationDbContext db,
            AccountingPostingService postingService,
            JournalWorkflowService journalWorkflowService,
            VendorBillService vendorBillService)
        {
            _db = db;
            _postingService = postingService;
            _journalWorkflowService = journalWorkflowService;
            _vendorBillService = vendorBillService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var bills = await _db.VendorBills
                .Include(x => x.Vendor)
                .Include(x => x.Property)
                .Include(x => x.JournalEntry)
                    .ThenInclude(x => x!.AccountingPeriod)
                .Include(x => x.Payments)
                .OrderByDescending(x => x.BillDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return View(bills);
        }

        /// <summary>The bill as a document: header, lines, tax summary, printable.</summary>
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var bill = await _db.VendorBills
                .AsNoTracking()
                .Include(x => x.Vendor)
                .Include(x => x.Property)
                .Include(x => x.DevelopmentProject)
                .Include(x => x.JournalEntry)
                    .ThenInclude(x => x!.AccountingPeriod)
                .Include(x => x.Lines)
                    .ThenInclude(x => x.Account)
                .Include(x => x.Lines)
                    .ThenInclude(x => x.Property)
                .Include(x => x.Payments)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (bill == null)
                return NotFound();

            ViewBag.Company = await _db.Companies.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == bill.CompanyId);

            // Payments and advance applications that settled the bill, by clearing document.
            ViewBag.Clearings = await _db.VendorClearingLines.AsNoTracking()
                .Where(x => x.VendorBillId == id)
                .Include(x => x.VendorClearing).ThenInclude(x => x!.VendorPayment)
                .Include(x => x.VendorClearing).ThenInclude(x => x!.AdvancePayment)
                .OrderBy(x => x.VendorClearing!.ClearingDate).ThenBy(x => x.Id)
                .ToListAsync();

            return View(bill);
        }

        // =====================================================================
        //  CREATE / EDIT
        // =====================================================================

        [HttpGet]
        public async Task<IActionResult> Create(int? receivedId)
        {
            var model = _vendorBillService.BuildNew();

            if (receivedId.HasValue)
            {
                var fromEInvoice = await _vendorBillService.BuildFromReceivedAsync(receivedId.Value);
                if (fromEInvoice == null)
                {
                    TempData["EInvoiceError"] =
                        "That e-invoice is already booked, is a credit note, or was not found.";
                    return RedirectToAction("Received", "EInvoicing");
                }

                model = fromEInvoice;
            }

            await LoadListsAsync();
            return View("Edit", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(VendorBillEditViewModel model)
        {
            model.Id = 0;
            return await SaveAsync(model);
        }

        /// <summary>
        /// Reads a scanned or photographed supplier invoice and opens the new-bill form
        /// filled in from it. Nothing is saved until the user checks it and saves.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(12 * 1024 * 1024)]
        public async Task<IActionResult> Scan(IFormFile? file, [FromServices] RealEstateManager.Services.Assistant.InvoiceScanService scanner)
        {
            try
            {
                var result = await scanner.ScanAsync(file, User.GetRequiredCurrentCompanyId());
                ViewBag.ScanWarnings = result.Warnings;
                await LoadListsAsync();
                return View("Edit", result.Bill);
            }
            catch (RealEstateManager.Services.Assistant.InvoiceScanService.ScanException ex)
            {
                TempData["BillError"] = ex.Message;
                return RedirectToAction(nameof(Create));
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var model = await _vendorBillService.BuildEditAsync(id);
            if (model == null)
                return NotFound();

            if (!model.IsEditable)
            {
                TempData["BillError"] =
                    "This bill is posted and cannot be edited. Reset it to draft first.";
                return RedirectToAction(nameof(Details), new { id });
            }

            await LoadListsAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(VendorBillEditViewModel model)
        {
            if (model.Id == 0)
                return NotFound();

            return await SaveAsync(model);
        }

        private async Task<IActionResult> SaveAsync(VendorBillEditViewModel model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();

            var bill = await _vendorBillService.SaveAsync(ModelState, companyId, model);

            if (bill == null)
            {
                await LoadListsAsync();
                await RefreshVendorContextAsync(model);

                // The grid clones its last row to add a new one, so a form redisplayed
                // with no rows at all would be impossible to fill in.
                if (model.Lines.Count == 0)
                    model.Lines.Add(new VendorBillLineViewModel { Quantity = 1m });

                return View("Edit", model);
            }

            TempData["BillSuccess"] =
                $"Bill {bill.BillNumber} saved as a draft. Review it, then post it to book it to the accounts.";

            return RedirectToAction(nameof(Details), new { id = bill.Id });
        }

        // =====================================================================
        //  WORKFLOW
        // =====================================================================

        /// <summary>
        /// Draft to Posted. The journal is built from the current lines - rebuilt if the
        /// bill was reset and amended - and then posted through the workflow service, so
        /// the closed-period rules apply here exactly as they do anywhere else.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Post(int id, string? returnUrl)
        {
            try
            {
                var journal = await _postingService.PostVendorBillAsync(id);
                var result = await _journalWorkflowService.PostAsync(journal.Id);

                TempData[result.Success ? "BillSuccess" : "BillError"] = result.Message;
            }
            catch (Exception ex)
            {
                TempData["BillError"] = ex.Message;
            }

            return RedirectBack(returnUrl, id);
        }

        /// <summary>
        /// Posted back to Draft. Refused by the workflow service if the period is closed
        /// or a payment has already been made against the bill.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetToDraft(int id, string? returnUrl)
        {
            var bill = await _db.VendorBills.FirstOrDefaultAsync(x => x.Id == id);

            if (bill?.JournalEntryId == null)
            {
                TempData["BillError"] = "This bill has no journal entry to reset.";
                return RedirectBack(returnUrl, id);
            }

            var result = await _journalWorkflowService.ResetToDraftAsync(bill.JournalEntryId.Value);
            TempData[result.Success ? "BillSuccess" : "BillError"] = result.Message;

            return RedirectBack(returnUrl, id);
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        private IActionResult RedirectBack(string? returnUrl, int id)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction(nameof(Details), new { id });
        }

        /// <summary>
        /// Re-reads the supplier so a redisplayed form still shows the right tax
        /// position - whether VAT may be charged at all is the first thing the user
        /// needs to see, and it must not go stale because validation failed.
        /// </summary>
        private async Task RefreshVendorContextAsync(VendorBillEditViewModel model)
        {
            var vendor = await _db.Vendors.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == model.VendorId);

            model.VendorCanChargeVat = vendor?.CanChargeVat ?? false;
            model.VendorTrn = vendor?.TrnNumber;
        }

        private async Task LoadListsAsync()
        {
            ViewBag.Vendors = await _db.Vendors
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Code)
                .Select(x => new VendorOptionViewModel
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    TrnNumber = x.TrnNumber,
                    IsVatRegistered = x.IsVatRegistered
                })
                .ToListAsync();

            ViewBag.Properties = await _db.Properties
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Code)
                .ToListAsync();

            // Only real, postable expense and asset accounts - a bill line cannot be
            // charged to a heading, and never to a revenue or payable account.
            ViewBag.Accounts = await _db.Accounts
                .AsNoTracking()
                .Where(x => x.IsActive
                            && x.IsPostable
                            && !x.IsGroup
                            && (x.AccountType == "Expense" || x.AccountType == "Asset"))
                .OrderBy(x => x.Code)
                .Select(x => new SelectListItem
                {
                    Value = x.Id.ToString(),
                    Text = x.Code + " - " + x.Name
                })
                .ToListAsync();

            ViewBag.DevelopmentProjects = await DevelopmentProjectService.DevelopmentCostOptions(_db);

            ViewBag.Attributions = InputTaxAttributions.All
                .Select(x => new SelectListItem
                {
                    Value = x,
                    Text = InputTaxAttributions.Describe(x)
                })
                .ToList();
        }
    }
}
