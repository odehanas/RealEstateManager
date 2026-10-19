using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Credit notes against customer invoices.
    ///
    /// A credit note reverses part or all of ONE invoice, at the tax treatment that
    /// invoice was issued under. It may not exceed what is still outstanding: crediting
    /// money the tenant has already paid would leave them in credit, which is a refund
    /// and is handled separately.
    /// </summary>
    public class CreditNoteService
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _postingService;

        public CreditNoteService(ApplicationDbContext db, AccountingPostingService postingService)
        {
            _db = db;
            _postingService = postingService;
        }

        // =====================================================================
        //  LOAD
        // =====================================================================

        public async Task<CreditNoteEditViewModel?> BuildNewAsync(int invoiceId)
        {
            var invoice = await LoadInvoiceAsync(invoiceId);
            if (invoice == null)
                return null;

            var model = new CreditNoteEditViewModel
            {
                CustomerInvoiceId = invoice.Id,
                CreditNoteDate = DateTime.Today
            };

            await FillContextAsync(model, invoice);
            return model;
        }

        public async Task<CreditNoteEditViewModel?> BuildEditAsync(int creditNoteId)
        {
            var note = await _db.CustomerCreditNotes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == creditNoteId);
            if (note == null)
                return null;

            var invoice = await LoadInvoiceAsync(note.CustomerInvoiceId);
            if (invoice == null)
                return null;

            var model = new CreditNoteEditViewModel
            {
                Id = note.Id,
                CustomerInvoiceId = note.CustomerInvoiceId,
                CreditNoteNumber = note.CreditNoteNumber,
                CreditNoteDate = note.CreditNoteDate,
                ReasonCode = note.ReasonCode,
                Reason = note.Reason,
                NetAmount = note.Subtotal,
                DebitAccountId = note.DebitAccountId
            };

            await FillContextAsync(model, invoice);
            return model;
        }

        /// <summary>Re-reads the invoice context so a redisplayed form is never stale.</summary>
        public async Task RefreshContextAsync(CreditNoteEditViewModel model)
        {
            var invoice = await LoadInvoiceAsync(model.CustomerInvoiceId);
            if (invoice != null)
                await FillContextAsync(model, invoice);
        }

        private async Task FillContextAsync(CreditNoteEditViewModel model, CustomerInvoice invoice)
        {
            var lease = invoice.Lease!;

            model.InvoiceNumber = invoice.InvoiceNumber;
            model.TaxInvoiceNumber = invoice.TaxInvoiceNumber;
            model.InvoiceDate = invoice.InvoiceDate;
            model.TenantName = lease.Tenant?.FullName ?? string.Empty;
            model.PropertyUnit = $"{lease.Unit?.Property?.Name}, unit {lease.Unit?.UnitNumber}";
            model.ContractNumber = lease.ContractNumber;
            model.VatCategory = invoice.VatCategory;
            model.VatRate = invoice.VatRate;
            model.InvoiceSubtotal = invoice.Subtotal;
            model.InvoiceVat = invoice.VatAmount;
            model.InvoiceTotal = invoice.TotalAmount;
            model.AlreadySettled = invoice.AmountPaid;
            model.InvoiceEInvoiceStatus = invoice.EInvoiceStatus;

            // When editing a draft, the draft's own amount is not yet settled, so it is
            // part of what may be credited.
            model.Outstanding = invoice.TotalAmount - invoice.AmountPaid;

            var defaultAccountId = await _postingService.GetInvoiceNetAccountIdAsync(
                invoice.CompanyId, lease.Unit!.Property!.OwnershipType);

            var accounts = await _db.Accounts
                .AsNoTracking()
                .Where(x => x.IsActive && x.IsPostable && !x.IsGroup
                            && (x.Id == defaultAccountId
                                || (!x.IsControlAccount && (x.AccountType == "Revenue" || x.AccountType == "Liability"))))
                .OrderBy(x => x.Code)
                .Select(x => new { x.Id, x.Code, x.Name })
                .ToListAsync();

            model.DefaultAccountName = accounts
                .Where(x => x.Id == defaultAccountId)
                .Select(x => $"{x.Code} - {x.Name}")
                .FirstOrDefault();

            model.Accounts = accounts
                .Select(x => new SelectListItem
                {
                    Value = x.Id.ToString(),
                    Text = $"{x.Code} - {x.Name}" + (x.Id == defaultAccountId ? "  (same account as the invoice)" : string.Empty)
                })
                .ToList();
        }

        // =====================================================================
        //  SAVE
        // =====================================================================

        /// <summary>Creates or updates a draft. Returns null with reasons in modelState when refused.</summary>
        public async Task<CustomerCreditNote?> SaveAsync(
            ModelStateDictionary modelState, int companyId, int? appUserId, CreditNoteEditViewModel model)
        {
            var invoice = await _db.CustomerInvoices
                .Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == model.CustomerInvoiceId && x.CompanyId == companyId);

            if (invoice == null)
            {
                modelState.AddModelError(string.Empty, "The invoice was not found.");
                return null;
            }

            if (invoice.JournalEntry?.IsPosted != true)
            {
                modelState.AddModelError(string.Empty,
                    "Only a posted invoice can be credited. A draft invoice can simply be corrected - it has not been issued.");
                return null;
            }

            CustomerCreditNote? note = null;
            if (model.Id != 0)
            {
                note = await _db.CustomerCreditNotes
                    .Include(x => x.JournalEntry)
                    .FirstOrDefaultAsync(x => x.Id == model.Id && x.CompanyId == companyId);

                if (note == null)
                {
                    modelState.AddModelError(string.Empty, "The credit note was not found.");
                    return null;
                }

                if (note.JournalEntry?.IsPosted == true)
                {
                    modelState.AddModelError(string.Empty, "This credit note is posted. Reset it to draft before changing it.");
                    return null;
                }
            }

            if (model.CreditNoteDate == default)
                modelState.AddModelError(nameof(model.CreditNoteDate), "Enter the credit note date.");
            else if (model.CreditNoteDate.Date < invoice.InvoiceDate.Date)
                modelState.AddModelError(nameof(model.CreditNoteDate), "A credit note cannot be dated before the invoice it credits.");

            if (!CreditNoteReasons.ForLeases.ContainsKey(model.ReasonCode ?? string.Empty))
                modelState.AddModelError(nameof(model.ReasonCode), "Choose the reason for the credit note.");

            if (string.IsNullOrWhiteSpace(model.Reason))
                modelState.AddModelError(nameof(model.Reason), "Explain the credit in a sentence - it is printed on the document.");

            var outstanding = invoice.TotalAmount - invoice.AmountPaid;
            var (net, vat) = CalculateAmounts(invoice, model, outstanding);

            if (net <= 0m)
                modelState.AddModelError(nameof(model.NetAmount), "Enter the net amount to credit.");
            else if (net + vat > outstanding)
                modelState.AddModelError(nameof(model.NetAmount),
                    $"The credit ({net + vat:N2} including VAT) is more than the {outstanding:N2} still outstanding on this invoice. " +
                    "Amounts the tenant has already paid are refunded, not credited.");

            if (model.DebitAccountId.HasValue)
            {
                var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == model.DebitAccountId.Value);
                if (account == null || account.IsGroup || !account.IsPostable)
                    modelState.AddModelError(nameof(model.DebitAccountId), "Choose a postable account.");
            }

            if (!modelState.IsValid)
                return null;

            if (note == null)
            {
                note = new CustomerCreditNote
                {
                    CompanyId = companyId,
                    CustomerInvoiceId = invoice.Id,
                    CreditNoteNumber = await GenerateNumberAsync(companyId, model.CreditNoteDate),
                    CreatedAt = DateTime.UtcNow,
                    CreatedByAppUserId = appUserId,
                    Status = "Draft"
                };
                _db.CustomerCreditNotes.Add(note);
            }

            note.CreditNoteDate = model.CreditNoteDate.Date;
            note.ReasonCode = model.ReasonCode!;
            note.Reason = model.Reason.Trim();
            note.VatCategory = invoice.VatCategory;
            note.VatRate = invoice.VatRate;
            note.Subtotal = net;
            note.VatAmount = vat;
            note.TotalAmount = net + vat;
            note.DebitAccountId = model.DebitAccountId;

            await _db.SaveChangesAsync();
            return note;
        }

        /// <summary>
        /// Net and VAT for the credit. A full credit of an untouched invoice reverses its
        /// figures exactly; anything else is computed from the net, at the invoice's rate.
        /// </summary>
        private static (decimal Net, decimal Vat) CalculateAmounts(
            CustomerInvoice invoice, CreditNoteEditViewModel model, decimal outstanding)
        {
            if (model.CreditFullOutstanding)
            {
                if (invoice.AmountPaid == 0m)
                    return (invoice.Subtotal, invoice.VatAmount);

                var factor = 1m + (VatCategories.IsTaxable(invoice.VatCategory) ? invoice.VatRate / 100m : 0m);
                var net = VatDeterminationService.Round(outstanding / factor);
                var vat = VatDeterminationService.CalculateTax(net, invoice.VatCategory);

                // Rounding can push net + VAT a fils over the balance; step back until it fits.
                while (net > 0m && net + vat > outstanding)
                {
                    net -= 0.01m;
                    vat = VatDeterminationService.CalculateTax(net, invoice.VatCategory);
                }

                return (net, vat);
            }

            var amount = VatDeterminationService.Round(model.NetAmount);
            return (amount, VatDeterminationService.CalculateTax(amount, invoice.VatCategory));
        }

        /// <summary>One sequence per company per year: CN-2026-000001. Never reused.</summary>
        private async Task<string> GenerateNumberAsync(int companyId, DateTime date)
        {
            var prefix = $"CN-{date:yyyy}-";

            var last = await _db.CustomerCreditNotes
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.CreditNoteNumber.StartsWith(prefix))
                .OrderByDescending(x => x.CreditNoteNumber)
                .Select(x => x.CreditNoteNumber)
                .FirstOrDefaultAsync();

            var next = 1;
            if (!string.IsNullOrEmpty(last) && int.TryParse(last[prefix.Length..], out var parsed))
                next = parsed + 1;

            return $"{prefix}{next:D6}";
        }

        // =====================================================================
        //  DOCUMENT
        // =====================================================================

        public async Task<CreditNoteDocumentViewModel?> BuildDocumentAsync(int creditNoteId)
        {
            var note = await _db.CustomerCreditNotes
                .AsNoTracking()
                .Include(x => x.JournalEntry)
                .Include(x => x.CustomerInvoice)!.ThenInclude(x => x!.Lease)!.ThenInclude(x => x!.Tenant)
                .Include(x => x.CustomerInvoice)!.ThenInclude(x => x!.Lease)!.ThenInclude(x => x!.Unit)!.ThenInclude(x => x!.Property)
                .FirstOrDefaultAsync(x => x.Id == creditNoteId);

            if (note?.CustomerInvoice?.Lease?.Tenant == null)
                return null;

            var company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == note.CompanyId);
            var invoice = note.CustomerInvoice;
            var lease = invoice.Lease!;
            var tenant = lease.Tenant!;

            var supplierAddress = string.Join(", ", new[]
            {
                company.AddressLine1, company.AddressLine2, company.City, Emirates.Describe(company.Emirate), company.Country
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

            var tenantAddress = string.Join(", ", new[]
            {
                tenant.Address, tenant.City, Emirates.Describe(tenant.Emirate)
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

            return new CreditNoteDocumentViewModel
            {
                Title = company.IsVatRegistered && note.VatCategory != VatCategories.Exempt ? "Tax Credit Note" : "Credit Note",
                CreditNoteNumber = note.CreditNoteNumber,
                CreditNoteDate = note.CreditNoteDate,
                ReasonCode = note.ReasonCode,
                Reason = note.Reason,
                IsPosted = note.JournalEntry?.IsPosted == true,
                SupplierName = string.IsNullOrWhiteSpace(company.LegalName) ? company.Name : company.LegalName,
                SupplierTrn = company.TrnNumber,
                SupplierAddress = supplierAddress,
                TenantName = tenant.FullName,
                TenantAddress = tenantAddress,
                TenantTrn = invoice.RecipientTrn ?? tenant.TrnNumber,
                OriginalInvoiceNumber = invoice.TaxInvoiceNumber ?? invoice.InvoiceNumber,
                OriginalInvoiceDate = invoice.InvoiceDate,
                PropertyUnit = $"{lease.Unit?.Property?.Name}, unit {lease.Unit?.UnitNumber}",
                ContractNumber = lease.ContractNumber,
                VatCategory = note.VatCategory,
                VatRate = note.VatRate,
                Subtotal = note.Subtotal,
                VatAmount = note.VatAmount,
                TotalAmount = note.TotalAmount,
                EInvoiceStatus = note.EInvoiceStatus,
                EInvoiceUuid = note.EInvoiceUuid
            };
        }

        private async Task<CustomerInvoice?> LoadInvoiceAsync(int invoiceId)
        {
            return await _db.CustomerInvoices
                .AsNoTracking()
                .Include(x => x.Lease)!.ThenInclude(x => x!.Tenant)
                .Include(x => x.Lease)!.ThenInclude(x => x!.Unit)!.ThenInclude(x => x!.Property)
                .FirstOrDefaultAsync(x => x.Id == invoiceId);
        }
    }
}
