using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Read-only data for the tenant portal.
    ///
    /// ONLY POSTED TRANSACTIONS ARE SHOWN. A draft is not in the accounts, so showing
    /// one would put the tenant's screens out of step with their balance - a draft
    /// receipt listed as "received" while the amount still shows as owing. Invoices,
    /// receipts, cheques and deposits all appear once their journal is posted. A journal
    /// that has been reset to draft disappears again until it is re-posted.
    ///
    /// Every method takes the tenant id from the signed-in principal and filters on it.
    /// Nothing here accepts a tenant id from a route or a form, which is what prevents
    /// one tenant reading another's account by editing a number in the URL.
    /// </summary>
    public class TenantPortalService
    {
        private readonly ApplicationDbContext _db;

        public TenantPortalService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<PortalDashboardViewModel> GetDashboardAsync(int tenantId, DateTime asOfDate)
        {
            var tenant = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == tenantId)
                ?? throw new InvalidOperationException("Tenant record not found.");

            var company = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == tenant.CompanyId);

            var leases = await GetContractsAsync(tenantId, asOfDate);
            var invoices = await GetInvoicesAsync(tenantId);
            var pdcs = await GetPdcsAsync(tenantId);

            var depositsHeld = await _db.SecurityDeposits
                .AsNoTracking()
                .Where(x => x.Lease!.TenantId == tenantId
                            && x.Status == "Held"
                            && x.JournalEntry != null
                            && x.JournalEntry.IsPosted)
                .SumAsync(x => (decimal?)x.Amount) ?? 0m;

            var invoiced = invoices.Sum(x => x.TotalAmount);

            // AmountPaid counts credit notes as well as money; the tenant sees them apart.
            var credited = invoices.Sum(x => x.CreditedAmount);
            var settled = invoices.Sum(x => x.AmountPaid);
            var paid = settled - credited;

            var overdue = invoices
                .Where(x => x.DueDate < asOfDate && x.Outstanding > 0m)
                .ToList();

            return new PortalDashboardViewModel
            {
                AsOfDate = asOfDate,
                TenantName = tenant.FullName,
                TenantCode = tenant.Code,
                CompanyName = company?.Name ?? string.Empty,
                CompanyPhone = company?.Phone,
                CompanyEmail = company?.Email,

                OpeningBalance = tenant.OpeningBalance,
                TotalInvoiced = invoiced,
                TotalPaid = paid,
                TotalCredited = credited,
                OutstandingBalance = tenant.OpeningBalance + invoiced - settled,

                OverdueAmount = overdue.Sum(x => x.Outstanding),
                OverdueCount = overdue.Count,

                SecurityDepositHeld = depositsHeld,

                ActiveContracts = leases.Where(x => x.IsCurrent).ToList(),
                NextDueInvoice = invoices
                    .Where(x => x.Outstanding > 0m)
                    .OrderBy(x => x.DueDate)
                    .FirstOrDefault(),

                PdcsOnHand = pdcs.Where(x => x.Status == "Received" || x.Status == "Deposited").ToList()
            };
        }

        public async Task<List<PortalContractViewModel>> GetContractsAsync(int tenantId, DateTime asOfDate)
        {
            return await _db.Leases
                .AsNoTracking()
                // A draft or a contract awaiting approval is not yet agreed: the tenant does not see it.
                .Where(x => x.TenantId == tenantId &&
                            x.Status != LeaseStatuses.Draft && x.Status != LeaseStatuses.PendingApproval)
                .OrderByDescending(x => x.StartDate)
                .Select(x => new PortalContractViewModel
                {
                    LeaseId = x.Id,
                    ContractNumber = x.ContractNumber,
                    PropertyName = x.Unit!.Property!.Name,
                    PropertyAddress = x.Unit.Property.Address,
                    UnitCode = x.Unit.UnitNumber,
                    Floor = x.Unit.Floor,
                    AreaSqFt = x.Unit.AreaSqFt,
                    StartDate = x.StartDate,
                    EndDate = x.EndDate,
                    AnnualRent = x.AnnualRent,
                    BillingFrequencyMonths = x.BillingFrequencyMonths,
                    VatCategory = x.VatCategory,
                    SecurityDepositAmount = x.SecurityDepositAmount,
                    Status = x.Status,
                    AsOfDate = asOfDate
                })
                .ToListAsync();
        }

        /// <summary>
        /// Posted invoices only. A draft has no ledger effect and is not yet a demand on
        /// the tenant, so showing it would be misleading.
        /// </summary>
        public async Task<List<PortalInvoiceViewModel>> GetInvoicesAsync(int tenantId)
        {
            return await _db.CustomerInvoices
                .AsNoTracking()
                .Where(x => x.Lease!.TenantId == tenantId
                            && x.JournalEntry != null
                            && x.JournalEntry.IsPosted
                            && x.Status != "Cancelled")
                .OrderByDescending(x => x.InvoiceDate)
                .Select(x => new PortalInvoiceViewModel
                {
                    InvoiceId = x.Id,
                    InvoiceNumber = x.InvoiceNumber,
                    TaxInvoiceNumber = x.TaxInvoiceNumber,
                    ContractNumber = x.Lease!.ContractNumber,
                    UnitCode = x.Lease.Unit!.UnitNumber,
                    InvoiceDate = x.InvoiceDate,
                    DueDate = x.DueDate,
                    Subtotal = x.Subtotal,
                    VatAmount = x.VatAmount,
                    TotalAmount = x.TotalAmount,
                    AmountPaid = x.AmountPaid,
                    CreditedAmount = x.CreditedAmount,
                    Status = x.Status
                })
                .ToListAsync();
        }

        public async Task<List<PortalReceiptViewModel>> GetReceiptsAsync(int tenantId)
        {
            return await _db.CustomerReceipts
                .AsNoTracking()
                .Where(x => x.CustomerInvoice!.Lease!.TenantId == tenantId
                            && x.JournalEntry != null
                            && x.JournalEntry.IsPosted)
                .OrderByDescending(x => x.ReceiptDate)
                .Select(x => new PortalReceiptViewModel
                {
                    ReceiptNumber = x.ReceiptNumber,
                    ReceiptDate = x.ReceiptDate,
                    Amount = x.Amount,
                    PaymentMethod = x.PaymentMethod,
                    AgainstInvoice = x.CustomerInvoice!.InvoiceNumber
                })
                .ToListAsync();
        }

        public async Task<List<PortalPdcViewModel>> GetPdcsAsync(int tenantId)
        {
            return await _db.PdcReceipts
                .AsNoTracking()
                .Where(x => x.Lease!.TenantId == tenantId
                            && x.MemorandumJournalEntry != null
                            && x.MemorandumJournalEntry.IsPosted)
                .OrderBy(x => x.ChequeDate)
                .Select(x => new PortalPdcViewModel
                {
                    ChequeNumber = x.ChequeNumber,
                    BankName = x.BankName,
                    ChequeDate = x.ChequeDate,
                    ReceivedDate = x.ReceivedDate,
                    ClearedDate = x.ClearedDate,
                    Amount = x.Amount,
                    Status = x.Status,
                    AgainstInvoice = x.CustomerInvoice != null ? x.CustomerInvoice.InvoiceNumber : null
                })
                .ToListAsync();
        }

        /// <summary>
        /// Running statement: opening balance, then every charge and payment in date
        /// order with a running balance. The same shape as the statement staff see.
        /// </summary>
        public async Task<PortalStatementViewModel> GetStatementAsync(int tenantId, DateTime fromDate, DateTime toDate)
        {
            var tenant = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == tenantId)
                ?? throw new InvalidOperationException("Tenant record not found.");

            var invoices = await _db.CustomerInvoices
                .AsNoTracking()
                .Where(x => x.Lease!.TenantId == tenantId
                            && x.JournalEntry != null
                            && x.JournalEntry.IsPosted
                            && x.Status != "Cancelled"
                            && x.InvoiceDate <= toDate)
                .Select(x => new { x.InvoiceDate, x.InvoiceNumber, x.TotalAmount, x.ChargeType, ContractNumber = x.Lease!.ContractNumber })
                .ToListAsync();

            var receipts = await _db.CustomerReceipts
                .AsNoTracking()
                .Where(x => x.CustomerInvoice!.Lease!.TenantId == tenantId
                            && x.JournalEntry != null
                            && x.JournalEntry.IsPosted
                            && x.ReceiptDate <= toDate)
                .Select(x => new { x.ReceiptDate, x.ReceiptNumber, x.Amount, Invoice = x.CustomerInvoice!.InvoiceNumber })
                .ToListAsync();

            // A cleared PDC settles the account, so it belongs on the statement as a
            // payment. One still on hand does not - the money has not moved.
            // Deposited for collection, it is credited on the deposit; a bounce charges it back.
            var cheques = await _db.PdcReceipts
                .AsNoTracking()
                .Where(x => x.Lease!.TenantId == tenantId && x.SettlementMode == "Receivable")
                .Select(x => new
                {
                    x.ChequeNumber, x.Amount, x.BouncedDate,
                    Deposited = x.DepositJournalEntry != null && x.DepositJournalEntry.IsPosted ? x.DepositedDate : null,
                    Cleared = x.ClearanceJournalEntry != null && x.ClearanceJournalEntry.IsPosted ? x.ClearedDate : null,
                    Bounced = x.BounceJournalEntry != null && x.BounceJournalEntry.IsPosted
                })
                .ToListAsync();

            var clearedPdcs = cheques
                .Select(x => new { Date = x.Deposited ?? x.Cleared, x.ChequeNumber, x.Amount, Deposited = x.Deposited != null })
                .Where(x => x.Date != null && x.Date <= toDate)
                .Select(x => new { Date = x.Date!.Value, x.ChequeNumber, x.Amount, x.Deposited })
                .ToList();

            var returnedPdcs = cheques
                .Where(x => x.Deposited != null && x.Bounced && x.BouncedDate != null && x.BouncedDate <= toDate)
                .Select(x => new { Date = x.BouncedDate!.Value, x.ChequeNumber, x.Amount })
                .ToList();

            var creditNotes = await _db.CustomerCreditNotes
                .AsNoTracking()
                .Where(x => x.CustomerInvoice!.Lease!.TenantId == tenantId
                            && x.JournalEntry != null
                            && x.JournalEntry.IsPosted
                            && x.CreditNoteDate <= toDate)
                .Select(x => new { x.CreditNoteDate, x.CreditNoteNumber, x.TotalAmount, Invoice = x.CustomerInvoice!.InvoiceNumber, x.Reason })
                .ToListAsync();

            var entries = new List<PortalStatementLineViewModel>();

            entries.AddRange(creditNotes.Select(x => new PortalStatementLineViewModel
            {
                Date = x.CreditNoteDate,
                Reference = x.CreditNoteNumber,
                Description = $"Credit note against {x.Invoice} - {x.Reason}",
                Payment = x.TotalAmount
            }));

            entries.AddRange(invoices.Select(x => new PortalStatementLineViewModel
            {
                Date = x.InvoiceDate,
                Reference = x.InvoiceNumber,
                Description = x.ChargeType != null ? $"{x.ChargeType} - contract {x.ContractNumber}" : $"Rent invoice - contract {x.ContractNumber}",
                Charge = x.TotalAmount
            }));

            entries.AddRange(receipts.Select(x => new PortalStatementLineViewModel
            {
                Date = x.ReceiptDate,
                Reference = x.ReceiptNumber,
                Description = $"Payment received against {x.Invoice}",
                Payment = x.Amount
            }));

            entries.AddRange(clearedPdcs.Select(x => new PortalStatementLineViewModel
            {
                Date = x.Date,
                Reference = x.ChequeNumber,
                Description = x.Deposited ? $"Cheque {x.ChequeNumber} deposited" : $"Cheque {x.ChequeNumber} cleared",
                Payment = x.Amount
            }));

            entries.AddRange(returnedPdcs.Select(x => new PortalStatementLineViewModel
            {
                Date = x.Date,
                Reference = x.ChequeNumber,
                Description = $"Cheque {x.ChequeNumber} returned unpaid",
                Charge = x.Amount
            }));

            var ordered = entries.OrderBy(x => x.Date).ThenBy(x => x.Reference).ToList();

            // Everything before the window collapses into the opening balance so the
            // running balance is correct even on a short date range.
            var opening = tenant.OpeningBalance
                + ordered.Where(x => x.Date < fromDate).Sum(x => x.Charge - x.Payment);

            var inPeriod = ordered.Where(x => x.Date >= fromDate).ToList();

            var running = opening;
            foreach (var line in inPeriod)
            {
                running += line.Charge - line.Payment;
                line.RunningBalance = running;
            }

            return new PortalStatementViewModel
            {
                TenantName = tenant.FullName,
                TenantCode = tenant.Code,
                FromDate = fromDate,
                ToDate = toDate,
                OpeningBalance = opening,
                Lines = inPeriod,
                ClosingBalance = running
            };
        }
    }
}
