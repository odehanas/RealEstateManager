using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Everything a vendor bill knows how to do to itself: validation, tax
    /// determination, and keeping the header totals equal to the sum of its lines.
    ///
    /// The single rule that drives the tax on a purchase document is the supplier's
    /// registration:
    ///
    ///   Supplier IS VAT registered (live registration and a TRN on file)
    ///       Taxable lines carry 5%. The document is a tax invoice and the input tax
    ///       is claimable to the extent the attribution allows.
    ///
    ///   Supplier is NOT registered
    ///       No tax anywhere on the document. It is a commercial invoice, and the whole
    ///       amount is a direct cost. An unregistered supplier cannot charge VAT, and
    ///       reclaiming tax they were never entitled to charge is a disallowed claim.
    ///
    /// That decision is snapshotted onto the bill when it is entered, so deregistering
    /// a supplier tomorrow never silently rewrites a document already in the accounts.
    /// </summary>
    public class VendorBillService
    {
        private readonly ApplicationDbContext _db;

        public VendorBillService(ApplicationDbContext db)
        {
            _db = db;
        }

        // =====================================================================
        //  LOAD
        // =====================================================================

        public VendorBillEditViewModel BuildNew()
        {
            return new VendorBillEditViewModel
            {
                BillDate = DateTime.Today,
                AccountingDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(30),
                Lines = new List<VendorBillLineViewModel>
                {
                    new() { Quantity = 1m }
                }
            };
        }

        public async Task<VendorBillEditViewModel?> BuildEditAsync(int id)
        {
            var bill = await _db.VendorBills
                .AsNoTracking()
                .Include(x => x.Vendor)
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (bill == null)
                return null;

            return new VendorBillEditViewModel
            {
                Id = bill.Id,
                BillNumber = bill.BillNumber,
                VendorId = bill.VendorId,
                VendorReference = bill.VendorReference,
                BillDate = bill.BillDate,
                AccountingDate = bill.AccountingDate == default ? bill.BillDate : bill.AccountingDate,
                DueDate = bill.DueDate,
                PropertyId = bill.PropertyId,
                ExpenseCategory = bill.ExpenseCategory,
                Notes = bill.Notes,
                Status = bill.Status,
                IsEditable = bill.IsEditable,
                JournalEntryId = bill.JournalEntryId,
                AmountPaid = bill.AmountPaid,
                VendorCanChargeVat = bill.Vendor?.CanChargeVat ?? false,
                VendorTrn = bill.Vendor?.TrnNumber,
                Lines = bill.Lines
                    .OrderBy(x => x.LineNumber)
                    .Select(x => new VendorBillLineViewModel
                    {
                        Id = x.Id,
                        Description = x.Description,
                        AccountId = x.AccountId,
                        PropertyId = x.PropertyId,
                        Quantity = x.Quantity,
                        UnitPrice = x.UnitPrice,
                        IsTaxable = x.IsTaxable,
                        InputTaxAttribution = x.InputTaxAttribution,
                        DevelopmentCost = DevelopmentProjectService.CostTag(x.DevelopmentProjectId, x.DevelopmentCostCategory)
                    })
                    .ToList()
            };
        }

        // =====================================================================
        //  SAVE
        // =====================================================================

        /// <summary>
        /// Creates or updates a draft bill. Returns the saved bill, or null when
        /// validation failed - in which case modelState carries the reasons.
        /// </summary>
        public async Task<VendorBill?> SaveAsync(
            ModelStateDictionary modelState, int companyId, VendorBillEditViewModel model)
        {
            var vendor = await _db.Vendors.FirstOrDefaultAsync(x => x.Id == model.VendorId);
            if (vendor == null)
            {
                modelState.AddModelError(nameof(model.VendorId), "Select the supplier this bill came from.");
                return null;
            }

            // Rows the user removed, and rows they never filled in, are simply not part
            // of the document.
            var lines = model.Lines
                .Where(x => !x.IsDeleted && !x.IsBlank)
                .ToList();

            if (lines.Count == 0)
            {
                modelState.AddModelError(string.Empty, "A bill needs at least one line.");
                return null;
            }

            ValidateHeader(modelState, model);

            // A development cost goes to its project's work-in-progress account, so the
            // project's cost always agrees with the ledger.
            var projectTags = new Dictionary<VendorBillLineViewModel, (int ProjectId, string Category)>();
            for (var i = 0; i < lines.Count; i++)
            {
                var (project, category, tagError) = await DevelopmentProjectService.ParseCostTagAsync(_db, lines[i].DevelopmentCost);
                if (tagError != null)
                {
                    modelState.AddModelError(string.Empty, $"Line {i + 1}: {tagError}.");
                    continue;
                }

                if (project == null)
                    continue;

                lines[i].AccountId = project.WipAccountId;
                lines[i].PropertyId ??= project.PropertyId;
                projectTags[lines[i]] = (project.Id, category!);
            }

            await ValidateLinesAsync(modelState, companyId, lines);

            if (!modelState.IsValid)
                return null;

            VendorBill bill;

            if (model.Id == 0)
            {
                bill = new VendorBill
                {
                    CompanyId = companyId,
                    BillNumber = await GenerateBillNumberAsync(companyId, model.AccountingDate),
                    Status = "Draft"
                };

                _db.VendorBills.Add(bill);
            }
            else
            {
                var existing = await _db.VendorBills
                    .Include(x => x.Lines)
                    .FirstOrDefaultAsync(x => x.Id == model.Id && x.CompanyId == companyId);

                if (existing == null)
                {
                    modelState.AddModelError(string.Empty, "The bill was not found.");
                    return null;
                }

                if (!existing.IsEditable)
                {
                    modelState.AddModelError(string.Empty,
                        "This bill is posted. Reset it to draft before changing it.");
                    return null;
                }

                bill = existing;

                // Replace the lines wholesale. Matching row-by-row would buy nothing
                // here - the grid is small and a draft carries no history worth keeping.
                _db.VendorBillLines.RemoveRange(bill.Lines);
                bill.Lines.Clear();
            }

            var canChargeVat = vendor.CanChargeVat;
            var company = await _db.Companies.FirstAsync(x => x.Id == companyId);

            bill.VendorId = vendor.Id;
            bill.VendorReference = Trim(model.VendorReference);
            bill.BillDate = model.BillDate.Date;
            bill.AccountingDate = model.AccountingDate.Date;
            bill.DueDate = model.DueDate.Date;
            bill.PropertyId = model.PropertyId;
            bill.ExpenseCategory = Trim(model.ExpenseCategory) ?? string.Empty;
            bill.Notes = Trim(model.Notes);
            bill.VendorIsVatRegistered = canChargeVat;
            bill.SupplierTrn = canChargeVat ? Trim(vendor.TrnNumber) : null;

            var lineNumber = 1;
            foreach (var source in lines)
            {
                var line = BuildLine(source, lineNumber++, canChargeVat, company.ProvisionalVatRecoveryRate);
                if (projectTags.TryGetValue(source, out var tag))
                {
                    line.DevelopmentProjectId = tag.ProjectId;
                    line.DevelopmentCostCategory = tag.Category;
                }

                bill.Lines.Add(line);
            }

            RecalculateHeader(bill);

            await _db.SaveChangesAsync();

            // A bill booked from a supplier e-invoice is linked to it, so the same document
            // cannot be booked a second time.
            if (model.ReceivedEInvoiceId.HasValue)
            {
                var received = await _db.ReceivedEInvoices
                    .FirstOrDefaultAsync(x => x.Id == model.ReceivedEInvoiceId.Value && x.CompanyId == companyId);

                if (received != null && received.VendorBillId == null)
                {
                    received.VendorBillId = bill.Id;
                    received.VendorId ??= bill.VendorId;
                    received.Status = "Booked";
                    await _db.SaveChangesAsync();
                }
            }

            return bill;
        }

        /// <summary>
        /// A new draft pre-filled from a supplier's e-invoice: their number, dates and
        /// lines. The accounts are left for the user to choose - the supplier cannot know
        /// how we classify their charge.
        /// </summary>
        public async Task<VendorBillEditViewModel?> BuildFromReceivedAsync(int receivedId)
        {
            var received = await _db.ReceivedEInvoices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == receivedId);
            if (received == null || received.VendorBillId != null || received.IsCreditNote)
                return null;

            var parsed = EInvoiceReader.Parse(received.Xml);
            var vendor = received.VendorId.HasValue
                ? await _db.Vendors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == received.VendorId.Value)
                : null;

            var model = BuildNew();
            model.ReceivedEInvoiceId = received.Id;
            model.ReceivedNet = received.NetAmount;
            model.ReceivedVat = received.VatAmount;
            model.ReceivedTotal = received.TotalAmount;
            model.ReceivedSupplierName = received.SupplierName;

            model.VendorId = vendor?.Id ?? 0;
            model.VendorCanChargeVat = vendor?.CanChargeVat ?? false;
            model.VendorTrn = vendor?.TrnNumber;
            model.VendorReference = received.DocumentNumber;
            model.BillDate = received.IssueDate.Date;
            model.AccountingDate = received.IssueDate.Date;
            model.DueDate = (received.DueDate ?? received.IssueDate).Date;
            model.Notes = $"Booked from supplier e-invoice {received.DocumentNumber} (UUID {received.Uuid}).";
            model.ExpenseCategory = "Supplier e-invoice";

            model.Lines = parsed.Lines.Count == 0
                ? new List<VendorBillLineViewModel> { new() { Quantity = 1m } }
                : parsed.Lines.Select(x => new VendorBillLineViewModel
                    {
                        Description = x.Description.Length > 250 ? x.Description[..250] : x.Description,
                        // One line at the supplier's line amount reproduces their figure
                        // exactly, whatever their quantity and unit price were.
                        Quantity = 1m,
                        UnitPrice = x.NetAmount,
                        IsTaxable = x.IsStandardRated
                    })
                    .ToList();

            return model;
        }

        /// <summary>
        /// Turns one form row into a stored line, applying the tax rules. This is the
        /// only place a VAT amount on a purchase is calculated.
        /// </summary>
        private static VendorBillLine BuildLine(
            VendorBillLineViewModel source, int lineNumber, bool vendorCanChargeVat, decimal provisionalRate)
        {
            var quantity = source.Quantity == 0m ? 1m : source.Quantity;
            var subtotal = VatDeterminationService.Round(quantity * source.UnitPrice);

            // An unregistered supplier cannot charge tax, whatever the row says.
            var taxable = vendorCanChargeVat && source.IsTaxable;

            var vatRate = taxable ? VatDeterminationService.StandardRate : 0m;
            var vatAmount = taxable
                ? VatDeterminationService.CalculateTax(subtotal, VatCategories.Standard)
                : 0m;

            var attribution = taxable
                ? Normalise(source.InputTaxAttribution)
                : InputTaxAttributions.Taxable;

            var (recoverable, nonRecoverable, appliedRate) =
                VatDeterminationService.SplitInputTax(vatAmount, attribution, provisionalRate);

            return new VendorBillLine
            {
                LineNumber = lineNumber,
                Description = Trim(source.Description) ?? "(no description)",
                AccountId = source.AccountId,
                PropertyId = source.PropertyId,
                Quantity = quantity,
                UnitPrice = source.UnitPrice,
                LineSubtotal = subtotal,
                IsTaxable = taxable,
                VatRate = vatRate,
                VatAmount = vatAmount,
                InputTaxAttribution = attribution,
                RecoverableVatAmount = recoverable,
                NonRecoverableVatAmount = nonRecoverable,
                AppliedRecoveryRate = appliedRate,
                LineTotal = subtotal + vatAmount
            };
        }

        /// <summary>
        /// Rolls the lines up onto the header. The header figures stay authoritative for
        /// everything downstream - the VAT return, the payables ageing, the payment
        /// screen - so they must never drift from the lines that produced them.
        /// </summary>
        public static void RecalculateHeader(VendorBill bill)
        {
            bill.Subtotal = bill.Lines.Sum(x => x.LineSubtotal);
            bill.VatAmount = bill.Lines.Sum(x => x.VatAmount);
            bill.TotalAmount = bill.Subtotal + bill.VatAmount;
            bill.RecoverableVatAmount = bill.Lines.Sum(x => x.RecoverableVatAmount);
            bill.NonRecoverableVatAmount = bill.Lines.Sum(x => x.NonRecoverableVatAmount);

            // The header attribution is a summary of the lines, kept for older screens.
            var attributions = bill.Lines
                .Where(x => x.VatAmount > 0m)
                .Select(x => x.InputTaxAttribution)
                .Distinct()
                .ToList();

            bill.InputTaxAttribution = attributions.Count == 1
                ? attributions[0]
                : InputTaxAttributions.Taxable;

            bill.AppliedRecoveryRate = bill.VatAmount > 0m
                ? VatDeterminationService.Round(bill.RecoverableVatAmount / bill.VatAmount * 100m)
                : 100m;
        }

        // =====================================================================
        //  VALIDATION
        // =====================================================================

        private static void ValidateHeader(ModelStateDictionary modelState, VendorBillEditViewModel model)
        {
            if (model.BillDate == default)
                modelState.AddModelError(nameof(model.BillDate), "Enter the date on the supplier's document.");

            if (model.AccountingDate == default)
                model.AccountingDate = model.BillDate;

            if (model.DueDate == default)
                model.DueDate = model.BillDate;

            if (model.DueDate < model.BillDate)
            {
                modelState.AddModelError(nameof(model.DueDate),
                    "The due date cannot fall before the bill date.");
            }
        }

        private async Task ValidateLinesAsync(
            ModelStateDictionary modelState, int companyId, List<VendorBillLineViewModel> lines)
        {
            var accountIds = lines
                .Where(x => x.AccountId.HasValue)
                .Select(x => x.AccountId!.Value)
                .Distinct()
                .ToList();

            var postableAccounts = await _db.Accounts
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && accountIds.Contains(x.Id))
                .Select(x => new { x.Id, x.IsPostable, x.IsGroup, x.Name, x.Code })
                .ToListAsync();

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var label = $"Line {i + 1}";

                if (!line.AccountId.HasValue)
                {
                    modelState.AddModelError(string.Empty,
                        $"{label}: choose the account this cost is charged to.");
                    continue;
                }

                var account = postableAccounts.FirstOrDefault(x => x.Id == line.AccountId.Value);

                if (account == null)
                {
                    modelState.AddModelError(string.Empty, $"{label}: the selected account was not found.");
                    continue;
                }

                if (account.IsGroup || !account.IsPostable)
                {
                    modelState.AddModelError(string.Empty,
                        $"{label}: {account.Code} {account.Name} is a heading, not a postable account.");
                }

                if (line.UnitPrice < 0m)
                    modelState.AddModelError(string.Empty, $"{label}: the amount cannot be negative.");

                if (line.Quantity < 0m)
                    modelState.AddModelError(string.Empty, $"{label}: the quantity cannot be negative.");
            }
        }

        // =====================================================================
        //  NUMBERING
        // =====================================================================

        /// <summary>
        /// Our own sequence, one per company per year: BILL-2026-0001. The supplier's
        /// number goes in VendorReference; this one is ours and is never reused.
        /// </summary>
        private async Task<string> GenerateBillNumberAsync(int companyId, DateTime accountingDate)
        {
            var year = accountingDate == default ? DateTime.Today.Year : accountingDate.Year;
            var prefix = $"BILL-{year}-";

            var existing = await _db.VendorBills
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.BillNumber.StartsWith(prefix))
                .Select(x => x.BillNumber)
                .ToListAsync();

            var highest = 0;
            foreach (var code in existing)
            {
                var suffix = code[prefix.Length..];
                if (int.TryParse(suffix, out var value) && value > highest)
                    highest = value;
            }

            for (var next = highest + 1; next < highest + 1000; next++)
            {
                var candidate = $"{prefix}{next:0000}";

                var taken = await _db.VendorBills
                    .IgnoreQueryFilters()
                    .AnyAsync(x => x.CompanyId == companyId && x.BillNumber == candidate);

                if (!taken)
                    return candidate;
            }

            throw new InvalidOperationException("Could not allocate a bill number.");
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        private static string Normalise(string? attribution)
        {
            var value = Trim(attribution);
            return InputTaxAttributions.All.Contains(value, StringComparer.OrdinalIgnoreCase)
                ? InputTaxAttributions.All.First(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))
                : InputTaxAttributions.Taxable;
        }

        private static string? Trim(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
