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
                .Include(x => x.ChargeInvoice)
                .OrderByDescending(x => x.ReceivedDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return View(receipts);
        }

        [HttpGet]
        public async Task<IActionResult> Create(int? leaseId, int? invoiceId, int? replaces)
        {
            var model = new PdcReceiptCreateViewModel();

            // A new cheque replacing a bounced or withdrawn one starts from the old one.
            var old = replaces.HasValue ? await _db.PdcReceipts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == replaces.Value) : null;
            if (old is { Status: PdcStatuses.Bounced or PdcStatuses.Withdrawn })
            {
                model.ReplacesPdcId = old.Id;
                model.ReplacesChequeNumber = old.ChequeNumber;
                model.Amount = old.Amount;
                model.BankName = old.BankName;
                model.SettlementMode = old.SettlementMode;
                leaseId = old.LeaseId;
                invoiceId = old.SettlementMode == "Receivable" ? old.CustomerInvoiceId : null;
            }

            await LoadLookupsAsync(leaseId, invoiceId);

            if (invoiceId.HasValue)
            {
                var invoice = await _db.CustomerInvoices
                    .FirstOrDefaultAsync(x => x.Id == invoiceId.Value);

                if (invoice != null)
                {
                    model.LeaseId = invoice.LeaseId;
                    model.CustomerInvoiceId = invoice.Id;
                    if (model.ReplacesPdcId == null)
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

                if (model.ReplacesPdcId.HasValue)
                {
                    await _pdcService.MarkReplacedAsync(model.ReplacesPdcId.Value, $"Replaced by cheque {result.Pdc.ChequeNumber}");
                    TempData["PdcMessage"] = $"Cheque {result.Pdc.ChequeNumber} is received and replaces cheque {model.ReplacesChequeNumber}.";
                }

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

        // =====================================================================
        //  DEPOSIT, BOUNCE, WITHDRAW
        // =====================================================================

        /// <summary>Deposit, bounce or withdrawal of one cheque.</summary>
        [HttpGet]
        public async Task<IActionResult> Step(int id, string kind)
        {
            var pdc = await LoadPdcReceiptAsync(id);
            if (pdc == null)
                return NotFound();

            var company = await _db.Companies.FirstAsync(x => x.Id == pdc.CompanyId);
            var model = new PdcStepViewModel
            {
                PdcReceiptId = pdc.Id,
                Kind = kind is PdcStepViewModel.Bounce or PdcStepViewModel.Withdraw ? kind : PdcStepViewModel.Deposit,
                StepDate = DateTime.Today
            };
            model.Charge = model.Kind switch
            {
                PdcStepViewModel.Withdraw => company.PdcWithdrawalCharge,
                PdcStepViewModel.Bounce => company.PdcBounceCharge,
                _ => 0m
            };
            PopulateStepModel(model, pdc, company);

            foreach (var error in StepErrors(model.Kind, pdc, model.StepDate).Where(e => !e.Contains("date", StringComparison.OrdinalIgnoreCase)))
                ModelState.AddModelError(string.Empty, error);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step(PdcStepViewModel model)
        {
            var pdc = await LoadPdcReceiptAsync(model.PdcReceiptId);
            if (pdc == null)
                return NotFound();

            var company = await _db.Companies.FirstAsync(x => x.Id == pdc.CompanyId);
            PopulateStepModel(model, pdc, company);

            foreach (var error in StepErrors(model.Kind, pdc, model.StepDate))
                ModelState.AddModelError(string.Empty, error);
            if (model.Charge < 0m)
                ModelState.AddModelError(nameof(model.Charge), "The charge cannot be negative.");

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                string message;
                var warnings = new List<string>();

                if (model.Kind == PdcStepViewModel.Deposit)
                {
                    var journal = await _pdcService.DepositPdcAsync(pdc.Id, model.StepDate.Date);
                    if (await _journalWorkflowService.PostNewEntryAsync(journal.Id) is { } notPosted)
                        warnings.Add($"The deposit was saved as a draft but not posted: {notPosted}");
                    message = $"Cheque {pdc.ChequeNumber} is deposited to the bank and now under collection.";
                }
                else
                {
                    var result = model.Kind == PdcStepViewModel.Bounce
                        ? await _pdcService.BouncePdcAsync(pdc.Id, model.StepDate.Date, model.Reason, model.Charge)
                        : await _pdcService.WithdrawPdcAsync(pdc.Id, model.StepDate.Date, model.Charge, model.Reason);

                    if (await _journalWorkflowService.PostNewEntryAsync(result.Journal.Id) is { } notPosted)
                        warnings.Add($"The entry was saved as a draft but not posted: {notPosted}");
                    if (result.ChargeJournal != null && await _journalWorkflowService.PostNewEntryAsync(result.ChargeJournal.Id) is { } chargeNotPosted)
                        warnings.Add($"Charge invoice {result.ChargeInvoice?.InvoiceNumber} was saved as a draft but not posted: {chargeNotPosted}");

                    message = model.Kind == PdcStepViewModel.Bounce
                        ? $"Cheque {pdc.ChequeNumber} is recorded as bounced" + (pdc.Status == PdcStatuses.Deposited ? " and charged back to the tenant." : ".")
                        : $"Cheque {pdc.ChequeNumber} is withdrawn and handed back to the tenant.";
                    if (result.ChargeInvoice != null)
                        message += $" The tenant is charged {result.ChargeInvoice.TotalAmount:N2} on invoice {result.ChargeInvoice.InvoiceNumber}.";
                }

                TempData["PdcMessage"] = message;
                if (warnings.Count > 0)
                    TempData["PostingWarning"] = string.Join(" ", warnings);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }

        /// <summary>Deposits every ticked cheque on hand to the bank on one date.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DepositSelected(int[] ids, DateTime depositDate)
        {
            if (ids.Length == 0)
            {
                TempData["PostingWarning"] = "Tick the cheques to deposit first.";
                return RedirectToAction(nameof(Index));
            }

            var done = new List<string>();
            var problems = new List<string>();

            foreach (var id in ids.Distinct())
            {
                var pdc = await _db.PdcReceipts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
                if (pdc == null)
                    continue;

                try
                {
                    var journal = await _pdcService.DepositPdcAsync(id, depositDate.Date);
                    if (await _journalWorkflowService.PostNewEntryAsync(journal.Id) is { } notPosted)
                        problems.Add($"{pdc.ChequeNumber}: saved as a draft, not posted - {notPosted}");
                    else
                        done.Add(pdc.ChequeNumber);
                }
                catch (Exception ex)
                {
                    problems.Add($"{pdc.ChequeNumber}: {ex.Message}");
                }
            }

            if (done.Count > 0)
                TempData["PdcMessage"] = $"{done.Count} cheque(s) deposited on {depositDate:dd/MM/yyyy}: {string.Join(", ", done)}.";
            if (problems.Count > 0)
                TempData["PostingWarning"] = string.Join(" ", problems);
            return RedirectToAction(nameof(Index));
        }

        /// <summary>A bounced or withdrawn cheque closed off once the tenant has replaced it or paid.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkReplaced(int id, string? note)
        {
            try
            {
                await _pdcService.MarkReplacedAsync(id, note);
                TempData["PdcMessage"] = "The cheque is marked as replaced.";
            }
            catch (Exception ex)
            {
                TempData["PostingWarning"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        private static List<string> StepErrors(string kind, PdcReceipt pdc, DateTime date) => kind switch
        {
            PdcStepViewModel.Bounce => PdcService.CheckBounce(pdc, date),
            PdcStepViewModel.Withdraw => PdcService.CheckWithdrawal(pdc, date),
            _ => PdcService.CheckDeposit(pdc, date)
        };

        private static void PopulateStepModel(PdcStepViewModel model, PdcReceipt pdc, Company company)
        {
            model.ChequeNumber = pdc.ChequeNumber;
            model.BankName = pdc.BankName;
            model.ChequeDate = pdc.ChequeDate;
            model.ReceivedDate = pdc.ReceivedDate;
            model.DepositedDate = pdc.DepositedDate;
            model.Amount = pdc.Amount;
            model.SettlementMode = pdc.SettlementMode;
            model.Status = pdc.Status;
            model.LeaseDisplay = $"{pdc.Lease?.ContractNumber} - {pdc.Lease?.Tenant?.FullName} - {pdc.Lease?.Unit?.Property?.Code}/{pdc.Lease?.Unit?.UnitNumber}";
            model.InvoiceDisplay = pdc.CustomerInvoice?.InvoiceNumber ?? "-";
            model.VatRate = company.IsVatRegistered ? VatDeterminationService.RateFor(VatCategories.Standard) : 0m;
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
            model.DepositedDate = pdc.Status == PdcStatuses.Deposited ? pdc.DepositedDate : null;
            model.LeaseDisplay = $"{pdc.Lease?.ContractNumber} - {pdc.Lease?.Tenant?.FullName} - {pdc.Lease?.Unit?.Property?.Code}/{pdc.Lease?.Unit?.UnitNumber}";
            model.InvoiceDisplay = pdc.CustomerInvoice?.InvoiceNumber ?? "-";
        }
    }
}
