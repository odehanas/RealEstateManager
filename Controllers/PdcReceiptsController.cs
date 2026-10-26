using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class PdcReceiptsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly PdcService _pdcService;
        private readonly JournalWorkflowService _journalWorkflowService;
        private readonly PdcReceiptEntryService _pdcEntry;

        public PdcReceiptsController(
            ApplicationDbContext db,
            PdcService pdcService,
            JournalWorkflowService journalWorkflowService,
            PdcReceiptEntryService pdcEntry)
        {
            _db = db;
            _pdcService = pdcService;
            _journalWorkflowService = journalWorkflowService;
            _pdcEntry = pdcEntry;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var receipts = await _db.PdcReceipts
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .Include(x => x.CustomerInvoice)
                // Needed so the list can show the date each entry actually posted on,
                // rather than leaving the user to trust that it matches the received date.
                .Include(x => x.MemorandumJournalEntry)
                .Include(x => x.ClearanceJournalEntry)
                .OrderByDescending(x => x.ReceivedDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return View(receipts);
        }

        [HttpGet]
        public async Task<IActionResult> Create(int? leaseId, int? invoiceId)
        {
            await LoadLookupsAsync(leaseId, invoiceId);

            var model = new PdcReceiptCreateViewModel();

            if (invoiceId.HasValue)
            {
                var invoice = await _db.CustomerInvoices
                    .FirstOrDefaultAsync(x => x.Id == invoiceId.Value);

                if (invoice != null)
                {
                    model.LeaseId = invoice.LeaseId;
                    model.CustomerInvoiceId = invoice.Id;
                    model.Amount = invoice.TotalAmount - invoice.AmountPaid;
                    model.SettlementMode = "Receivable";
                }
            }
            else if (leaseId.HasValue)
            {
                model.LeaseId = leaseId.Value;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PdcReceiptCreateViewModel model)
        {
            await LoadLookupsAsync(model.LeaseId, model.CustomerInvoiceId);

            var companyId = User.GetRequiredCurrentCompanyId();
            foreach (var error in await _pdcEntry.ValidateAsync(companyId, model))
            {
                ModelState.AddModelError(error.Field, error.Message);
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var result = await _pdcEntry.CreateAsync(companyId, model);
                if (result.NotPostedReason != null)
                    TempData["PostingWarning"] = $"Cheque {result.Pdc.ChequeNumber} was saved as a draft but not posted: {result.NotPostedReason}";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }

        /// <summary>
        /// Correct a mis-keyed PDC. Changing the received date also re-dates the
        /// memorandum journal, so the register and the ledger stay in step.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var model = await BuildEditModelAsync(id);
            return model == null ? NotFound() : View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(PdcEditViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var reloaded = await BuildEditModelAsync(model.PdcReceiptId, model);
                return reloaded == null ? NotFound() : View(reloaded);
            }

            try
            {
                await _pdcService.UpdateReceiptDetailsAsync(
                    model.PdcReceiptId,
                    model.ReceivedDate,
                    model.ChequeDate,
                    model.ChequeNumber,
                    model.BankName,
                    model.Notes);

                TempData["PdcMessage"] =
                    $"Cheque {model.ChequeNumber.Trim()} updated. The memorandum entry is now dated " +
                    $"{model.ReceivedDate:dd/MM/yyyy}.";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);

                var reloaded = await BuildEditModelAsync(model.PdcReceiptId, model);
                return reloaded == null ? NotFound() : View(reloaded);
            }
        }

        /// <summary>
        /// Loads the read-only context for the edit screen. When <paramref name="posted"/>
        /// is supplied its editable values are kept, so a failed save does not discard
        /// what the user typed.
        /// </summary>
        private async Task<PdcEditViewModel?> BuildEditModelAsync(int id, PdcEditViewModel? posted = null)
        {
            var pdc = await _db.PdcReceipts
                .AsNoTracking()
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .Include(x => x.CustomerInvoice)
                .Include(x => x.MemorandumJournalEntry)
                    .ThenInclude(x => x!.AccountingPeriod)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (pdc == null)
                return null;

            return new PdcEditViewModel
            {
                PdcReceiptId = pdc.Id,
                ChequeNumber = posted?.ChequeNumber ?? pdc.ChequeNumber,
                BankName = posted?.BankName ?? pdc.BankName,
                ChequeDate = posted?.ChequeDate ?? pdc.ChequeDate,
                ReceivedDate = posted?.ReceivedDate ?? pdc.ReceivedDate,
                Notes = posted?.Notes ?? pdc.Notes,

                Amount = pdc.Amount,
                SettlementMode = pdc.SettlementMode,
                Status = pdc.Status,
                ClearedDate = pdc.ClearedDate,
                LeaseDisplay = $"{pdc.Lease?.ContractNumber} - {pdc.Lease?.Tenant?.FullName} - {pdc.Lease?.Unit?.Property?.Code}/{pdc.Lease?.Unit?.UnitNumber}",
                InvoiceDisplay = pdc.CustomerInvoice?.InvoiceNumber ?? "-",

                MemoPostedOn = pdc.MemorandumJournalEntry?.EntryDate,
                MemoEntryNumber = pdc.MemorandumJournalEntry?.EntryNumber,
                MemoPeriodName = pdc.MemorandumJournalEntry?.AccountingPeriod?.Name,
                MemoPeriodClosed = pdc.MemorandumJournalEntry?.AccountingPeriod?.IsClosed ?? false
            };
        }

        [HttpGet]
        public async Task<IActionResult> Clear(int id)
        {
            var pdc = await LoadPdcReceiptAsync(id);
            if (pdc == null)
            {
                return NotFound();
            }

            var model = new PdcClearViewModel
            {
                PdcReceiptId = pdc.Id,
                ClearedDate = DateTime.Today
            };

            PopulateClearModel(model, pdc);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Clear(PdcClearViewModel model)
        {
            var pdc = await LoadPdcReceiptAsync(model.PdcReceiptId);
            if (pdc == null)
            {
                return NotFound();
            }

            PopulateClearModel(model, pdc);

            foreach (var (field, message) in PdcService.CheckClearance(pdc, model.ClearedDate))
            {
                ModelState.AddModelError(field == "ClearedDate" ? nameof(model.ClearedDate) : field, message);
            }

            // Clearing before the cheque date is allowed, but only once the user confirms it.
            var earlyWarning = PdcService.EarlyClearanceWarning(pdc, model.ClearedDate);
            if (ModelState.IsValid && earlyWarning != null && !model.ConfirmEarlyClearance)
            {
                model.RequiresEarlyClearanceConfirmation = true;

                ModelState.AddModelError(string.Empty,
                    earlyWarning + " Check the cleared date, or confirm below to go ahead anyway.");

                return View(model);
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var journal = await _pdcService.ClearPdcAsync(pdc.Id, model.ClearedDate.Date);

                // "Clear and Post" posts, through the same checks as Journal Inquiry.
                var notPosted = await _journalWorkflowService.PostNewEntryAsync(journal.Id);
                if (notPosted != null)
                    TempData["PostingWarning"] = $"Cheque {pdc.ChequeNumber} was cleared as a draft but not posted: {notPosted}";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }

        private async Task LoadLookupsAsync(int? selectedLeaseId = null, int? selectedInvoiceId = null)
        {
            var leases = await _db.Leases
                .Include(x => x.Unit)
                    .ThenInclude(x => x!.Property)
                .Include(x => x.Tenant)
                .Where(x => x.Status == "Active")
                .OrderByDescending(x => x.StartDate)
                .ToListAsync();

            var invoices = await _db.CustomerInvoices
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                .Where(x => x.Status != "Cancelled" && x.TotalAmount > x.AmountPaid)
                .OrderByDescending(x => x.InvoiceDate)
                .ToListAsync();

            ViewBag.Leases = leases;
            ViewBag.Invoices = invoices;
            ViewBag.SelectedLeaseId = selectedLeaseId;
            ViewBag.SelectedInvoiceId = selectedInvoiceId;
        }

        private async Task<PdcReceipt?> LoadPdcReceiptAsync(int id)
        {
            return await _db.PdcReceipts
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .Include(x => x.CustomerInvoice)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        private static void PopulateClearModel(PdcClearViewModel model, PdcReceipt pdc)
        {
            model.PdcReceiptId = pdc.Id;
            model.ChequeNumber = pdc.ChequeNumber;
            model.BankName = pdc.BankName;
            model.ChequeDate = pdc.ChequeDate;
            model.ReceivedDate = pdc.ReceivedDate;
            model.Amount = pdc.Amount;
            model.SettlementMode = pdc.SettlementMode;
            model.LeaseDisplay = $"{pdc.Lease?.ContractNumber} - {pdc.Lease?.Tenant?.FullName} - {pdc.Lease?.Unit?.Property?.Code}/{pdc.Lease?.Unit?.UnitNumber}";
            model.InvoiceDisplay = pdc.CustomerInvoice?.InvoiceNumber ?? "-";
        }
    }
}
