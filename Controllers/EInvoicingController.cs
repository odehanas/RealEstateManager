using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// UAE e-invoicing: the company's settings, the queue of documents to send with their
    /// 14-day deadlines, the exchange of files with the Accredited Service Provider, and
    /// the inbox of e-invoices received from suppliers.
    /// </summary>
    public class EInvoicingController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly EInvoiceService _eInvoiceService;

        public EInvoicingController(ApplicationDbContext db, EInvoiceService eInvoiceService)
        {
            _db = db;
            _eInvoiceService = eInvoiceService;
        }

        // =====================================================================
        //  MONITOR
        // =====================================================================

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId);
            var today = DateTime.Today;

            var model = new EInvoiceMonitorViewModel
            {
                Today = today,
                Enabled = company.EInvoicingEnabled,
                RevenueBand = company.EInvoicingRevenueBand,
                StartDate = EInvoiceService.StartDate(company),
                AppointmentDeadline = EInvoicingRevenueBands.AppointmentDeadline(company.EInvoicingRevenueBand),
                ProviderName = company.EInvoicingProviderName,
                CompanySetupProblems = EInvoiceService.CheckCompany(company).Count
            };

            var inScopeTenants = await _db.Tenants
                .AsNoTracking()
                .Where(x => x.IsActive && (x.PartyType == PartyTypes.Business || x.PartyType == PartyTypes.Government))
                .ToListAsync();
            model.TenantsNeedingAttention = inScopeTenants.Count(x => EInvoiceService.CheckTenant(x).Count > 0);

            var tracked = new[]
            {
                EInvoiceStatuses.Pending, EInvoiceStatuses.Generated, EInvoiceStatuses.Rejected,
                EInvoiceStatuses.Submitted, EInvoiceStatuses.Accepted
            };

            var invoices = await _db.CustomerInvoices
                .AsNoTracking()
                .Where(x => tracked.Contains(x.EInvoiceStatus))
                .Select(x => new
                {
                    x.Id,
                    Number = x.TaxInvoiceNumber ?? x.InvoiceNumber,
                    x.InvoiceDate,
                    SupplyDate = x.DateOfSupply ?? x.InvoiceDate,
                    Tenant = x.Lease!.Tenant!.FullName,
                    x.TotalAmount,
                    x.EInvoiceStatus,
                    x.EInvoiceProviderReference,
                    x.EInvoiceRejectionReason,
                    x.EInvoiceSubmittedAt,
                    x.EInvoiceResponseAt
                })
                .ToListAsync();

            var notes = await _db.CustomerCreditNotes
                .AsNoTracking()
                .Where(x => tracked.Contains(x.EInvoiceStatus))
                .Select(x => new
                {
                    x.Id,
                    Number = x.CreditNoteNumber,
                    x.CreditNoteDate,
                    Tenant = x.CustomerInvoice!.Lease!.Tenant!.FullName,
                    x.TotalAmount,
                    x.EInvoiceStatus,
                    x.EInvoiceProviderReference,
                    x.EInvoiceRejectionReason,
                    x.EInvoiceSubmittedAt,
                    x.EInvoiceResponseAt
                })
                .ToListAsync();

            var rows = invoices.Select(x => new EInvoiceRowViewModel
                {
                    Kind = EInvoiceService.KindInvoice,
                    Id = x.Id,
                    Number = x.Number,
                    IssueDate = x.InvoiceDate,
                    SupplyDate = x.SupplyDate,
                    Deadline = EInvoiceService.DeadlineFor(x.SupplyDate),
                    TenantName = x.Tenant,
                    Total = x.TotalAmount,
                    Status = x.EInvoiceStatus,
                    ProviderReference = x.EInvoiceProviderReference,
                    RejectionReason = x.EInvoiceRejectionReason,
                    SubmittedAt = x.EInvoiceSubmittedAt,
                    ResponseAt = x.EInvoiceResponseAt
                })
                .Concat(notes.Select(x => new EInvoiceRowViewModel
                {
                    Kind = EInvoiceService.KindCreditNote,
                    Id = x.Id,
                    Number = x.Number,
                    IssueDate = x.CreditNoteDate,
                    SupplyDate = x.CreditNoteDate,
                    Deadline = EInvoiceService.DeadlineFor(x.CreditNoteDate),
                    TenantName = x.Tenant,
                    Total = x.TotalAmount,
                    Status = x.EInvoiceStatus,
                    ProviderReference = x.EInvoiceProviderReference,
                    RejectionReason = x.EInvoiceRejectionReason,
                    SubmittedAt = x.EInvoiceSubmittedAt,
                    ResponseAt = x.EInvoiceResponseAt
                }))
                .ToList();

            foreach (var row in rows)
                row.DaysLeft = (row.Deadline - today).Days;

            model.Outstanding = rows
                .Where(x => EInvoiceStatuses.IsOutstanding(x.Status))
                .OrderBy(x => x.Deadline).ThenBy(x => x.Number)
                .ToList();

            model.AwaitingResponse = rows
                .Where(x => x.Status == EInvoiceStatuses.Submitted)
                .OrderBy(x => x.SubmittedAt).ThenBy(x => x.Number)
                .ToList();

            model.RecentlyAccepted = rows
                .Where(x => x.Status == EInvoiceStatuses.Accepted)
                .OrderByDescending(x => x.ResponseAt).ThenByDescending(x => x.Number)
                .Take(25)
                .ToList();

            // Drafts are not issued yet, so they carry no e-invoice status - but the 14 days
            // run from the supply, not from posting. Count the ones already at risk.
            var start = EInvoiceService.StartDate(company);
            if (company.EInvoicingEnabled && start != null)
            {
                model.UnpostedInScope = await _db.CustomerInvoices
                    .AsNoTracking()
                    .CountAsync(x => (x.JournalEntry == null || !x.JournalEntry.IsPosted)
                                     && x.Status != "Cancelled"
                                     && (x.DateOfSupply ?? x.InvoiceDate) <= today
                                     && (x.DateOfSupply ?? x.InvoiceDate) >= start.Value
                                     && (x.Lease!.Tenant!.PartyType == PartyTypes.Business
                                         || x.Lease.Tenant.PartyType == PartyTypes.Government));
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Generate(string kind, int id)
        {
            var result = kind == EInvoiceService.KindCreditNote
                ? await _eInvoiceService.GenerateCreditNoteAsync(id)
                : await _eInvoiceService.GenerateInvoiceAsync(id);

            Report(result);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateAll()
        {
            var batch = await _eInvoiceService.GenerateAllPendingAsync(User.GetRequiredCurrentCompanyId());

            TempData[batch.Failures.Count == 0 ? "EInvoiceSuccess" : "EInvoiceError"] = batch.Summary;
            if (batch.Failures.Count > 0)
            {
                TempData["EInvoiceProblems"] = string.Join("\n", batch.Failures
                    .SelectMany(x => x.Problems.Count > 0 ? x.Problems.Select(p => $"{x.Message} {p}") : new[] { x.Message })
                    .Distinct()
                    .Take(40));
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reassess()
        {
            var changed = await _eInvoiceService.ReassessAsync(User.GetRequiredCurrentCompanyId());
            TempData["EInvoiceSuccess"] = changed == 0
                ? "Every posted document already had the right e-invoicing status."
                : $"{changed} posted document(s) were re-assessed against the current settings and tenant details.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>The stored file - exactly what was, or will be, sent.</summary>
        [HttpGet]
        public async Task<IActionResult> Download(string kind, int id)
        {
            var (number, xml) = await LoadXmlAsync(kind, id);
            if (xml == null)
                return NotFound();

            return File(Encoding.UTF8.GetBytes(xml), "application/xml", EInvoiceService.FileNameFor(number));
        }

        /// <summary>
        /// Every document ready to send, as one ZIP - for providers whose portal takes a
        /// batch upload. Downloading does not change any status: the user marks them as
        /// sent once the upload has actually happened.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DownloadBatch(string[]? selected)
        {
            var refs = ParseRefs(selected);

            var invoiceQuery = _db.CustomerInvoices.AsNoTracking()
                .Where(x => x.EInvoiceStatus == EInvoiceStatuses.Generated && x.EInvoiceXml != null);
            var noteQuery = _db.CustomerCreditNotes.AsNoTracking()
                .Where(x => x.EInvoiceStatus == EInvoiceStatuses.Generated && x.EInvoiceXml != null);

            if (refs.Count > 0)
            {
                var invoiceIds = refs.Where(x => x.Kind == EInvoiceService.KindInvoice).Select(x => x.Id).ToList();
                var noteIds = refs.Where(x => x.Kind == EInvoiceService.KindCreditNote).Select(x => x.Id).ToList();
                invoiceQuery = invoiceQuery.Where(x => invoiceIds.Contains(x.Id));
                noteQuery = noteQuery.Where(x => noteIds.Contains(x.Id));
            }

            var files = (await invoiceQuery.Select(x => new { Number = x.TaxInvoiceNumber ?? x.InvoiceNumber, Xml = x.EInvoiceXml! }).ToListAsync())
                .Concat(await noteQuery.Select(x => new { Number = x.CreditNoteNumber, Xml = x.EInvoiceXml! }).ToListAsync())
                .ToList();

            if (files.Count == 0)
            {
                TempData["EInvoiceError"] = "There are no generated e-invoices ready to send. Generate them first.";
                return RedirectToAction(nameof(Index));
            }

            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in files)
                {
                    var entry = zip.CreateEntry(EInvoiceService.FileNameFor(file.Number), CompressionLevel.Optimal);
                    await using var stream = entry.Open();
                    var bytes = Encoding.UTF8.GetBytes(file.Xml);
                    await stream.WriteAsync(bytes);
                }
            }

            return File(buffer.ToArray(), "application/zip", $"e-invoices-{DateTime.Now:yyyyMMdd-HHmm}.zip");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkSubmitted(string[]? selected, string? providerReference)
        {
            var refs = ParseRefs(selected);
            if (refs.Count == 0)
            {
                TempData["EInvoiceError"] = "Tick the documents you uploaded to your provider first.";
                return RedirectToAction(nameof(Index));
            }

            var count = await _eInvoiceService.MarkSubmittedAsync(User.GetRequiredCurrentCompanyId(), refs, providerReference);
            TempData["EInvoiceSuccess"] = count == 0
                ? "Nothing changed - only documents that are generated and ready can be marked as sent."
                : $"{count} document(s) marked as sent to the provider. They are now locked: corrections need a credit note.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordResponse(string kind, int id, bool accepted, string? providerReference, string? reason)
        {
            var result = await _eInvoiceService.RecordResponseAsync(
                User.GetRequiredCurrentCompanyId(), new EInvoiceDocumentRef(kind, id), accepted, providerReference, reason);

            Report(result);
            return RedirectToAction(nameof(Index));
        }

        // =====================================================================
        //  SETTINGS
        // =====================================================================

        [HttpGet]
        public async Task<IActionResult> Settings()
        {
            var company = await LoadCompanyAsync();

            var model = new EInvoicingSettingsViewModel
            {
                CompanyName = company.Name,
                LegalName = company.LegalName,
                TrnNumber = company.TrnNumber,
                TaxIdentificationNumber = company.TaxIdentificationNumber,
                LegalRegistrationType = company.LegalRegistrationType,
                TradeLicenseNumber = company.TradeLicenseNumber,
                LicensingAuthority = company.LicensingAuthority,
                AddressLine1 = company.AddressLine1,
                AddressLine2 = company.AddressLine2,
                City = company.City,
                Emirate = company.Emirate,
                Phone = company.Phone,
                Email = company.Email,
                EInvoicePaymentMeansCode = company.EInvoicePaymentMeansCode,
                BankIban = company.BankIban,
                BankAccountName = company.BankAccountName,
                EInvoicingEnabled = company.EInvoicingEnabled,
                EInvoicingRevenueBand = company.EInvoicingRevenueBand,
                EInvoicingStartDate = company.EInvoicingStartDate,
                EInvoicingProviderName = company.EInvoicingProviderName,
                EInvoicingProviderAppointedOn = company.EInvoicingProviderAppointedOn
            };

            await PopulateReadinessAsync(model, company);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Settings(EInvoicingSettingsViewModel model)
        {
            var company = await _db.Companies.FirstAsync(x => x.Id == User.GetRequiredCurrentCompanyId());

            if (!string.IsNullOrWhiteSpace(model.Emirate) && !Emirates.IsValid(model.Emirate))
                ModelState.AddModelError(nameof(model.Emirate), "Choose one of the seven emirates.");

            if (!LegalRegistrationTypes.All.Contains(model.LegalRegistrationType))
                ModelState.AddModelError(nameof(model.LegalRegistrationType), "Choose the registration type.");

            if (!EInvoicePaymentMeans.All.ContainsKey(model.EInvoicePaymentMeansCode ?? string.Empty))
                ModelState.AddModelError(nameof(model.EInvoicePaymentMeansCode), "Choose how tenants pay.");

            if (!string.IsNullOrWhiteSpace(model.EInvoicingRevenueBand)
                && !EInvoicingRevenueBands.All.Contains(model.EInvoicingRevenueBand))
                ModelState.AddModelError(nameof(model.EInvoicingRevenueBand), "Choose the revenue band.");

            // Switching on is a commitment: from then on every in-scope invoice is queued for
            // the provider. It needs a provider and a complete identity first.
            if (model.EInvoicingEnabled)
            {
                if (string.IsNullOrWhiteSpace(model.EInvoicingRevenueBand) && model.EInvoicingStartDate == null)
                    ModelState.AddModelError(nameof(model.EInvoicingRevenueBand),
                        "Choose the revenue band (or a start date) so the system knows which supplies to e-invoice.");

                if (string.IsNullOrWhiteSpace(model.EInvoicingProviderName))
                    ModelState.AddModelError(nameof(model.EInvoicingProviderName),
                        "Enter the Accredited Service Provider you have appointed. E-invoices can only be sent through one.");
            }

            if (!ModelState.IsValid)
            {
                model.CompanyName = company.Name;
                await PopulateReadinessAsync(model, company);
                return View(model);
            }

            var wasEnabled = company.EInvoicingEnabled;
            var oldStart = EInvoiceService.StartDate(company);

            company.LegalName = Trim(model.LegalName);
            company.TrnNumber = Digits(model.TrnNumber);
            company.TaxIdentificationNumber = Digits(model.TaxIdentificationNumber);
            company.LegalRegistrationType = model.LegalRegistrationType;
            company.TradeLicenseNumber = Trim(model.TradeLicenseNumber);
            company.LicensingAuthority = Trim(model.LicensingAuthority);
            company.AddressLine1 = Trim(model.AddressLine1);
            company.AddressLine2 = Trim(model.AddressLine2);
            company.City = Trim(model.City);
            company.Emirate = Trim(model.Emirate);
            company.CountryCode = "AE";
            company.Phone = Trim(model.Phone);
            company.Email = Trim(model.Email);
            company.EInvoicePaymentMeansCode = model.EInvoicePaymentMeansCode;
            company.BankIban = Trim(model.BankIban)?.Replace(" ", string.Empty).ToUpperInvariant();
            company.BankAccountName = Trim(model.BankAccountName);
            company.EInvoicingEnabled = model.EInvoicingEnabled;
            company.EInvoicingRevenueBand = Trim(model.EInvoicingRevenueBand);
            company.EInvoicingStartDate = model.EInvoicingStartDate?.Date;
            company.EInvoicingProviderName = Trim(model.EInvoicingProviderName);
            company.EInvoicingProviderAppointedOn = model.EInvoicingProviderAppointedOn?.Date;

            await _db.SaveChangesAsync();

            var message = "E-invoicing settings saved.";

            // Turning the switch on, or moving the start date, changes the answer for
            // documents already posted. Re-assess them now rather than leaving the queue wrong.
            if (wasEnabled != company.EInvoicingEnabled || oldStart != EInvoiceService.StartDate(company))
            {
                var changed = await _eInvoiceService.ReassessAsync(company.Id);
                if (changed > 0)
                    message += $" {changed} posted document(s) were re-assessed.";
            }

            TempData["EInvoiceSuccess"] = message;
            return RedirectToAction(nameof(Settings));
        }

        private async Task PopulateReadinessAsync(EInvoicingSettingsViewModel model, Company company)
        {
            model.DerivedTin = Company.TinFromTrn(company.TrnNumber);
            model.CompanyProblems = EInvoiceService.CheckCompany(company);

            var tenants = await _db.Tenants.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync();

            model.BusinessTenants = tenants.Count(x => x.PartyType == PartyTypes.Business);
            model.GovernmentTenants = tenants.Count(x => x.PartyType == PartyTypes.Government);
            model.IndividualTenants = tenants.Count(x => x.PartyType == PartyTypes.Individual);

            model.TenantIssues = tenants
                .Where(x => PartyTypes.IsInScope(x.PartyType))
                .Select(x => new EInvoiceTenantIssueViewModel
                {
                    TenantId = x.Id,
                    TenantCode = x.Code,
                    TenantName = x.FullName,
                    Problems = EInvoiceService.CheckTenant(x)
                })
                .Where(x => x.Problems.Count > 0)
                .ToList();

            // An individual holding a trade licence or TRN is very likely a business that
            // was set up before e-invoicing existed. Flag it rather than guess.
            foreach (var tenant in tenants.Where(x => x.PartyType == PartyTypes.Individual
                                                      && (!string.IsNullOrWhiteSpace(x.TradeLicenseNumber)
                                                          || !string.IsNullOrWhiteSpace(x.TrnNumber))))
            {
                model.TenantIssues.Add(new EInvoiceTenantIssueViewModel
                {
                    TenantId = tenant.Id,
                    TenantCode = tenant.Code,
                    TenantName = tenant.FullName,
                    Problems = new List<string>
                    {
                        "Marked as an individual but holds a trade licence or TRN. If this tenant is a business, " +
                        "change the customer type to Business - their invoices then need an e-invoice."
                    }
                });
            }
        }

        // =====================================================================
        //  RECEIVED FROM SUPPLIERS
        // =====================================================================

        [HttpGet]
        public async Task<IActionResult> Received()
        {
            var items = await _db.ReceivedEInvoices
                .AsNoTracking()
                .OrderByDescending(x => x.ReceivedAt)
                .Select(x => new ReceivedEInvoiceListItemViewModel
                {
                    Id = x.Id,
                    DocumentNumber = x.DocumentNumber,
                    DocumentTypeCode = x.DocumentTypeCode,
                    IssueDate = x.IssueDate,
                    SupplierName = x.SupplierName,
                    SupplierTrn = x.SupplierTrn,
                    VendorId = x.VendorId,
                    VendorName = x.Vendor != null ? x.Vendor.Name : null,
                    TotalAmount = x.TotalAmount,
                    Status = x.Status,
                    VendorBillId = x.VendorBillId,
                    VendorBillNumber = x.VendorBill != null ? x.VendorBill.BillNumber : null,
                    ReceivedAt = x.ReceivedAt
                })
                .ToListAsync();

            return View(items);
        }

        /// <summary>
        /// Takes the XML files the provider delivered. Each is read, matched to a vendor by
        /// TRN or TIN, and kept as received. Duplicates - the same UUID again - are refused.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(26_214_400)]
        public async Task<IActionResult> Upload(List<IFormFile>? files)
        {
            var companyId = User.GetRequiredCurrentCompanyId();

            if (files == null || files.Count == 0)
            {
                TempData["EInvoiceError"] = "Choose one or more e-invoice XML files to upload.";
                return RedirectToAction(nameof(Received));
            }

            var vendors = await _db.Vendors.AsNoTracking().ToListAsync();
            var accepted = 0;
            var problems = new List<string>();

            foreach (var file in files)
            {
                if (file.Length == 0 || file.Length > EInvoiceReader.MaxFileBytes)
                {
                    problems.Add($"{file.FileName}: empty, or larger than 5 MB.");
                    continue;
                }

                string xml;
                using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                    xml = await reader.ReadToEndAsync();

                ParsedEInvoice parsed;
                try
                {
                    parsed = EInvoiceReader.Parse(xml);
                }
                catch (InvalidOperationException ex)
                {
                    problems.Add($"{file.FileName}: {ex.Message}");
                    continue;
                }

                if (await _db.ReceivedEInvoices.AnyAsync(x => x.Uuid == parsed.Uuid))
                {
                    problems.Add($"{file.FileName}: document {parsed.DocumentNumber} from {parsed.SupplierName} was already received.");
                    continue;
                }

                var vendor = MatchVendor(vendors, parsed);

                _db.ReceivedEInvoices.Add(new ReceivedEInvoice
                {
                    CompanyId = companyId,
                    Uuid = parsed.Uuid,
                    DocumentTypeCode = parsed.DocumentTypeCode,
                    DocumentNumber = parsed.DocumentNumber,
                    IssueDate = parsed.IssueDate,
                    DueDate = parsed.DueDate,
                    SupplierName = parsed.SupplierName,
                    SupplierTrn = parsed.SupplierTrn,
                    SupplierTin = parsed.SupplierTin,
                    VendorId = vendor?.Id,
                    Currency = parsed.Currency,
                    NetAmount = parsed.NetAmount,
                    VatAmount = parsed.VatAmount,
                    TotalAmount = parsed.TotalAmount,
                    Xml = xml,
                    FileName = Path.GetFileName(file.FileName),
                    Status = "Received",
                    ReceivedAt = DateTime.UtcNow,
                    ReceivedByAppUserId = User.GetAppUserId()
                });

                await _db.SaveChangesAsync();
                accepted++;

                if (!parsed.IsUaeFormat)
                    problems.Add($"{file.FileName}: received, but it is not marked as a UAE (PINT AE) document - check it with the supplier.");
                if (vendor == null)
                    problems.Add($"{file.FileName}: received, but no vendor has TRN/TIN {parsed.SupplierTrn ?? parsed.SupplierTin}. " +
                                 "Set the supplier up under Master Data ▸ Vendors, then book it.");
            }

            if (accepted > 0)
                TempData["EInvoiceSuccess"] = $"{accepted} e-invoice(s) received.";
            if (problems.Count > 0)
            {
                TempData["EInvoiceError"] = accepted == 0 ? "Nothing was received." : "Some files need attention.";
                TempData["EInvoiceProblems"] = string.Join("\n", problems);
            }

            return RedirectToAction(nameof(Received));
        }

        [HttpGet]
        public async Task<IActionResult> ReceivedFile(int id)
        {
            var item = await _db.ReceivedEInvoices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (item == null)
                return NotFound();

            return File(Encoding.UTF8.GetBytes(item.Xml), "application/xml", EInvoiceService.FileNameFor(item.DocumentNumber));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetReceivedStatus(int id, bool ignore)
        {
            var item = await _db.ReceivedEInvoices.FirstOrDefaultAsync(x => x.Id == id);
            if (item == null)
                return NotFound();

            if (item.VendorBillId != null)
            {
                TempData["EInvoiceError"] = "This document is already booked as a vendor bill.";
                return RedirectToAction(nameof(Received));
            }

            // Re-matching on restore picks up a vendor set up since the file arrived.
            if (!ignore && item.VendorId == null)
            {
                var vendors = await _db.Vendors.AsNoTracking().ToListAsync();
                item.VendorId = MatchVendor(vendors, EInvoiceReader.Parse(item.Xml))?.Id;
            }

            item.Status = ignore ? "Ignored" : "Received";
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Received));
        }

        /// <summary>Matches on TRN first, then TIN - never on name, which is not unique.</summary>
        public static Vendor? MatchVendor(IEnumerable<Vendor> vendors, ParsedEInvoice parsed)
        {
            var list = vendors.ToList();

            if (!string.IsNullOrWhiteSpace(parsed.SupplierTrn))
            {
                var byTrn = list.FirstOrDefault(x => x.TrnNumber?.Trim() == parsed.SupplierTrn.Trim());
                if (byTrn != null) return byTrn;
            }

            var tin = parsed.SupplierTin ?? Company.TinFromTrn(parsed.SupplierTrn);
            return string.IsNullOrWhiteSpace(tin)
                ? null
                : list.FirstOrDefault(x => x.EffectiveTin == tin.Trim());
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        private async Task<(string Number, string? Xml)> LoadXmlAsync(string kind, int id)
        {
            if (kind == EInvoiceService.KindCreditNote)
            {
                var note = await _db.CustomerCreditNotes.AsNoTracking()
                    .Where(x => x.Id == id)
                    .Select(x => new { x.CreditNoteNumber, x.EInvoiceXml })
                    .FirstOrDefaultAsync();
                return (note?.CreditNoteNumber ?? string.Empty, note?.EInvoiceXml);
            }

            var invoice = await _db.CustomerInvoices.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new { Number = x.TaxInvoiceNumber ?? x.InvoiceNumber, x.EInvoiceXml })
                .FirstOrDefaultAsync();
            return (invoice?.Number ?? string.Empty, invoice?.EInvoiceXml);
        }

        private static List<EInvoiceDocumentRef> ParseRefs(string[]? selected)
        {
            var refs = new List<EInvoiceDocumentRef>();
            foreach (var key in selected ?? Array.Empty<string>())
            {
                var parts = key.Split(':');
                if (parts.Length == 2
                    && (parts[0] == EInvoiceService.KindInvoice || parts[0] == EInvoiceService.KindCreditNote)
                    && int.TryParse(parts[1], out var id))
                {
                    refs.Add(new EInvoiceDocumentRef(parts[0], id));
                }
            }
            return refs;
        }

        private void Report(EInvoiceResult result)
        {
            TempData[result.Success ? "EInvoiceSuccess" : "EInvoiceError"] = result.Message;
            if (result.Problems.Count > 0)
                TempData["EInvoiceProblems"] = string.Join("\n", result.Problems);
        }

        private async Task<Company> LoadCompanyAsync()
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            return await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId);
        }

        private static string? Trim(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? Digits(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : new string(value.Where(char.IsLetterOrDigit).ToArray());
    }
}
