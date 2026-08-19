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
                lines.Add(new JournalEntryLine
                {
                    AccountId = vatPayableAccount.Id,
                    Debit = 0m,
                    Credit = invoice.VatAmount,
                    Description = $"VAT on invoice {invoice.InvoiceNumber}",
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

            // A tax invoice number is allocated only when output tax is actually
            // charged, and only once - the series must stay sequential and gap-free,
            // so re-posting never issues a second number for the same supply.
            if (invoice.VatAmount > 0m && string.IsNullOrWhiteSpace(invoice.TaxInvoiceNumber))
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
                    VendorBillId = bill.Id
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

        public async Task<JournalEntry> PostVendorPaymentAsync(int vendorPaymentId)
        {
            var payment = await _db.VendorPayments
                .Include(x => x.VendorBill)
                    .ThenInclude(x => x!.Vendor)
                .Include(x => x.VendorBill)
                    .ThenInclude(x => x!.Property)
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

            var cashAccount = await GetMappedAccountAsync(
                payment.CompanyId,
                settings.DefaultCashAccountId,
                "Default Cash");

            var accountsPayableAccount = await GetMappedAccountAsync(
                payment.CompanyId,
                settings.AccountsPayableAccountId,
                "Accounts Payable");

            var bill = payment.VendorBill!;
            var lines = new List<JournalEntryLine>
            {
                new()
                {
                    AccountId = accountsPayableAccount.Id,
                    Debit = payment.Amount,
                    Credit = 0m,
                    Description = $"Settlement of vendor bill {bill.BillNumber}",
                    PropertyId = bill.PropertyId,
                    VendorId = bill.VendorId,
                    VendorBillId = bill.Id
                },
                new()
                {
                    AccountId = cashAccount.Id,
                    Debit = 0m,
                    Credit = payment.Amount,
                    Description = $"Vendor payment {payment.PaymentNumber}",
                    PropertyId = bill.PropertyId,
                    VendorId = bill.VendorId,
                    VendorBillId = bill.Id
                }
            };

            var journalEntry = await CreateJournalEntryAsync(
                payment.CompanyId,
                payment.PaymentDate,
                $"Posting vendor payment {payment.PaymentNumber}",
                "VendorPayment",
                payment.Id,
                lines,
                journalType: "Auto",
                documentNumber: payment.PaymentNumber,
                referenceNumber: bill.BillNumber);

            // Settlement of the bill is applied when the journal is posted.
            payment.JournalEntryId = journalEntry.Id;

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

        public async Task CloseAccountingPeriodAsync(int companyId, int fiscalYear, int periodNumber)
        {
            var period = await _db.AccountingPeriods.FirstOrDefaultAsync(x =>
                x.CompanyId == companyId &&
                x.FiscalYear == fiscalYear &&
                x.PeriodNumber == periodNumber);

            if (period == null)
                throw new InvalidOperationException("Accounting period was not found.");

            if (period.IsClosed)
                return;

            await ProcessRevenueRecognitionUpToAsync(period.EndDate);
            await ProcessPrepaymentRecognitionsUpToAsync(period.EndDate);

            period.IsClosed = true;
            period.ClosedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        public async Task<JournalEntry> CloseFiscalYearAsync(int companyId, int fiscalYear)
        {
            var existingClosing = await _db.FiscalYearClosings
                .Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.FiscalYear == fiscalYear);

            if (existingClosing?.JournalEntryId != null)
            {
                return await _db.JournalEntries
                    .Include(x => x.Lines)
                    .FirstAsync(x => x.Id == existingClosing.JournalEntryId.Value);
            }

            var periodStart = new DateTime(fiscalYear, 1, 1);
            var periodEnd = new DateTime(fiscalYear, 12, 31);

            await ProcessRevenueRecognitionUpToAsync(periodEnd);

            var entryLines = await _db.JournalEntryLines
                .Include(x => x.Account)
                .Include(x => x.JournalEntry)
                .Where(x =>
                    x.JournalEntry != null &&
                    x.JournalEntry.IsPosted &&
                    x.JournalEntry.EntryDate >= periodStart &&
                    x.JournalEntry.EntryDate <= periodEnd)
                .ToListAsync();

            var revenueBalances = entryLines
                .Where(x => x.Account != null && x.Account.AccountType == "Revenue")
                .GroupBy(x => new { x.AccountId, x.Account!.Name })
                .Select(g => new
                {
                    g.Key.AccountId,
                    Balance = g.Sum(x => x.Credit - x.Debit)
                })
                .Where(x => x.Balance != 0m)
                .ToList();

            var expenseBalances = entryLines
                .Where(x => x.Account != null && x.Account.AccountType == "Expense")
                .GroupBy(x => new { x.AccountId, x.Account!.Name })
                .Select(g => new
                {
                    g.Key.AccountId,
                    Balance = g.Sum(x => x.Debit - x.Credit)
                })
                .Where(x => x.Balance != 0m)
                .ToList();

            if (!revenueBalances.Any() && !expenseBalances.Any())
                throw new InvalidOperationException("There are no revenue or expense balances to close for this fiscal year.");

            var settings = await GetCompanyAccountSettingAsync(companyId);

            var retainedEarningsAccount = await GetMappedAccountAsync(
                companyId,
                settings.RetainedEarningsPriorYearsAccountId,
                "Retained Earnings - Prior Years");

            var closingLines = new List<JournalEntryLine>();

            foreach (var revenue in revenueBalances)
            {
                closingLines.Add(new JournalEntryLine
                {
                    AccountId = revenue.AccountId,
                    Debit = revenue.Balance,
                    Credit = 0m,
                    Description = $"Year-end closing of revenue for {fiscalYear}"
                });
            }

            foreach (var expense in expenseBalances)
            {
                closingLines.Add(new JournalEntryLine
                {
                    AccountId = expense.AccountId,
                    Debit = 0m,
                    Credit = expense.Balance,
                    Description = $"Year-end closing of expense for {fiscalYear}"
                });
            }

            var totalRevenue = revenueBalances.Sum(x => x.Balance);
            var totalExpense = expenseBalances.Sum(x => x.Balance);
            var netIncome = totalRevenue - totalExpense;

            if (netIncome >= 0m)
            {
                closingLines.Add(new JournalEntryLine
                {
                    AccountId = retainedEarningsAccount.Id,
                    Debit = 0m,
                    Credit = netIncome,
                    Description = $"Transfer net income to retained earnings for {fiscalYear}"
                });
            }
            else
            {
                closingLines.Add(new JournalEntryLine
                {
                    AccountId = retainedEarningsAccount.Id,
                    Debit = Math.Abs(netIncome),
                    Credit = 0m,
                    Description = $"Transfer net loss to retained earnings for {fiscalYear}"
                });
            }

            var closingEntry = await CreateJournalEntryAsync(
                companyId,
                periodEnd,
                $"Fiscal year closing for {fiscalYear}",
                "FiscalYearClosing",
                fiscalYear,
                closingLines,
                journalType: "Closing",
                documentNumber: $"FY-{fiscalYear}",
                referenceNumber: $"FY-CLOSE-{fiscalYear}");

            var closingRecord = new FiscalYearClosing
            {
                CompanyId = companyId,
                FiscalYear = fiscalYear,
                RetainedEarningsAccountId = retainedEarningsAccount.Id,
                JournalEntryId = closingEntry.Id,
                ProfitOrLossAmount = netIncome,
                ClosedAt = DateTime.UtcNow,
                Notes = $"Automatic fiscal year closing for {fiscalYear}"
            };

            _db.FiscalYearClosings.Add(closingRecord);

            var periods = await _db.AccountingPeriods
                .Where(x => x.CompanyId == companyId && x.FiscalYear == fiscalYear && !x.IsClosed)
                .ToListAsync();

            foreach (var period in periods)
            {
                period.IsClosed = true;
                period.ClosedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return closingEntry;
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
