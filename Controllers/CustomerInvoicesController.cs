using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class CustomerInvoicesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _postingService;
        private readonly JournalWorkflowService _journalWorkflowService;

        public CustomerInvoicesController(ApplicationDbContext db, AccountingPostingService postingService,
            JournalWorkflowService journalWorkflowService)
        {
            _db = db;
            _postingService = postingService;
            _journalWorkflowService = journalWorkflowService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var invoices = await _db.CustomerInvoices
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                .Include(x => x.Receipts)
                // Needed to tell a posted invoice from one whose journal is only a draft.
                .Include(x => x.JournalEntry)
                .OrderByDescending(x => x.InvoiceDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return View(invoices);
        }

        /// <summary>
        /// The printable tax invoice. Full or simplified is decided by whether the
        /// tenant has a TRN; an exempt supply prints as a plain invoice, because a
        /// document with no output tax is not a tax invoice.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> TaxInvoice(int id)
        {
            var companyId = User.GetRequiredCurrentCompanyId();

            var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var invoice = await _db.CustomerInvoices
                .AsNoTracking()
                .Include(x => x.Lease)!.ThenInclude(x => x!.Tenant)
                .Include(x => x.Lease)!.ThenInclude(x => x!.Unit)!.ThenInclude(x => x!.Property)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (invoice?.Lease?.Tenant == null || invoice.Lease.Unit?.Property == null)
                return NotFound();

            var lease = invoice.Lease;
            var tenant = lease.Tenant!;
            var unit = lease.Unit!;
            var property = unit.Property!;

            var model = new TaxInvoiceViewModel
            {
                SupplierName = company.Name,
                SupplierLegalName = company.LegalName,
                SupplierTrn = company.TrnNumber,
                SupplierAddressLine1 = company.AddressLine1,
                SupplierAddressLine2 = company.AddressLine2,
                SupplierCity = company.City,
                SupplierCountry = company.Country,
                SupplierPhone = company.Phone,
                SupplierEmail = company.Email,

                TenantName = tenant.FullName,
                TenantAddress = tenant.Address,
                TenantPhone = tenant.Phone,
                TenantEmail = tenant.Email,
                // Prefer the TRN captured when the invoice was issued, so a reprint
                // reproduces the document as it was, not as the tenant is today.
                RecipientTrn = invoice.RecipientTrn ?? tenant.TrnNumber,

                DocumentNumber = invoice.TaxInvoiceNumber ?? invoice.InvoiceNumber,
                SourceInvoiceNumber = invoice.TaxInvoiceNumber != null ? invoice.InvoiceNumber : null,
                EInvoiceUuid = invoice.EInvoiceUuid,
                EInvoiceStatus = invoice.EInvoiceStatus,
                CreditedAmount = invoice.CreditedAmount,
                TaxInvoiceType = invoice.TaxInvoiceType,
                InvoiceDate = invoice.InvoiceDate,
                DateOfSupply = invoice.DateOfSupply ?? invoice.InvoiceDate,
                DueDate = invoice.DueDate,

                PropertyName = $"{property.Code} - {property.Name}",
                UnitNumber = unit.UnitNumber,
                ContractNumber = lease.ContractNumber,
                LeasePeriod = $"{lease.StartDate:dd/MM/yyyy} to {lease.EndDate:dd/MM/yyyy}",
                LineDescription = "Rent for leased premises",
                LineDetail = $"{property.Name}, unit {unit.UnitNumber} — contract {lease.ContractNumber}",

                VatCategory = invoice.VatCategory,
                VatRate = invoice.VatRate,
                Subtotal = invoice.Subtotal,
                VatAmount = invoice.VatAmount,
                TotalAmount = invoice.TotalAmount,
                AmountPaid = invoice.AmountPaid,

                Currency = company.BaseCurrency,
                CurrencyName = company.BaseCurrency == "AED" ? "UAE Dirhams (AED)" : company.BaseCurrency
            };

            // Surface anything that would make this document non-compliant, on screen
            // only, so it is never handed over unknowingly.
            if (VatCategories.IsTaxable(invoice.VatCategory) || invoice.VatCategory == VatCategories.ZeroRated)
            {
                if (string.IsNullOrWhiteSpace(company.TrnNumber))
                    model.ComplianceWarnings.Add("The company has no TRN. Set it under Setup ▸ Companies before issuing tax invoices.");

                if (string.IsNullOrWhiteSpace(company.AddressLine1))
                    model.ComplianceWarnings.Add("The company has no address. A tax invoice must show the supplier's address.");

                if (string.IsNullOrWhiteSpace(invoice.TaxInvoiceNumber))
                    model.ComplianceWarnings.Add("No sequential tax invoice number has been allocated yet — post the invoice first.");

                if (invoice.TaxInvoiceType == TaxInvoiceTypes.Full && string.IsNullOrWhiteSpace(model.RecipientTrn))
                    model.ComplianceWarnings.Add("This is marked as a full tax invoice but the recipient TRN is missing.");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Post(int id)
        {
            // An invoice is already a draft DOCUMENT before this button is pressed, so
            // pressing Post must both write the journal and post it - one click, as in
            // Odoo. Reset to Draft is then available from Journal Inquiry.
            try
            {
                var journal = await _postingService.PostCustomerInvoiceAsync(id);
                var result = await _journalWorkflowService.PostAsync(journal.Id);

                TempData[result.Success ? "InvoiceSuccess" : "InvoiceError"] = result.Message;
            }
            catch (Exception ex)
            {
                TempData["InvoiceError"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }
    }
}