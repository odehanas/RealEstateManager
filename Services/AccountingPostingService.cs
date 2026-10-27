using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    public class AccountingPostingService
    {
        private readonly ApplicationDbContext _db;
        private readonly VatDeterminationService _vatDeterminationService;

        public AccountingPostingService(
            ApplicationDbContext db,
            VatDeterminationService vatDeterminationService)
        {
            _db = db;
            _vatDeterminationService = vatDeterminationService;
        }

        public async Task<JournalEntry> PostCustomerInvoiceAsync(int customerInvoiceId)
        {
            var invoice = await _db.CustomerInvoices
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                .Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == customerInvoiceId);

            if (invoice == null)
                throw new InvalidOperationException("Customer invoice was not found.");

            if (invoice.JournalEntryId.HasValue)
            {
                return await _db.JournalEntries
                    .Include(x => x.Lines)
                    .FirstAsync(x => x.Id == invoice.JournalEntryId.Value);
            }

            var settings = await GetCompanyAccountSettingAsync(invoice.CompanyId);

            var receivableAccount = await GetMappedAccountAsync(
                invoice.CompanyId,
                settings.AccountsReceivableAccountId,
                "Accounts Receivable");

            var vatPayableAccount = await GetMappedAccountAsync(
                invoice.CompanyId,
                settings.VatPayableAccountId,
                "VAT Payable");

            var ownershipType = invoice.Lease!.Unit!.Property!.OwnershipType;
            var billedForOwner = invoice.Lease.Unit.Property.BillsInOwnerName;

            var offsetAccount = ownershipType == "Managed"
                ? await GetMappedAccountAsync(invoice.CompanyId, settings.DueToLandlordsAccountId, "Due To Landlords")
                : await GetMappedAccountAsync(invoice.CompanyId, settings.UnearnedRevenueAccountId, "Unearned Revenue");

            var propertyId = invoice.Lease.Unit.Property!.Id;
            var unitId = invoice.Lease.Unit.Id;
            var tenantId = invoice.Lease.Tenant!.Id;
            var leaseId = invoice.Lease.Id;

            var lines = new List<JournalEntryLine>
            {
                new()
                {
                    AccountId = receivableAccount.Id,
                    Debit = invoice.TotalAmount,
                    Credit = 0m,
                    Description = $"Tenant invoice {invoice.InvoiceNumber}",
                    PropertyId = propertyId,
                    UnitId = unitId,
                    TenantId = tenantId,
                    LeaseId = leaseId,
                    CustomerInvoiceId = invoice.Id
                }
            };

            if (invoice.Subtotal > 0)
            {
                lines.Add(new JournalEntryLine
                {
                    AccountId = offsetAccount.Id,
                    Debit = 0m,
                    Credit = invoice.Subtotal,
                    Description = ownershipType == "Managed"
                        ? "Liability to landlord from tenant billing"
                        : "Unearned revenue from tenant billing",
                    PropertyId = propertyId,
                    UnitId = unitId,
                    TenantId = tenantId,
                    LeaseId = leaseId,
                    CustomerInvoiceId = invoice.Id
                });
            }

            if (invoice.VatAmount > 0)
            {
                // Billed in the owner's name, the VAT is the owner's to pay to the FTA:
                // it is passed on to them with the rent, not declared by the company.
                lines.Add(new JournalEntryLine
                {
                    AccountId = billedForOwner ? offsetAccount.Id : vatPayableAccount.Id,
                    Debit = 0m,
                    Credit = invoice.VatAmount,
                    Description = billedForOwner
                        ? $"VAT on invoice {invoice.InvoiceNumber}, the owner's to declare"
                        : $"VAT on invoice {invoice.InvoiceNumber}",
                    PropertyId = propertyId,
                    UnitId = unitId,
                    TenantId = tenantId,
                    LeaseId = leaseId,
                    CustomerInvoiceId = invoice.Id
                });
            }

            var journalEntry = await CreateJournalEntryAsync(
                invoice.CompanyId,
                invoice.InvoiceDate,
                $"Posting customer invoice {invoice.InvoiceNumber}",
                "CustomerInvoice",
                invoice.Id,
                lines,
                journalType: "Auto",
                documentNumber: invoice.InvoiceNumber,
                referenceNumber: invoice.InvoiceNumber);

            // The link is permanent; the status moves only when the journal is posted.
            invoice.JournalEntryId = journalEntry.Id;

            // A tax invoice number is allocated only to a taxable supply - standard rated,
            // or zero rated, which is taxable at 0% and still needs a tax invoice - and
            // only once: the series must stay sequential and gap-free, so re-posting never
            // issues a second number for the same supply.
            var isTaxableSupply = invoice.VatAmount > 0m || invoice.VatCategory == VatCategories.ZeroRated;
            if (billedForOwner)
            {
                // The owner is the supplier. A registered owner's tax invoice is numbered in
                // the owner's own series for the property, never in the company's.
                var property = invoice.Lease.Unit.Property;
                invoice.IssuedForOwnerName = property.OwnerName!.Trim();
                invoice.IssuedForOwnerTrn = string.IsNullOrWhiteSpace(property.OwnerTrn) ? null : property.OwnerTrn.Trim();
                invoice.EInvoiceStatus = EInvoiceStatuses.NotRequired;
                if (isTaxableSupply && invoice.IssuedForOwnerTrn != null && string.IsNullOrWhiteSpace(invoice.TaxInvoiceNumber))
                {
                    var tenant = invoice.Lease.Tenant!;
                    invoice.RecipientTrn = tenant.TrnNumber;
                    invoice.TaxInvoiceType = VatDeterminationService.ResolveInvoiceType(invoice.VatCategory, tenant.TrnNumber);
                    invoice.DateOfSupply ??= invoice.InvoiceDate;
                    var prefix = $"OTI-{property.Code}-{invoice.InvoiceDate:yyyy}-";
                    var used = await _db.CustomerInvoices.CountAsync(x => x.TaxInvoiceNumber != null && x.TaxInvoiceNumber.StartsWith(prefix));
                    invoice.TaxInvoiceNumber = $"{prefix}{used + 1:D4}";
                }
                else if (invoice.IssuedForOwnerTrn == null)
                {
                    invoice.TaxInvoiceType = TaxInvoiceTypes.NotApplicable;
                }
            }
            else if (isTaxableSupply && string.IsNullOrWhiteSpace(invoice.TaxInvoiceNumber))
            {
                var tenant = invoice.Lease!.Tenant!;

                invoice.RecipientTrn = tenant.TrnNumber;
                invoice.TaxInvoiceType = VatDeterminationService.ResolveInvoiceType(
                    invoice.VatCategory, tenant.TrnNumber);
                invoice.DateOfSupply ??= invoice.InvoiceDate;
                invoice.TaxInvoiceNumber = await _vatDeterminationService
                    .GenerateTaxInvoiceNumberAsync(invoice.CompanyId, invoice.InvoiceDate);
            }

            await _db.SaveChangesAsync();
            return journalEntry;
        }

        /// <summary>
        /// The account an invoice credited for its net amount: the landlord liability for
        /// a managed property, unearned revenue for an owned one. A credit note reverses
        /// into the same account by default.
        /// </summary>
        public async Task<int?> GetInvoiceNetAccountIdAsync(int companyId, string ownershipType)
        {
            var settings = await GetCompanyAccountSettingAsync(companyId);
            return ownershipType == "Managed"
                ? settings.DueToLandlordsAccountId
                : settings.UnearnedRevenueAccountId;
        }

        /// <summary>
        /// Builds the journal for a credit note - the mirror of the invoice it reduces:
        ///
        ///   Dr  net account (unearned revenue / due to landlord, or the account chosen)
        ///   Dr  VAT payable                                   output tax reversed
        ///       Cr  Accounts receivable                       the tenant owes less
        ///
        /// If a DRAFT journal exists it is rebuilt, because a draft credit note may have
        /// been amended. A posted journal is returned untouched.
        /// </summary>
        public async Task<JournalEntry> PostCustomerCreditNoteAsync(int creditNoteId)
        {
            var note = await _db.CustomerCreditNotes
                .Include(x => x.JournalEntry)!.ThenInclude(x => x!.Lines)
                .Include(x => x.CustomerInvoice)!.ThenInclude(x => x!.Lease)!.ThenInclude(x => x!.Unit)!.ThenInclude(x => x!.Property)
                .FirstOrDefaultAsync(x => x.Id == creditNoteId);

            if (note?.CustomerInvoice?.Lease?.Unit?.Property == null)
                throw new InvalidOperationException("Credit note was not found.");

            if (note.JournalEntry is { IsPosted: true })
                return note.JournalEntry;

            var invoice = note.CustomerInvoice;
            var lease = invoice.Lease!;
            var settings = await GetCompanyAccountSettingAsync(note.CompanyId);

            var receivableAccount = await GetMappedAccountAsync(note.CompanyId, settings.AccountsReceivableAccountId, "Accounts Receivable");

            var netAccountId = note.DebitAccountId
                               ?? await GetInvoiceNetAccountIdAsync(note.CompanyId, lease.Unit!.Property!.OwnershipType);
            var netAccount = await GetMappedAccountAsync(note.CompanyId, netAccountId, "Credit note account");

            var propertyId = lease.Unit!.Property!.Id;
            var unitId = lease.Unit.Id;
            var tenantId = lease.TenantId;

            var lines = new List<JournalEntryLine>
            {
                new()
                {
                    AccountId = netAccount.Id,
                    Debit = note.Subtotal,
                    Credit = 0m,
                    Description = $"Credit note {note.CreditNoteNumber} against {invoice.InvoiceNumber}",
                    PropertyId = propertyId,
                    UnitId = unitId,
                    TenantId = tenantId,
                    LeaseId = lease.Id,
                    CustomerInvoiceId = invoice.Id
                }
            };

            if (note.VatAmount > 0m)
            {
                // An invoice issued in the owner's name had its VAT passed to the owner;
                // the credit takes it back from the owner, not from the company's VAT.
                var vatPayableAccount = invoice.IsIssuedForOwner
                    ? await GetMappedAccountAsync(note.CompanyId, settings.DueToLandlordsAccountId, "Due To Landlords")
                    : await GetMappedAccountAsync(note.CompanyId, settings.VatPayableAccountId, "VAT Payable");

                lines.Add(new JournalEntryLine
                {
                    AccountId = vatPayableAccount.Id,
                    Debit = note.VatAmount,
                    Credit = 0m,
                    Description = $"Output VAT reversed - credit note {note.CreditNoteNumber}",
                    PropertyId = propertyId,
                    UnitId = unitId,
                    TenantId = tenantId,
                    LeaseId = lease.Id,
                    CustomerInvoiceId = invoice.Id
                });
            }

            lines.Add(new JournalEntryLine
            {
                AccountId = receivableAccount.Id,
                Debit = 0m,
                Credit = note.TotalAmount,
                Description = $"Credit note {note.CreditNoteNumber} - {invoice.InvoiceNumber} reduced",
                PropertyId = propertyId,
                UnitId = unitId,
                TenantId = tenantId,
                LeaseId = lease.Id,
                CustomerInvoiceId = invoice.Id
            });

            // A draft from an earlier attempt is rebuilt in place from the figures the
            // credit note now carries, so the document keeps one journal for its life.
            if (note.JournalEntry != null)
            {
                var existingEntry = note.JournalEntry;
                _db.JournalEntryLines.RemoveRange(existingEntry.Lines);
                existingEntry.Lines.Clear();

                foreach (var line in lines)
                {
                    line.JournalEntryId = existingEntry.Id;
                    existingEntry.Lines.Add(line);
                }

                existingEntry.EntryDate = note.CreditNoteDate;
                existingEntry.AccountingPeriodId =
                    (await ResolveAccountingPeriodAsync(note.CompanyId, note.CreditNoteDate))?.Id;
                existingEntry.DocumentNumber = note.CreditNoteNumber;

                await _db.SaveChangesAsync();
                return existingEntry;
            }

            var journalEntry = await CreateJournalEntryAsync(
                note.CompanyId,
                note.CreditNoteDate,
                $"Posting credit note {note.CreditNoteNumber}",
                "CustomerCreditNote",
                note.Id,
                lines,
                journalType: "Auto",
                documentNumber: note.CreditNoteNumber,
                referenceNumber: invoice.TaxInvoiceNumber ?? invoice.InvoiceNumber);

            note.JournalEntryId = journalEntry.Id;
            await _db.SaveChangesAsync();
            return journalEntry;
        }

        public async Task<JournalEntry> PostCustomerReceiptAsync(int customerReceiptId)
        {
            var receipt = await _db.CustomerReceipts
                .Include(x => x.CustomerInvoice)
                    .ThenInclude(x => x!.Lease)
                        .ThenInclude(x => x!.Unit)
                            .ThenInclude(x => x!.Property)
                .Include(x => x.CustomerInvoice)
                    .ThenInclude(x => x!.Lease)
                        .ThenInclude(x => x!.Tenant)
                .Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == customerReceiptId);

            if (receipt == null)
                throw new InvalidOperationException("Customer receipt was not found.");

            if (receipt.JournalEntryId.HasValue)
            {
                return await _db.JournalEntries
                    .Include(x => x.Lines)
                    .FirstAsync(x => x.Id == receipt.JournalEntryId.Value);
            }

            var settings = await GetCompanyAccountSettingAsync(receipt.CompanyId);

            var cashAccount = await GetMappedAccountAsync(
                receipt.CompanyId,
                settings.DefaultCashAccountId,
                "Default Cash");

            var receivableAccount = await GetMappedAccountAsync(
                receipt.CompanyId,
                settings.AccountsReceivableAccountId,
                "Accounts Receivable");

            var invoice = receipt.CustomerInvoice!;
            var propertyId = invoice.Lease!.Unit!.Property!.Id;
            var unitId = invoice.Lease.Unit.Id;
            var tenantId = invoice.Lease.Tenant!.Id;
            var leaseId = invoice.Lease.Id;

            var lines = new List<JournalEntryLine>
            {
                new()
                {
                    AccountId = cashAccount.Id,
                    Debit = receipt.Amount,
                    Credit = 0m,
                    Description = $"Receipt {receipt.ReceiptNumber}",
                    PropertyId = propertyId,
                    UnitId = unitId,
                    TenantId = tenantId,
                    LeaseId = leaseId,
                    CustomerInvoiceId = invoice.Id
                },
                new()
                {
                    AccountId = receivableAccount.Id,
                    Debit = 0m,
                    Credit = receipt.Amount,
                    Description = $"Settlement of invoice {invoice.InvoiceNumber}",
                    PropertyId = propertyId,
                    UnitId = unitId,
                    TenantId = tenantId,
                    LeaseId = leaseId,
                    CustomerInvoiceId = invoice.Id
                }
            };

            var journalEntry = await CreateJournalEntryAsync(
                receipt.CompanyId,
                receipt.ReceiptDate,
                $"Posting customer receipt {receipt.ReceiptNumber}",
                "CustomerReceipt",
                receipt.Id,
                lines,
                journalType: "Auto",
                documentNumber: receipt.ReceiptNumber,
                referenceNumber: invoice.InvoiceNumber);

            // AmountPaid and the invoice status are applied when the journal is posted,
            // not here - otherwise a draft receipt would settle the invoice.
            receipt.JournalEntryId = journalEntry.Id;

            await _db.SaveChangesAsync();
            return journalEntry;
        }

        /// <summary>
        /// Builds the journal for a multi-line vendor bill: one debit per bill line to
        /// that line's own account carrying that line's property, one debit for the
        /// total recoverable input tax, and one credit to Accounts Payable.
        ///
        /// Irrecoverable input tax is never shown separately - it is added to the line
        /// it belongs to, because VAT that cannot be reclaimed is simply part of what
        /// the thing cost.
        ///
        /// If a DRAFT journal already exists it is rebuilt rather than reused. A draft
        /// bill can be edited after being reset, and reusing the old lines would post
        /// figures the document no longer shows. A POSTED journal is returned untouched.
        /// </summary>
        public async Task<JournalEntry> PostVendorBillAsync(int vendorBillId)
        {
            var bill = await _db.VendorBills
                .Include(x => x.Vendor)
                .Include(x => x.Property)
                .Include(x => x.Lines)
                .Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == vendorBillId);

            if (bill == null)
                throw new InvalidOperationException("Vendor bill was not found.");

            var existingEntry = bill.JournalEntryId.HasValue
                ? await _db.JournalEntries
                    .Include(x => x.Lines)
                    .FirstOrDefaultAsync(x => x.Id == bill.JournalEntryId.Value)
                : null;

            if (existingEntry != null && existingEntry.IsPosted)
                return existingEntry;

            if (bill.Lines.Count == 0)
                throw new InvalidOperationException($"Bill {bill.BillNumber} has no lines to post.");

            var settings = await GetCompanyAccountSettingAsync(bill.CompanyId);

            var accountsPayableAccount = await GetMappedAccountAsync(
                bill.CompanyId,
                settings.AccountsPayableAccountId,
                "Accounts Payable");

            var vatRecoverableAccount = await GetMappedAccountAsync(
                bill.CompanyId,
                settings.VatRecoverableAccountId,
                "VAT Recoverable");

            // The line amounts were settled when the bill was saved. Recomputing the
            // header here guards against a row edited directly in the database.
            VendorBillService.RecalculateHeader(bill);

            var lines = new List<JournalEntryLine>();

            foreach (var billLine in bill.Lines.OrderBy(x => x.LineNumber))
            {
                if (!billLine.AccountId.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Line {billLine.LineNumber} of bill {bill.BillNumber} has no account. " +
                        "Open the bill and choose one before posting.");
                }

                var debit = billLine.ChargedToExpense;
                if (debit == 0m)
                    continue;

                lines.Add(new JournalEntryLine
                {
                    AccountId = billLine.AccountId.Value,
                    Debit = debit,
                    Credit = 0m,
                    Description = billLine.NonRecoverableVatAmount > 0m
                        ? $"{billLine.Description} (includes {billLine.NonRecoverableVatAmount:N2} irrecoverable VAT)"
                        : billLine.Description,
                    PropertyId = billLine.PropertyId ?? bill.PropertyId,
                    UnitId = billLine.UnitId,
                    VendorId = bill.VendorId,
                    VendorBillId = bill.Id,
                    DevelopmentProjectId = billLine.DevelopmentProjectId,
                    DevelopmentCostCategory = billLine.DevelopmentCostCategory
                });
            }

            if (bill.RecoverableVatAmount > 0m)
            {
                lines.Add(new JournalEntryLine
                {
                    AccountId = vatRecoverableAccount.Id,
                    Debit = bill.RecoverableVatAmount,
                    Credit = 0m,
                    Description = $"Recoverable input tax on bill {bill.BillNumber}",
                    PropertyId = bill.PropertyId,
                    VendorId = bill.VendorId,
                    VendorBillId = bill.Id
                });
            }

            lines.Add(new JournalEntryLine
            {
                AccountId = accountsPayableAccount.Id,
                Debit = 0m,
                Credit = bill.TotalAmount,
                Description = string.IsNullOrWhiteSpace(bill.VendorReference)
                    ? $"Payable for bill {bill.BillNumber}"
                    : $"Payable for bill {bill.BillNumber} (supplier ref {bill.VendorReference})",
                PropertyId = bill.PropertyId,
                VendorId = bill.VendorId,
                VendorBillId = bill.Id
            });

            // The accounting date decides the period, not the supplier's document date.
            var entryDate = bill.AccountingDate == default ? bill.BillDate : bill.AccountingDate;

            var description = string.IsNullOrWhiteSpace(bill.VendorReference)
                ? $"Vendor bill {bill.BillNumber}"
                : $"Vendor bill {bill.BillNumber} - {bill.Vendor?.Name} ref {bill.VendorReference}";

            if (existingEntry != null)
            {
                // Rebuild the existing draft in place, so the document keeps one journal
                // for its whole life and Journal Inquiry does not fill with orphans.
                _db.JournalEntryLines.RemoveRange(existingEntry.Lines);
                existingEntry.Lines.Clear();

                foreach (var line in lines)
                {
                    line.JournalEntryId = existingEntry.Id;
                    existingEntry.Lines.Add(line);
                }

                existingEntry.EntryDate = entryDate;
                existingEntry.Description = description;
                existingEntry.AccountingPeriodId =
                    (await ResolveAccountingPeriodAsync(bill.CompanyId, entryDate))?.Id;
                existingEntry.DocumentNumber = bill.BillNumber;
                existingEntry.ReferenceNumber = bill.VendorReference ?? bill.BillNumber;
                existingEntry.Remarks = bill.Notes;

                await _db.SaveChangesAsync();
                return existingEntry;
            }

            var journalEntry = await CreateJournalEntryAsync(
                bill.CompanyId,
                entryDate,
                description,
                "VendorBill",
                bill.Id,
                lines,
                journalType: "Auto",
                documentNumber: bill.BillNumber,
                referenceNumber: bill.VendorReference ?? bill.BillNumber,
                remarks: bill.Notes);

            bill.JournalEntryId = journalEntry.Id;

            await _db.SaveChangesAsync();
            return journalEntry;
        }

        /// <summary>
        /// A payment to a vendor. Paying open bills (SAP F-53):
        ///
        ///   Dr  Accounts Payable        one line per bill, tagged with the bill and vendor
        ///       Cr  Bank / Cash         the account paid from
        ///
        /// An advance / down payment (SAP F-48, special G/L indicator A) is kept apart from
        /// Accounts Payable until applied to bills:
        ///
        ///   Dr  11550 Advances to Suppliers   tagged with the vendor
        ///       Cr  Bank / Cash
        /// </summary>
        public async Task<JournalEntry> PostVendorPaymentAsync(int vendorPaymentId)
        {
            var payment = await _db.VendorPayments
                .Include(x => x.VendorBill)
                .Include(x => x.Vendor)
                .Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == vendorPaymentId);

            if (payment == null)
                throw new InvalidOperationException("Vendor payment was not found.");

            if (payment.JournalEntryId.HasValue)
            {
                return await _db.JournalEntries
                    .Include(x => x.Lines)
                    .FirstAsync(x => x.Id == payment.JournalEntryId.Value);
            }

            var settings = await GetCompanyAccountSettingAsync(payment.CompanyId);

            var cashAccount = payment.PaidFromAccountId.HasValue
                ? await _db.Accounts.FirstAsync(x => x.Id == payment.PaidFromAccountId.Value)
                : await GetMappedAccountAsync(payment.CompanyId, settings.DefaultCashAccountId, "Default Cash");

            var vendorId = payment.VendorId ?? payment.VendorBill?.VendorId
                ?? throw new InvalidOperationException($"Payment {payment.PaymentNumber} has no vendor.");

            var lines = new List<JournalEntryLine>();
            string reference;

            if (payment.IsAdvance)
            {
                var advances = await GetSupplierAdvancesAccountAsync(payment.CompanyId);
                lines.Add(new JournalEntryLine
                {
                    AccountId = advances.Id,
                    Debit = payment.Amount,
                    Description = $"Advance (down payment) {payment.PaymentNumber}" + (payment.Vendor != null ? $" to {payment.Vendor.Name}" : string.Empty),
                    VendorId = vendorId
                });
                reference = "Advance";
            }
            else
            {
                var accountsPayableAccount = await GetMappedAccountAsync(
                    payment.CompanyId,
                    settings.AccountsPayableAccountId,
                    "Accounts Payable");

                var allocations = await _db.VendorClearingLines
                    .Include(x => x.VendorBill)
                    .Where(x => x.VendorClearing!.VendorPaymentId == payment.Id && x.VendorClearing.ClearingType == VendorClearingTypes.Payment)
                    .ToListAsync();

                // A payment recorded before clearing documents pays its one bill.
                var applied = allocations.Count > 0
                    ? allocations.Select(x => (Bill: x.VendorBill!, x.Amount)).ToList()
                    : new List<(VendorBill Bill, decimal Amount)>
                    {
                        (payment.VendorBill ?? throw new InvalidOperationException($"Payment {payment.PaymentNumber} is not applied to any bill."), payment.Amount)
                    };

                foreach (var (bill, amount) in applied)
                {
                    lines.Add(new JournalEntryLine
                    {
                        AccountId = accountsPayableAccount.Id,
                        Debit = amount,
                        Description = $"Settlement of vendor bill {bill.BillNumber}" + (bill.VendorReference != null ? $" ({bill.VendorReference})" : string.Empty),
                        PropertyId = bill.PropertyId,
                        VendorId = vendorId,
                        VendorBillId = bill.Id
                    });
                }

                reference = payment.ClearingNumber ?? string.Join(", ", applied.Select(x => x.Bill.BillNumber));
            }

            lines.Add(new JournalEntryLine
            {
                AccountId = cashAccount.Id,
                Credit = payment.Amount,
                Description = $"Vendor payment {payment.PaymentNumber}" + (string.IsNullOrWhiteSpace(payment.Reference) ? string.Empty : $" ref {payment.Reference}"),
                PropertyId = lines.Count == 1 ? lines[0].PropertyId : null,
                VendorId = vendorId,
                VendorBillId = lines.Count == 1 ? lines[0].VendorBillId : null
            });

            var journalEntry = await CreateJournalEntryAsync(
                payment.CompanyId,
                payment.PaymentDate,
                payment.IsAdvance ? $"Advance to vendor {payment.PaymentNumber}" : $"Posting vendor payment {payment.PaymentNumber}",
                "VendorPayment",
                payment.Id,
                lines,
                journalType: "Auto",
                documentNumber: payment.PaymentNumber,
                referenceNumber: reference.Length > 100 ? reference[..100] : reference);

            // Settlement of the bills is applied when the journal is posted.
            payment.JournalEntryId = journalEntry.Id;

            await _db.SaveChangesAsync();
            return journalEntry;
        }

        /// <summary>
        /// An advance applied to the vendor's bills (SAP F-54). No money moves: the advance
        /// already paid settles the bills.
        ///
        ///   Dr  Accounts Payable                one line per bill
        ///       Cr  11550 Advances to Suppliers
        /// </summary>
        public async Task<JournalEntry> PostVendorAdvanceClearingAsync(int vendorClearingId)
        {
            var clearing = await _db.VendorClearings
                .Include(x => x.Lines).ThenInclude(x => x.VendorBill)
                .Include(x => x.AdvancePayment)
                .FirstOrDefaultAsync(x => x.Id == vendorClearingId)
                ?? throw new InvalidOperationException("The clearing document was not found.");

            if (clearing.JournalEntryId.HasValue)
                return await _db.JournalEntries.Include(x => x.Lines).FirstAsync(x => x.Id == clearing.JournalEntryId.Value);

            var settings = await GetCompanyAccountSettingAsync(clearing.CompanyId);
            var accountsPayableAccount = await GetMappedAccountAsync(clearing.CompanyId, settings.AccountsPayableAccountId, "Accounts Payable");
            var advances = await GetSupplierAdvancesAccountAsync(clearing.CompanyId);

            var lines = clearing.Lines.Select(x => new JournalEntryLine
            {
                AccountId = accountsPayableAccount.Id,
                Debit = x.Amount,
                Description = $"Bill {x.VendorBill!.BillNumber} settled from advance {clearing.AdvancePayment?.PaymentNumber}",
                PropertyId = x.VendorBill.PropertyId,
                VendorId = clearing.VendorId,
                VendorBillId = x.VendorBillId
            }).ToList();

            lines.Add(new JournalEntryLine
            {
                AccountId = advances.Id,
                Credit = clearing.Lines.Sum(x => x.Amount),
                Description = $"Advance {clearing.AdvancePayment?.PaymentNumber} applied ({clearing.ClearingNumber})",
                VendorId = clearing.VendorId
            });

            var journalEntry = await CreateJournalEntryAsync(
                clearing.CompanyId,
                clearing.ClearingDate,
                $"Advance applied to bills {clearing.ClearingNumber}",
                VendorClearingService.AdvanceSourceType,
                clearing.Id,
                lines,
                journalType: "Auto",
                documentNumber: clearing.ClearingNumber,
                referenceNumber: clearing.AdvancePayment?.PaymentNumber);

            clearing.JournalEntryId = journalEntry.Id;
            await _db.SaveChangesAsync();
            return journalEntry;
        }

        /// <summary>11550 Advances to Suppliers: where down payments wait until applied to bills.</summary>
        private async Task<Account> GetSupplierAdvancesAccountAsync(int companyId) =>
            await _db.Accounts.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Code == VendorClearingService.SupplierAdvancesAccountCode)
            ?? throw new InvalidOperationException(
                $"Account {VendorClearingService.SupplierAdvancesAccountCode} Advances to Suppliers is missing from the chart of accounts. Add it under Assets to pay vendor advances.");

        /// <summary>
        /// A cost capitalised into a development project without a supplier bill - the land
        /// paid from the bank, an authority fee, borrowing costs moved out of finance costs:
        ///
        ///   Dr  the project's work-in-progress account    tagged with project and category
        ///       Cr  the account chosen (bank, accrual, finance costs...)
        /// </summary>
        public async Task<JournalEntry> PostDevelopmentCostAsync(
            DevelopmentProject project,
            DateTime costDate,
            string costCategory,
            string description,
            decimal amount,
            int creditAccountId,
            string? reference)
        {
            var wipAccount = await GetMappedAccountAsync(project.CompanyId, project.WipAccountId, "Project work in progress");
            var creditAccount = await GetMappedAccountAsync(project.CompanyId, creditAccountId, "Account credited");

            var lines = new List<JournalEntryLine>
            {
                new()
                {
                    AccountId = wipAccount.Id,
                    Debit = amount,
                    Credit = 0m,
                    Description = description,
                    PropertyId = project.PropertyId,
                    DevelopmentProjectId = project.Id,
                    DevelopmentCostCategory = costCategory
                },
                new()
                {
                    AccountId = creditAccount.Id,
                    Debit = 0m,
                    Credit = amount,
                    Description = $"{project.Code} {project.Name} - {description}",
                    PropertyId = project.PropertyId
                }
            };

            return await CreateJournalEntryAsync(
                project.CompanyId,
                costDate,
                $"Development cost {project.Code} - {description}",
                "DevelopmentCost",
                project.Id,
                lines,
                journalType: "Auto",
                documentNumber: reference,
                referenceNumber: project.Code);
        }

        /// <summary>Records a journal whose lines another service has built (unit sales, capital asset VAT).</summary>
        public Task<JournalEntry> CreatePreparedJournalAsync(
            int companyId, DateTime date, string description, string sourceType, int sourceId,
            List<JournalEntryLine> lines, string? documentNumber, string? referenceNumber = null)
        {
            return CreateJournalEntryAsync(companyId, date, description, sourceType, sourceId, lines,
                journalType: "Auto", documentNumber: documentNumber, referenceNumber: referenceNumber);
        }

        /// <summary>Allocates opening investment property to one building (built by InvestmentPropertySetupService).</summary>
        public Task<JournalEntry> CreateInvestmentPropertySetupJournalAsync(
            int companyId, DateTime date, Property property, List<JournalEntryLine> lines)
        {
            return CreateJournalEntryAsync(
                companyId,
                date,
                $"Investment property allocated to {property.Code} - {property.Name}",
                InvestmentPropertySetupService.SourceType,
                property.Id,
                lines,
                journalType: "Auto",
                documentNumber: property.Code);
        }

        /// <summary>
        /// The journal that moves a completed project's cost out of work in progress. The
        /// lines are built by <see cref="DevelopmentTransferService"/>, which knows the
        /// allocation to units; this only records them.
        /// </summary>
        public Task<JournalEntry> CreateDevelopmentTransferJournalAsync(
            DevelopmentTransfer transfer, DevelopmentProject project, List<JournalEntryLine> lines)
        {
            return CreateJournalEntryAsync(
                transfer.CompanyId,
                transfer.TransferDate,
                $"Completion transfer {project.Code} - {project.Name}",
                "DevelopmentTransfer",
                transfer.Id,
                lines,
                journalType: "Auto",
                documentNumber: project.Code,
                referenceNumber: project.CompletionCertificateNumber);
        }

        /// <summary>
        /// Builds the journal for the VAT on a commission tax invoice to a landlord. The
        /// commission itself is already in revenue through revenue recognition; this adds
        /// the output tax on it, paid out of the owner's money:
        ///
        ///   Dr  Due to Landlords        one line per property
        ///       Cr  VAT Payable
        /// </summary>
        public async Task<JournalEntry> PostOwnerCommissionVatAsync(int ownerCommissionInvoiceId)
        {
            var invoice = await _db.OwnerCommissionInvoices
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == ownerCommissionInvoiceId)
                ?? throw new InvalidOperationException("Commission invoice was not found.");

            if (invoice.JournalEntryId.HasValue)
            {
                return await _db.JournalEntries
                    .Include(x => x.Lines)
                    .FirstAsync(x => x.Id == invoice.JournalEntryId.Value);
            }

            var settings = await GetCompanyAccountSettingAsync(invoice.CompanyId);
            var dueToLandlords = await GetMappedAccountAsync(
                invoice.CompanyId, settings.DueToLandlordsAccountId, "Due To Landlords");
            var vatPayable = await GetMappedAccountAsync(
                invoice.CompanyId, settings.VatPayableAccountId, "VAT Payable");

            // The VAT is worked out on the invoice total and shared across the properties,
            // the last one taking the rounding, so the lines always add up to the invoice.
            var byProperty = invoice.Lines
                .GroupBy(x => x.PropertyId)
                .Select(g => new { PropertyId = g.Key, Commission = g.Sum(x => x.Commission) })
                .ToList();

            var lines = new List<JournalEntryLine>();
            var allocated = 0m;
            for (var i = 0; i < byProperty.Count; i++)
            {
                var share = i == byProperty.Count - 1 || invoice.Commission == 0m
                    ? invoice.VatAmount - allocated
                    : VatDeterminationService.Round(invoice.VatAmount * byProperty[i].Commission / invoice.Commission);
                allocated += share;

                lines.Add(new JournalEntryLine
                {
                    AccountId = dueToLandlords.Id,
                    Debit = share,
                    Credit = 0m,
                    Description = $"VAT on commission {invoice.TaxInvoiceNumber ?? invoice.InvoiceNumber} - {invoice.OwnerName}",
                    PropertyId = byProperty[i].PropertyId
                });
            }

            lines.Add(new JournalEntryLine
            {
                AccountId = vatPayable.Id,
                Debit = 0m,
                Credit = invoice.VatAmount,
                Description = $"Output VAT on management commission {invoice.TaxInvoiceNumber ?? invoice.InvoiceNumber}"
            });

            var journalEntry = await CreateJournalEntryAsync(
                invoice.CompanyId,
                invoice.InvoiceDate,
                $"Commission tax invoice {invoice.TaxInvoiceNumber ?? invoice.InvoiceNumber} - {invoice.OwnerName}",
                "OwnerCommissionInvoice",
                invoice.Id,
                lines,
                journalType: "Auto",
                documentNumber: invoice.TaxInvoiceNumber ?? invoice.InvoiceNumber,
                referenceNumber: invoice.InvoiceNumber);

            invoice.JournalEntryId = journalEntry.Id;
            await _db.SaveChangesAsync();
            return journalEntry;
        }

        /// <summary>
        /// Builds the journal for a payment to the landlord of a managed property:
        ///
        ///   Dr  Due to Landlords        the liability to the owner falls
        ///       Cr  Bank / Cash         the money leaves
        /// </summary>
        public async Task<JournalEntry> PostOwnerSettlementAsync(int ownerSettlementId, int? propertyId)
        {
            var settlement = await _db.OwnerSettlements
                .FirstOrDefaultAsync(x => x.Id == ownerSettlementId)
                ?? throw new InvalidOperationException("Owner settlement was not found.");

            if (settlement.JournalEntryId.HasValue)
            {
                return await _db.JournalEntries
                    .Include(x => x.Lines)
                    .FirstAsync(x => x.Id == settlement.JournalEntryId.Value);
            }

            var settings = await GetCompanyAccountSettingAsync(settlement.CompanyId);
            var dueToLandlords = await GetMappedAccountAsync(
                settlement.CompanyId, settings.DueToLandlordsAccountId, "Due To Landlords");
            var paidFrom = await GetMappedAccountAsync(
                settlement.CompanyId, settlement.PaidFromAccountId, "Paid from account");

            var lines = new List<JournalEntryLine>
            {
                new()
                {
                    AccountId = dueToLandlords.Id,
                    Debit = settlement.AmountPaid,
                    Credit = 0m,
                    Description = $"Settlement {settlement.SettlementNumber} to {settlement.OwnerName}",
                    PropertyId = propertyId
                },
                new()
                {
                    AccountId = paidFrom.Id,
                    Debit = 0m,
                    Credit = settlement.AmountPaid,
                    Description = $"Paid to {settlement.OwnerName} - {settlement.PaymentMethod}",
                    PropertyId = propertyId
                }
            };

            var journalEntry = await CreateJournalEntryAsync(
                settlement.CompanyId,
                settlement.SettlementDate,
                $"Owner settlement {settlement.SettlementNumber} - {settlement.OwnerName}",
                "OwnerSettlement",
                settlement.Id,
                lines,
                journalType: "Auto",
                documentNumber: settlement.SettlementNumber,
                referenceNumber: settlement.ReferenceNumber,
                remarks: settlement.Notes);

            settlement.JournalEntryId = journalEntry.Id;
            await _db.SaveChangesAsync();
            return journalEntry;
        }

        public async Task<JournalEntry> PostSecurityDepositAsync(int securityDepositId)
        {
            var deposit = await _db.SecurityDeposits
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == securityDepositId);

            if (deposit == null)
                throw new InvalidOperationException("Security deposit was not found.");

            if (deposit.JournalEntryId.HasValue)
            {
                return await _db.JournalEntries
                    .Include(x => x.Lines)
                    .FirstAsync(x => x.Id == deposit.JournalEntryId.Value);
            }

            var settings = await GetCompanyAccountSettingAsync(deposit.CompanyId);

            var cashAccount = await GetMappedAccountAsync(
                deposit.CompanyId,
                settings.DefaultCashAccountId,
                "Default Cash");

            var depositLiabilityAccount = await GetMappedAccountAsync(
                deposit.CompanyId,
                settings.TenantSecurityDepositLiabilityAccountId,
                "Tenant Security Deposit Liability");

            var lease = deposit.Lease!;
            var propertyId = lease.Unit!.Property!.Id;
            var unitId = lease.Unit.Id;
            var tenantId = lease.Tenant!.Id;
            var leaseId = lease.Id;

            var lines = new List<JournalEntryLine>
            {
                new()
                {
                    AccountId = cashAccount.Id,
                    Debit = deposit.Amount,
                    Credit = 0m,
                    Description = $"Security deposit receipt {deposit.ReferenceNumber}",
                    PropertyId = propertyId,
                    UnitId = unitId,
                    TenantId = tenantId,
                    LeaseId = leaseId,
                    SecurityDepositId = deposit.Id
                },
                new()
                {
                    AccountId = depositLiabilityAccount.Id,
                    Debit = 0m,
                    Credit = deposit.Amount,
                    Description = $"Security deposit liability for lease {lease.ContractNumber}",
                    PropertyId = propertyId,
                    UnitId = unitId,
                    TenantId = tenantId,
                    LeaseId = leaseId,
                    SecurityDepositId = deposit.Id
                }
            };

            var journalEntry = await CreateJournalEntryAsync(
                deposit.CompanyId,
                deposit.ReceiptDate,
                $"Posting security deposit {deposit.ReferenceNumber}",
                "SecurityDeposit",
                deposit.Id,
                lines,
                journalType: "Auto",
                documentNumber: deposit.ReferenceNumber,
                referenceNumber: lease.ContractNumber);

            deposit.JournalEntryId = journalEntry.Id;
            await _db.SaveChangesAsync();

            return journalEntry;
        }

        public async Task<JournalEntry> PostRevenueRecognitionAsync(int scheduleId)
        {
            var schedule = await _db.RevenueRecognitionSchedules
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Tenant)
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == scheduleId);

            if (schedule == null)
                throw new InvalidOperationException("Revenue recognition schedule was not found.");

            if (schedule.JournalEntryId.HasValue)
            {
                return await _db.JournalEntries
                    .Include(x => x.Lines)
                    .FirstAsync(x => x.Id == schedule.JournalEntryId.Value);
            }

            var settings = await GetCompanyAccountSettingAsync(schedule.CompanyId);

            var lease = schedule.Lease!;
            var property = lease.Unit!.Property!;
            var propertyId = property.Id;
            var unitId = lease.Unit.Id;
            var tenantId = lease.Tenant!.Id;
            var leaseId = lease.Id;

            var lines = new List<JournalEntryLine>();

            if (property.OwnershipType == "Managed")
            {
                var dueToLandlordsAccount = await GetMappedAccountAsync(
                    schedule.CompanyId,
                    settings.DueToLandlordsAccountId,
                    "Due To Landlords");

                var managementRevenueAccount = await GetMappedAccountAsync(
                    schedule.CompanyId,
                    settings.ManagementFeeRevenueAccountId,
                    "Management Fee Revenue");

                lines.Add(new JournalEntryLine
                {
                    AccountId = dueToLandlordsAccount.Id,
                    Debit = schedule.Amount,
                    Credit = 0m,
                    Description = "Recognition of management commission",
                    PropertyId = propertyId,
                    UnitId = unitId,
                    TenantId = tenantId,
                    LeaseId = leaseId
                });

                lines.Add(new JournalEntryLine
                {
                    AccountId = managementRevenueAccount.Id,
                    Debit = 0m,
                    Credit = schedule.Amount,
                    Description = "Management fee revenue recognized",
                    PropertyId = propertyId,
                    UnitId = unitId,
                    TenantId = tenantId,
                    LeaseId = leaseId
                });
            }
            else
            {
                var unearnedRevenueAccount = await GetMappedAccountAsync(
                    schedule.CompanyId,
                    settings.UnearnedRevenueAccountId,
                    "Unearned Revenue");

                var rentalRevenueAccount = await GetMappedAccountAsync(
                    schedule.CompanyId,
                    settings.RentalRevenueOwnedAccountId,
                    "Rental Revenue - Owned");

                lines.Add(new JournalEntryLine
                {
                    AccountId = unearnedRevenueAccount.Id,
                    Debit = schedule.Amount,
                    Credit = 0m,
                    Description = "Release of unearned revenue",
                    PropertyId = propertyId,
                    UnitId = unitId,
                    TenantId = tenantId,
                    LeaseId = leaseId
                });

                lines.Add(new JournalEntryLine
                {
                    AccountId = rentalRevenueAccount.Id,
                    Debit = 0m,
                    Credit = schedule.Amount,
                    Description = "Rental revenue recognized",
                    PropertyId = propertyId,
                    UnitId = unitId,
                    TenantId = tenantId,
                    LeaseId = leaseId
                });
            }

            var journalEntry = await CreateJournalEntryAsync(
                schedule.CompanyId,
                schedule.RecognitionDate,
                $"Posting revenue recognition schedule #{schedule.Id}",
                "RevenueRecognitionSchedule",
                schedule.Id,
                lines,
                journalType: "Auto");

            // Status moves to Posted when the journal is posted, not here.
            schedule.JournalEntryId = journalEntry.Id;

            await _db.SaveChangesAsync();
            return journalEntry;
        }

        public async Task<int> ProcessRevenueRecognitionUpToAsync(DateTime cutoffDate)
        {
            var scheduleIds = await _db.RevenueRecognitionSchedules
                .Where(x =>
                    !x.JournalEntryId.HasValue &&
                    x.Status == "Planned" &&
                    x.RecognitionDate <= cutoffDate.Date)
                .OrderBy(x => x.RecognitionDate)
                .ThenBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var id in scheduleIds)
            {
                await PostRevenueRecognitionAsync(id);
            }

            return scheduleIds.Count;
        }

        public async Task<int> CreatePrepaymentScheduleAsync(int companyId, PrepaymentCreateViewModel model)
        {
            var settings = await GetCompanyAccountSettingAsync(companyId);

            if (model.TotalAmount <= 0m)
                throw new InvalidOperationException("Prepayment amount must be greater than zero.");

            if (model.RecognitionMonths <= 0)
                throw new InvalidOperationException("Recognition months must be greater than zero.");

            if (!model.PrepaidAccountId.HasValue)
                throw new InvalidOperationException("Prepaid account is required.");

            if (!model.ExpenseAccountId.HasValue)
                throw new InvalidOperationException("Expense account is required.");

            if (!model.OffsetAccountId.HasValue)
                throw new InvalidOperationException("Paid / payable account is required.");

            var prepaidAccount = await GetMappedAccountAsync(companyId, model.PrepaidAccountId, "Prepaid Account");
            var expenseAccount = await GetMappedAccountAsync(companyId, model.ExpenseAccountId, "Expense Account");
            var offsetAccount = await GetMappedAccountAsync(companyId, model.OffsetAccountId, "Paid / Payable Account");
            var usesAccountsPayable = settings.AccountsPayableAccountId == offsetAccount.Id;

            if (usesAccountsPayable && !model.VendorId.HasValue)
                throw new InvalidOperationException("Vendor is required when the paid / payable account is Accounts Payable.");

            var scheduleNumber = string.IsNullOrWhiteSpace(model.ScheduleNumber)
                ? await GeneratePrepaymentScheduleNumberAsync()
                : model.ScheduleNumber.Trim();

            var exists = await _db.PrepaymentSchedules.AnyAsync(x => x.CompanyId == companyId && x.ScheduleNumber == scheduleNumber);
            if (exists)
                throw new InvalidOperationException("Schedule number already exists.");

            var schedule = new PrepaymentSchedule
            {
                CompanyId = companyId,
                ScheduleNumber = scheduleNumber,
                Description = model.Description.Trim(),
                PrepaymentType = string.IsNullOrWhiteSpace(model.PrepaymentType) ? "PrepaidExpense" : model.PrepaymentType.Trim(),
                PaymentDate = model.PaymentDate.Date,
                FirstRecognitionDate = NormalizeToMonthEnd(model.FirstRecognitionDate.Date),
                TotalAmount = model.TotalAmount,
                RecognitionMonths = model.RecognitionMonths,
                PrepaidAccountId = prepaidAccount.Id,
                ExpenseAccountId = expenseAccount.Id,
                OffsetAccountId = offsetAccount.Id,
                VendorId = usesAccountsPayable ? model.VendorId : null,
                Status = "Active",
                ReferenceNumber = string.IsNullOrWhiteSpace(model.ReferenceNumber) ? null : model.ReferenceNumber.Trim(),
                Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _db.PrepaymentSchedules.Add(schedule);
            await _db.SaveChangesAsync();

            VendorBill? vendorBill = null;
            if (usesAccountsPayable)
            {
                vendorBill = await CreateAutoVendorBillAsync(
                    companyId,
                    model.VendorId!.Value,
                    propertyId: null,
                    billDate: model.PaymentDate.Date,
                    amount: model.TotalAmount,
                    expenseCategory: "Prepayment",
                    sourceCode: schedule.ScheduleNumber,
                    referenceNumber: schedule.ReferenceNumber);

                schedule.VendorBillId = vendorBill.Id;
            }

            var initialLines = new List<JournalEntryLine>
            {
                new()
                {
                    AccountId = prepaidAccount.Id,
                    Debit = model.TotalAmount,
                    Credit = 0m,
                    Description = $"Prepayment setup {schedule.ScheduleNumber}",
                    VendorId = vendorBill?.VendorId,
                    VendorBillId = vendorBill?.Id
                },
                new()
                {
                    AccountId = offsetAccount.Id,
                    Debit = 0m,
                    Credit = model.TotalAmount,
                    Description = $"Initial payment for {schedule.ScheduleNumber}",
                    VendorId = vendorBill?.VendorId,
                    VendorBillId = vendorBill?.Id
                }
            };

            var initialJournal = await CreateJournalEntryAsync(
                companyId,
                model.PaymentDate.Date,
                $"Prepayment setup {schedule.ScheduleNumber}",
                "PrepaymentSchedule",
                schedule.Id,
                initialLines,
                journalType: "Auto",
                documentNumber: schedule.ScheduleNumber,
                referenceNumber: schedule.ReferenceNumber);

            schedule.InitialJournalEntryId = initialJournal.Id;
            if (vendorBill != null)
            {
                vendorBill.JournalEntryId = initialJournal.Id;
            }

            var monthlyAmount = Math.Round(model.TotalAmount / model.RecognitionMonths, 2, MidpointRounding.AwayFromZero);
            var accumulated = 0m;

            for (var i = 0; i < model.RecognitionMonths; i++)
            {
                var recognitionMonth = model.FirstRecognitionDate.Date.AddMonths(i);
                var recognitionDate = NormalizeToMonthEnd(recognitionMonth);
                var amount = i == model.RecognitionMonths - 1
                    ? model.TotalAmount - accumulated
                    : monthlyAmount;

                accumulated += amount;

                _db.PrepaymentScheduleLines.Add(new PrepaymentScheduleLine
                {
                    PrepaymentScheduleId = schedule.Id,
                    RecognitionDate = recognitionDate,
                    Amount = amount,
                    Status = "Planned"
                });
            }

            await _db.SaveChangesAsync();
            return schedule.Id;
        }

        public async Task<PrepaymentScheduleListViewModel> GetPrepaymentSchedulesAsync(DateTime asOfDate, bool showPosted)
        {
            var query = _db.PrepaymentSchedules
                .AsNoTracking()
                .Include(x => x.PrepaidAccount)
                .Include(x => x.ExpenseAccount)
                .Include(x => x.OffsetAccount)
                .Include(x => x.Vendor)
                .Include(x => x.VendorBill)
                .Include(x => x.InitialJournalEntry)
                .Include(x => x.Lines)
                .AsQueryable();

            if (!showPosted)
            {
                query = query.Where(x => x.Status != "Completed");
            }

            var schedules = await query
                .OrderByDescending(x => x.PaymentDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            var rows = schedules.Select(x =>
            {
                var recognized = x.Lines.Where(l => l.Status == "Posted").Sum(l => l.Amount);
                var dueLines = x.Lines.Where(l => l.Status == "Planned" && l.RecognitionDate <= asOfDate.Date).ToList();

                return new PrepaymentScheduleListItemViewModel
                {
                    PrepaymentScheduleId = x.Id,
                    ScheduleNumber = x.ScheduleNumber,
                    Description = x.Description,
                    PrepaymentType = x.PrepaymentType,
                    PaymentDate = x.PaymentDate,
                    FirstRecognitionDate = x.FirstRecognitionDate,
                    TotalAmount = x.TotalAmount,
                    RecognizedAmount = recognized,
                    RemainingAmount = x.TotalAmount - recognized,
                    RecognitionMonths = x.RecognitionMonths,
                    Status = x.Status,
                    PrepaidAccountDisplay = x.PrepaidAccount != null ? $"{x.PrepaidAccount.Code} - {x.PrepaidAccount.Name}" : "-",
                    ExpenseAccountDisplay = x.ExpenseAccount != null ? $"{x.ExpenseAccount.Code} - {x.ExpenseAccount.Name}" : "-",
                    OffsetAccountDisplay = x.OffsetAccount != null ? $"{x.OffsetAccount.Code} - {x.OffsetAccount.Name}" : "-",
                    VendorDisplay = x.Vendor != null ? $"{x.Vendor.Code} - {x.Vendor.Name}" : null,
                    VendorBillNumber = x.VendorBill?.BillNumber,
                    InitialJournalEntryNumber = x.InitialJournalEntry?.EntryNumber,
                    DueLineCount = dueLines.Count,
                    DueLineAmount = dueLines.Sum(l => l.Amount)
                };
            }).ToList();

            return new PrepaymentScheduleListViewModel
            {
                AsOfDate = asOfDate.Date,
                ShowPosted = showPosted,
                OpenPrepaidBalance = rows.Sum(x => x.RemainingAmount),
                DueRecognitionAmount = rows.Sum(x => x.DueLineAmount),
                DueLineCount = rows.Sum(x => x.DueLineCount),
                Schedules = rows
            };
        }

        public async Task<JournalEntry> PostPrepaymentRecognitionAsync(int scheduleLineId)
        {
            var line = await _db.PrepaymentScheduleLines
                .Include(x => x.PrepaymentSchedule)
                    .ThenInclude(x => x!.PrepaidAccount)
                .Include(x => x.PrepaymentSchedule)
                    .ThenInclude(x => x!.ExpenseAccount)
                .Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == scheduleLineId);

            if (line == null)
                throw new InvalidOperationException("Prepayment schedule line was not found.");

            if (line.JournalEntryId.HasValue)
            {
                return await _db.JournalEntries
                    .Include(x => x.Lines)
                    .FirstAsync(x => x.Id == line.JournalEntryId.Value);
            }

            var schedule = line.PrepaymentSchedule!;
            var prepaidAccount = await GetMappedAccountAsync(schedule.CompanyId, schedule.PrepaidAccountId, "Prepaid Account");
            var expenseAccount = await GetMappedAccountAsync(schedule.CompanyId, schedule.ExpenseAccountId, "Expense Account");

            var lines = new List<JournalEntryLine>
            {
                new()
                {
                    AccountId = expenseAccount.Id,
                    Debit = line.Amount,
                    Credit = 0m,
                    Description = $"Prepayment recognition {schedule.ScheduleNumber}"
                },
                new()
                {
                    AccountId = prepaidAccount.Id,
                    Debit = 0m,
                    Credit = line.Amount,
                    Description = $"Release prepaid balance {schedule.ScheduleNumber}"
                }
            };

            var journalEntry = await CreateJournalEntryAsync(
                schedule.CompanyId,
                line.RecognitionDate,
                $"Posting prepayment recognition {schedule.ScheduleNumber}",
                "PrepaymentRecognition",
                line.Id,
                lines,
                journalType: "Auto",
                documentNumber: schedule.ScheduleNumber,
                referenceNumber: schedule.ReferenceNumber);

            line.JournalEntryId = journalEntry.Id;
            line.Status = "Posted";
            line.PostedAt = DateTime.UtcNow;

            var remainingPlanned = await _db.PrepaymentScheduleLines.AnyAsync(x =>
                x.PrepaymentScheduleId == schedule.Id &&
                x.Status == "Planned");

            if (!remainingPlanned)
            {
                schedule.Status = "Completed";
            }

            await _db.SaveChangesAsync();
            return journalEntry;
        }

        public async Task<int> ProcessPrepaymentRecognitionsUpToAsync(DateTime cutoffDate)
        {
            var lineIds = await _db.PrepaymentScheduleLines
                .Where(x =>
                    !x.JournalEntryId.HasValue &&
                    x.Status == "Planned" &&
                    x.RecognitionDate <= cutoffDate.Date)
                .OrderBy(x => x.RecognitionDate)
                .ThenBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var id in lineIds)
            {
                await PostPrepaymentRecognitionAsync(id);
            }

            return lineIds.Count;
        }

        private async Task<CompanyAccountSetting> GetCompanyAccountSettingAsync(int companyId)
        {
            var setting = await _db.CompanyAccountSettings
                .FirstOrDefaultAsync(x => x.CompanyId == companyId);

            if (setting == null)
                throw new InvalidOperationException("Company account mapping setup was not found.");

            return setting;
        }

        private async Task<Account> GetMappedAccountAsync(int companyId, int? accountId, string mappingName)
        {
            if (!accountId.HasValue)
                throw new InvalidOperationException($"Account mapping for '{mappingName}' is not configured.");

            var account = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == accountId.Value);

            if (account == null)
                throw new InvalidOperationException($"Mapped account for '{mappingName}' was not found.");

            if (!account.IsPostable)
                throw new InvalidOperationException($"Mapped account for '{mappingName}' must be a postable account.");

            if (!account.IsActive)
                throw new InvalidOperationException($"Mapped account for '{mappingName}' is inactive.");

            return account;
        }

        private async Task<AccountingPeriod?> ResolveAccountingPeriodAsync(int companyId, DateTime entryDate)
        {
            return await _db.AccountingPeriods.FirstOrDefaultAsync(x =>
                x.CompanyId == companyId &&
                x.StartDate <= entryDate.Date &&
                x.EndDate >= entryDate.Date);
        }

        /// <summary>
        /// Material taken from the maintenance store for a job:
        ///   Dr  Due to Landlords (managed property: the owner bears it)
        ///       or Property Maintenance (the company's own property)
        ///   Cr  Maintenance Materials Stock
        /// </summary>
        public async Task<JournalEntry> PostMaintenanceMaterialAsync(int materialId)
        {
            var material = await _db.MaintenanceMaterials
                .Include(x => x.ServiceRequest).ThenInclude(x => x!.Property)
                .FirstOrDefaultAsync(x => x.Id == materialId)
                ?? throw new InvalidOperationException("The material was not found.");

            if (material.JournalEntryId.HasValue)
                return await _db.JournalEntries.Include(x => x.Lines).FirstAsync(x => x.Id == material.JournalEntryId.Value);

            var job = material.ServiceRequest!;
            var stock = await _db.Accounts.FirstOrDefaultAsync(x => x.CompanyId == material.CompanyId && x.Code == MaintenanceStandards.StockAccountCode)
                ?? throw new InvalidOperationException($"Account {MaintenanceStandards.StockAccountCode} {MaintenanceStandards.StockAccountName} is missing from the chart of accounts.");

            Account charge;
            if (material.ChargedTo == "Owner")
            {
                var settings = await GetCompanyAccountSettingAsync(material.CompanyId);
                charge = await GetMappedAccountAsync(material.CompanyId, settings.DueToLandlordsAccountId, "Due To Landlords");
            }
            else
            {
                charge = await _db.Accounts.FirstOrDefaultAsync(x => x.CompanyId == material.CompanyId && x.Code == MaintenanceStandards.MaintenanceExpenseAccountCode)
                    ?? throw new InvalidOperationException($"Account {MaintenanceStandards.MaintenanceExpenseAccountCode} Property Maintenance is missing from the chart of accounts.");
            }

            var text = $"Job {job.RequestNumber}: {material.Description}";
            var lines = new List<JournalEntryLine>
            {
                new() { AccountId = charge.Id, Debit = material.Amount, Credit = 0m, Description = text, PropertyId = job.PropertyId, UnitId = job.UnitId, TenantId = job.TenantId },
                new() { AccountId = stock.Id, Debit = 0m, Credit = material.Amount, Description = text, PropertyId = job.PropertyId, UnitId = job.UnitId }
            };

            var journal = await CreateJournalEntryAsync(
                material.CompanyId, material.MaterialDate, $"Maintenance materials for job {job.RequestNumber}",
                "MaintenanceMaterial", material.Id, lines, documentNumber: job.RequestNumber);

            material.JournalEntryId = journal.Id;
            await _db.SaveChangesAsync();
            return journal;
        }

        private async Task<JournalEntry> CreateJournalEntryAsync(
            int companyId,
            DateTime entryDate,
            string description,
            string sourceType,
            int sourceId,
            List<JournalEntryLine> lines,
            string journalType = "Auto",
            string? documentNumber = null,
            string? referenceNumber = null,
            string? remarks = null)
        {
            if (lines.Count < 2)
                throw new InvalidOperationException("A journal entry must contain at least two lines.");

            var totalDebit = lines.Sum(x => x.Debit);
            var totalCredit = lines.Sum(x => x.Credit);

            if (Math.Abs(totalDebit - totalCredit) != 0m)
                throw new InvalidOperationException("Journal entry is not balanced.");

            var period = await ResolveAccountingPeriodAsync(companyId, entryDate);

            if (period == null)
                throw new InvalidOperationException("No accounting period found for the entry date.");

            if (period.IsClosed)
                throw new InvalidOperationException("The accounting period is closed. Posting is not allowed.");

            var journalEntry = new JournalEntry
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                EntryNumber = await GenerateEntryNumberAsync(sourceType),
                EntryDate = entryDate.Date,
                JournalType = journalType,
                // Created as a DRAFT. A draft has no effect on the accounts - every
                // report filters on IsPosted - so the figures only move when someone
                // posts it from Journal Inquiry. JournalWorkflowService.PostAsync is
                // what applies the document consequences (invoice AmountPaid, statuses),
                // which is why none of the posting methods above do that themselves.
                Status = "Draft",
                Description = description,
                DocumentNumber = documentNumber,
                ReferenceNumber = referenceNumber,
                Remarks = remarks,
                SourceType = sourceType,
                SourceId = sourceId,
                IsPosted = false,
                CreatedAt = DateTime.UtcNow,
                PostedAt = null,
                Lines = lines
            };

            _db.JournalEntries.Add(journalEntry);
            await _db.SaveChangesAsync();

            return journalEntry;
        }

        private async Task<string> GenerateEntryNumberAsync(string sourceType)
        {
            var prefix = sourceType.Length >= 3
                ? sourceType[..3].ToUpperInvariant()
                : sourceType.ToUpperInvariant();

            var count = await _db.JournalEntries.CountAsync() + 1;
            return $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{count:D6}";
        }

        private async Task<string> GeneratePrepaymentScheduleNumberAsync()
        {
            var count = await _db.PrepaymentSchedules.CountAsync() + 1;
            return $"PRE-{DateTime.UtcNow:yyyyMMdd}-{count:D4}";
        }

        private static DateTime NormalizeToMonthEnd(DateTime value)
        {
            return new DateTime(value.Year, value.Month, DateTime.DaysInMonth(value.Year, value.Month));
        }

        private async Task<VendorBill> CreateAutoVendorBillAsync(
            int companyId,
            int vendorId,
            int? propertyId,
            DateTime billDate,
            decimal amount,
            string expenseCategory,
            string sourceCode,
            string? referenceNumber)
        {
            _ = await GetVendorAsync(companyId, vendorId);

            var bill = new VendorBill
            {
                CompanyId = companyId,
                VendorId = vendorId,
                PropertyId = propertyId,
                BillNumber = await GenerateAutoVendorBillNumberAsync(expenseCategory),
                VendorReference = referenceNumber,
                BillDate = billDate.Date,
                AccountingDate = billDate.Date,
                DueDate = billDate.Date,
                Subtotal = amount,
                VatAmount = 0m,
                TotalAmount = amount,
                AmountPaid = 0m,
                ExpenseCategory = expenseCategory,
                Status = "Posted"
            };

            // A single line so the document reads like any other bill on screen. Its
            // accounting is done by the routine that raised it, not by
            // PostVendorBillAsync, so the line carries no account of its own.
            bill.Lines.Add(new VendorBillLine
            {
                LineNumber = 1,
                Description = $"{expenseCategory} - {sourceCode}",
                PropertyId = propertyId,
                Quantity = 1m,
                UnitPrice = amount,
                LineSubtotal = amount,
                IsTaxable = false,
                LineTotal = amount
            });

            _db.VendorBills.Add(bill);
            await _db.SaveChangesAsync();
            return bill;
        }

        private async Task<Vendor> GetVendorAsync(int companyId, int vendorId)
        {
            var vendor = await _db.Vendors.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == vendorId);

            if (vendor == null)
                throw new InvalidOperationException("Selected vendor was not found.");

            if (!vendor.IsActive)
                throw new InvalidOperationException("Selected vendor is inactive.");

            return vendor;
        }

        private async Task<string> GenerateAutoVendorBillNumberAsync(string category)
        {
            var prefix = category switch
            {
                "Prepayment" => "VPP",
                _ => "VAP"
            };

            var count = await _db.VendorBills.CountAsync() + 1;
            return $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{count:D5}";
        }
    }
}
