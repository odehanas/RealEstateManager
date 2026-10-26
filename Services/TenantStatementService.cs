using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    public class TenantStatementService
    {
        private readonly ApplicationDbContext _db;

        public TenantStatementService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<TenantStatementViewModel> GetTenantStatementAsync(
            int tenantId,
            int? leaseId,
            DateTime fromDate,
            DateTime toDate)
        {
            if (fromDate.Date > toDate.Date)
                throw new InvalidOperationException("From date cannot be later than to date.");

            var tenant = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == tenantId);

            if (tenant == null)
                throw new InvalidOperationException("Tenant was not found.");

            var leases = await _db.Leases
                .AsNoTracking()
                .Include(x => x.Unit)
                    .ThenInclude(x => x!.Property)
                .Where(x => x.TenantId == tenantId)
                .ToListAsync();

            var selectedLease = leaseId.HasValue
                ? leases.FirstOrDefault(x => x.Id == leaseId.Value)
                : null;

            if (leaseId.HasValue && selectedLease == null)
                throw new InvalidOperationException("Selected lease was not found for the tenant.");

            // Only documents whose journal is posted are in the accounts. A journal reset
            // to draft keeps its link to the document, so the link alone is not enough.
            var invoices = await _db.CustomerInvoices
                .AsNoTracking()
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .Where(x =>
                    x.JournalEntry != null &&
                    x.JournalEntry.IsPosted &&
                    x.Status != "Cancelled" &&
                    x.Lease != null &&
                    x.Lease.TenantId == tenantId &&
                    (!leaseId.HasValue || x.LeaseId == leaseId.Value))
                .ToListAsync();

            var receipts = await _db.CustomerReceipts
                .AsNoTracking()
                .Include(x => x.CustomerInvoice)
                    .ThenInclude(x => x!.Lease)
                        .ThenInclude(x => x!.Unit)
                            .ThenInclude(x => x!.Property)
                .Where(x =>
                    x.JournalEntry != null &&
                    x.JournalEntry.IsPosted &&
                    x.CustomerInvoice != null &&
                    x.CustomerInvoice.Lease != null &&
                    x.CustomerInvoice.Lease.TenantId == tenantId &&
                    (!leaseId.HasValue || x.CustomerInvoice.LeaseId == leaseId.Value))
                .ToListAsync();

            var pdcReceipts = await _db.PdcReceipts
                .AsNoTracking()
                .Include(x => x.Lease)
                    .ThenInclude(x => x!.Unit)
                        .ThenInclude(x => x!.Property)
                .Include(x => x.CustomerInvoice)
                .Include(x => x.ClearanceJournalEntry)
                .Include(x => x.MemorandumJournalEntry)
                .Where(x =>
                    x.Lease != null &&
                    x.Lease.TenantId == tenantId &&
                    (!leaseId.HasValue || x.LeaseId == leaseId.Value))
                .ToListAsync();

            // A posted credit note reduces what the tenant owes, exactly as a payment does.
            var creditNotes = await _db.CustomerCreditNotes
                .AsNoTracking()
                .Include(x => x.CustomerInvoice)
                    .ThenInclude(x => x!.Lease)
                        .ThenInclude(x => x!.Unit)
                            .ThenInclude(x => x!.Property)
                .Where(x =>
                    x.JournalEntry != null &&
                    x.JournalEntry.IsPosted &&
                    x.CustomerInvoice != null &&
                    x.CustomerInvoice.Lease != null &&
                    x.CustomerInvoice.Lease.TenantId == tenantId &&
                    (!leaseId.HasValue || x.CustomerInvoice.LeaseId == leaseId.Value))
                .ToListAsync();

            var openingCredits = creditNotes
                .Where(x => x.CreditNoteDate.Date < fromDate.Date)
                .Sum(x => x.TotalAmount);

            var openingCharges = invoices
                .Where(x => x.InvoiceDate.Date < fromDate.Date)
                .Sum(x => x.TotalAmount);

            var openingReceipts = receipts
                .Where(x => x.ReceiptDate.Date < fromDate.Date)
                .Sum(x => x.Amount);

            var openingClearedPdc = pdcReceipts
                .Where(x =>
                    x.Status == "Cleared" &&
                    x.ClearanceJournalEntry != null &&
                    x.ClearanceJournalEntry.IsPosted &&
                    x.ClearedDate.HasValue &&
                    x.ClearedDate.Value.Date < fromDate.Date)
                .Sum(x => x.Amount);

            var rows = new List<TenantStatementRowViewModel>();

            rows.AddRange(invoices
                .Where(x => x.InvoiceDate.Date >= fromDate.Date && x.InvoiceDate.Date <= toDate.Date)
                .Select(x => new TenantStatementRowViewModel
                {
                    SourceType = "CustomerInvoice",
                    SourceId = x.Id,
                    TransactionDate = x.InvoiceDate.Date,
                    TransactionType = "Invoice",
                    ReferenceNumber = x.InvoiceNumber,
                    Description = BuildInvoiceDescription(x.Lease),
                    Debit = x.TotalAmount,
                    Credit = 0m
                }));

            rows.AddRange(receipts
                .Where(x => x.ReceiptDate.Date >= fromDate.Date && x.ReceiptDate.Date <= toDate.Date)
                .Select(x => new TenantStatementRowViewModel
                {
                    SourceType = "CustomerReceipt",
                    SourceId = x.Id,
                    TransactionDate = x.ReceiptDate.Date,
                    TransactionType = "Receipt",
                    ReferenceNumber = x.ReceiptNumber,
                    Description = BuildReceiptDescription(x.CustomerInvoice?.Lease),
                    Debit = 0m,
                    Credit = x.Amount
                }));

            rows.AddRange(pdcReceipts
                .Where(x =>
                    x.Status == "Cleared" &&
                    x.ClearanceJournalEntry != null &&
                    x.ClearanceJournalEntry.IsPosted &&
                    x.ClearedDate.HasValue &&
                    x.ClearedDate.Value.Date >= fromDate.Date &&
                    x.ClearedDate.Value.Date <= toDate.Date)
                .Select(x => new TenantStatementRowViewModel
                {
                    SourceType = "PdcClearance",
                    SourceId = x.Id,
                    TransactionDate = x.ClearedDate!.Value.Date,
                    TransactionType = "PDC Cleared",
                    ReferenceNumber = x.ChequeNumber,
                    Description = x.SettlementMode == "Receivable"
                        ? $"PDC cleared against receivable - {BuildLeaseDisplay(x.Lease)}"
                        : $"PDC cleared as customer advance - {BuildLeaseDisplay(x.Lease)}",
                    Debit = 0m,
                    Credit = x.Amount
                }));

            rows.AddRange(creditNotes
                .Where(x => x.CreditNoteDate.Date >= fromDate.Date && x.CreditNoteDate.Date <= toDate.Date)
                .Select(x => new TenantStatementRowViewModel
                {
                    SourceType = "CustomerCreditNote",
                    SourceId = x.Id,
                    TransactionDate = x.CreditNoteDate.Date,
                    TransactionType = "Credit Note",
                    ReferenceNumber = x.CreditNoteNumber,
                    Description = $"Credit against {x.CustomerInvoice!.InvoiceNumber} - {BuildLeaseDisplay(x.CustomerInvoice.Lease)}",
                    Debit = 0m,
                    Credit = x.TotalAmount
                }));

            rows = rows
                .OrderBy(x => x.TransactionDate)
                .ThenBy(x => GetTransactionOrder(x.TransactionType))
                .ThenBy(x => x.ReferenceNumber)
                .ToList();

            var openingBalance = openingCharges - openingReceipts - openingClearedPdc - openingCredits;
            var runningBalance = openingBalance;

            foreach (var row in rows)
            {
                runningBalance += row.Debit - row.Credit;
                row.RunningBalance = runningBalance;
            }

            var notedPdcItems = pdcReceipts
                .Where(x =>
                    x.MemorandumJournalEntry != null &&
                    x.MemorandumJournalEntry.IsPosted &&
                    x.ReceivedDate.Date <= toDate.Date &&
                    x.Status != "Cancelled" &&
                    x.Status != "Replaced" &&
                    (!x.ClearedDate.HasValue || x.ClearedDate.Value.Date > toDate.Date))
                .OrderBy(x => x.ReceivedDate)
                .ThenBy(x => x.ChequeNumber)
                .Select(x => new TenantStatementPdcItemViewModel
                {
                    ReceivedDate = x.ReceivedDate.Date,
                    ChequeDate = x.ChequeDate.Date,
                    ChequeNumber = x.ChequeNumber,
                    BankName = x.BankName,
                    Amount = x.Amount,
                    SettlementMode = x.SettlementMode,
                    Status = x.Status,
                    LeaseDisplay = BuildLeaseDisplay(x.Lease),
                    InvoiceDisplay = x.CustomerInvoice?.InvoiceNumber ?? "-"
                })
                .ToList();

            return new TenantStatementViewModel
            {
                TenantId = tenantId,
                LeaseId = leaseId,
                FromDate = fromDate.Date,
                ToDate = toDate.Date,
                TenantCode = tenant.Code,
                TenantName = tenant.FullName,
                LeaseDisplay = selectedLease != null ? BuildLeaseDisplay(selectedLease) : "All Leases",
                OpeningBalance = openingBalance,
                TotalCharges = rows.Sum(x => x.Debit),
                TotalCredits = rows.Sum(x => x.Credit),
                ClosingBalance = runningBalance,
                NotedPdcTotal = notedPdcItems.Sum(x => x.Amount),
                Rows = rows,
                NotedPdcItems = notedPdcItems
            };
        }

        private static int GetTransactionOrder(string transactionType)
        {
            return transactionType == "Invoice" ? 1 : 2;
        }

        private static string BuildInvoiceDescription(Domain.Lease? lease)
        {
            return $"Invoice posted - {BuildLeaseDisplay(lease)}";
        }

        private static string BuildReceiptDescription(Domain.Lease? lease)
        {
            return $"Cash receipt posted - {BuildLeaseDisplay(lease)}";
        }

        private static string BuildLeaseDisplay(Domain.Lease? lease)
        {
            if (lease == null)
                return "Lease not available";

            var propertyCode = lease.Unit?.Property?.Code ?? "-";
            var unitNumber = lease.Unit?.UnitNumber ?? "-";
            return $"{lease.ContractNumber} - {propertyCode}/{unitNumber}";
        }
    }
}
