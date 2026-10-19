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

        public PdcReceiptsController(
            ApplicationDbContext db,
            PdcService pdcService,
            JournalWorkflowService journalWorkflowService)
        {
            _db = db;
            _pdcService = pdcService;
            _journalWorkflowService = journalWorkflowService;
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
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
            {
                ModelState.AddModelError(string.Empty, "No company record found.");
            }

            var lease = await _db.Leases
                .Include(x => x.Unit)
                    .ThenInclude(x => x!.Property)
                .Include(x => x.Tenant)
                .FirstOrDefaultAsync(x => x.Id == model.LeaseId);

            if (lease == null)
            {
                ModelState.AddModelError(nameof(model.LeaseId), "Selected lease was not found.");
            }

            CustomerInvoice? invoice = null;

            if (model.CustomerInvoiceId.HasValue)
            {
                invoice = await _db.CustomerInvoices
                    .Include(x => x.Lease)
                    .FirstOrDefaultAsync(x => x.Id == model.CustomerInvoiceId.Value);

                if (invoice == null)
                {
                    ModelState.AddModelError(nameof(model.CustomerInvoiceId), "Selected invoice was not found.");
                }
            }

            if (string.IsNullOrWhiteSpace(model.ChequeNumber))
            {
                ModelState.AddModelError(nameof(model.ChequeNumber), "Cheque number is required.");
            }

            if (model.Amount <= 0m)
            {
                ModelState.AddModelError(nameof(model.Amount), "PDC amount must be greater than zero.");
            }

            // The received date is the ACCOUNTING date - the memorandum journal is posted
            // on it. A future received date would post the entry into a future period,
            // where it is invisible in today's Trial Balance and Balance Sheet. The
            // classic mistake is typing the cheque's future date into this field, so it
            // is rejected rather than silently accepted.
            if (model.ReceivedDate.Date > DateTime.Today)
            {
                ModelState.AddModelError(nameof(model.ReceivedDate),
                    "The received date cannot be in the future - it is the date the accounting entry is posted. " +
                    "Enter the date the cheque was physically handed over. The future date written on the cheque goes in Cheque Date.");
            }

            if (model.ChequeDate.Date < model.ReceivedDate.Date)
            {
                ModelState.AddModelError(nameof(model.ChequeDate),
                    "The cheque date is earlier than the received date. Check the two dates - a post-dated cheque is dated on or after the day you receive it.");
            }

            if (model.SettlementMode != "Advance" && model.SettlementMode != "Receivable")
            {
                ModelState.AddModelError(nameof(model.SettlementMode), "Settlement mode must be Advance or Receivable.");
            }

            if (model.SettlementMode == "Receivable" && !model.CustomerInvoiceId.HasValue)
            {
                ModelState.AddModelError(nameof(model.CustomerInvoiceId), "Invoice is required when settlement mode is Receivable.");
            }

            if (invoice != null && invoice.LeaseId != model.LeaseId)
            {
                ModelState.AddModelError(nameof(model.CustomerInvoiceId), "Selected invoice does not belong to the selected lease.");
            }

            if (invoice != null && model.SettlementMode == "Receivable")
            {
                var outstanding = invoice.TotalAmount - invoice.AmountPaid;
                if (model.Amount > outstanding)
                {
                    ModelState.AddModelError(nameof(model.Amount), "PDC amount cannot exceed the outstanding invoice balance.");
                }
            }

            if (!string.IsNullOrWhiteSpace(model.ChequeNumber) && company != null)
            {
                var chequeExists = await _db.PdcReceipts.AnyAsync(x =>
                    x.CompanyId == companyId &&
                    x.ChequeNumber == model.ChequeNumber.Trim());

                if (chequeExists)
                {
                    ModelState.AddModelError(nameof(model.ChequeNumber), "Cheque number already exists.");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();

                var pdc = new PdcReceipt
                {
                    CompanyId = companyId,
                    LeaseId = model.LeaseId,
                    TenantId = lease!.TenantId,
                    CustomerInvoiceId = model.SettlementMode == "Receivable"
                        ? model.CustomerInvoiceId
                        : null,
                    ChequeNumber = model.ChequeNumber.Trim(),
                    BankName = string.IsNullOrWhiteSpace(model.BankName)
                        ? null
                        : model.BankName.Trim(),
                    ChequeDate = model.ChequeDate.Date,
                    ReceivedDate = model.ReceivedDate.Date,
                    Amount = model.Amount,
                    SettlementMode = model.SettlementMode,
                    Status = "Draft",
                    Notes = string.IsNullOrWhiteSpace(model.Notes)
                        ? null
                        : model.Notes.Trim()
                };

                _db.PdcReceipts.Add(pdc);
                await _db.SaveChangesAsync();

                var journal = await _pdcService.ReceivePdcAsync(pdc.Id);

                await transaction.CommitAsync();

                // The cheque is recorded as received at once, through the same checks as
                // Journal Inquiry; if one refuses, the memorandum entry stays a draft.
                var notPosted = await _journalWorkflowService.PostNewEntryAsync(journal.Id);
                if (notPosted != null)
                    TempData["PostingWarning"] = $"Cheque {pdc.ChequeNumber} was saved as a draft but not posted: {notPosted}";

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

            if (pdc.Status == "Cleared")
            {
                ModelState.AddModelError(string.Empty, "This PDC is already cleared.");
            }

            if (model.ClearedDate.Date < pdc.ReceivedDate.Date)
            {
                ModelState.AddModelError(nameof(model.ClearedDate), "Cleared date cannot be earlier than received date.");
            }

            // The cleared date is the accounting date of the cash movement, so it cannot
            // be in the future either.
            if (model.ClearedDate.Date > DateTime.Today)
            {
                ModelState.AddModelError(nameof(model.ClearedDate),
                    "The cleared date cannot be in the future - it is the date the cash entry is posted. " +
                    "Clear the cheque on the day the bank actually honours it.");
            }

            // Clearing before the date written on the cheque is usually a mistake - a bank
            // will not honour a post-dated cheque early. It is not forbidden, because the
            // parties can agree to present it early, so this asks for confirmation rather
            // than refusing outright.
            if (ModelState.IsValid
                && model.ClearedDate.Date < pdc.ChequeDate.Date
                && !model.ConfirmEarlyClearance)
            {
                var daysEarly = (int)(pdc.ChequeDate.Date - model.ClearedDate.Date).TotalDays;

                model.RequiresEarlyClearanceConfirmation = true;

                ModelState.AddModelError(string.Empty,
                    $"This would clear the cheque {daysEarly} day(s) before its cheque date of " +
                    $"{pdc.ChequeDate:dd/MM/yyyy}. A bank will not normally honour a post-dated cheque early. " +
                    "Check the cleared date, or confirm below to go ahead anyway.");

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
