using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// A vendor's statement of account, read from the ledger itself: every posted line on
    /// Accounts Payable that carries the vendor - bills, payments, prepayment and fixed
    /// asset purchases on credit, and manual journals tagged with the vendor. Drafts are
    /// not in the accounts and are left out. Built this way the closing balance is, by
    /// construction, the vendor's share of Accounts Payable in the Trial Balance.
    /// </summary>
    public class VendorStatementService
    {
        private readonly ApplicationDbContext _db;

        public VendorStatementService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<VendorStatementViewModel> GetVendorStatementAsync(int vendorId, DateTime fromDate, DateTime toDate)
        {
            if (fromDate.Date > toDate.Date)
                throw new InvalidOperationException("From date cannot be later than to date.");

            var vendor = await _db.Vendors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == vendorId)
                         ?? throw new InvalidOperationException("Vendor was not found.");

            var payablesAccountId = await _db.CompanyAccountSettings.AsNoTracking()
                .Select(x => x.AccountsPayableAccountId)
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("Accounts Payable is not mapped in Account Mappings.");

            var to = toDate.Date;
            var from = fromDate.Date;

            var lines = await _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.AccountId == payablesAccountId &&
                            x.VendorId == vendorId &&
                            x.JournalEntry != null &&
                            x.JournalEntry.IsPosted &&
                            x.JournalEntry.EntryDate <= to)
                .Select(x => new
                {
                    x.Debit,
                    x.Credit,
                    x.Description,
                    x.JournalEntry!.Id,
                    x.JournalEntry.EntryDate,
                    x.JournalEntry.EntryNumber,
                    x.JournalEntry.DocumentNumber,
                    x.JournalEntry.ReferenceNumber,
                    x.JournalEntry.SourceType,
                    EntryDescription = x.JournalEntry.Description
                })
                .ToListAsync();

            // The vendor's own opening balance brought forward, then everything before the period.
            var opening = vendor.OpeningBalance + lines
                .Where(x => x.EntryDate.Date < from)
                .Sum(x => x.Credit - x.Debit);

            var rows = lines
                .Where(x => x.EntryDate.Date >= from)
                .OrderBy(x => x.EntryDate).ThenBy(x => x.Id)
                .Select(x => new VendorStatementRowViewModel
                {
                    TransactionDate = x.EntryDate.Date,
                    TransactionType = Describe(x.SourceType),
                    ReferenceNumber = string.IsNullOrWhiteSpace(x.DocumentNumber) ? x.EntryNumber : x.DocumentNumber!,
                    Description = x.SourceType == "VendorPayment" && !string.IsNullOrWhiteSpace(x.ReferenceNumber)
                        ? $"Payment against bill {x.ReferenceNumber}"
                        : string.IsNullOrWhiteSpace(x.EntryDescription) ? x.Description ?? string.Empty : x.EntryDescription,
                    Debit = x.Debit,
                    Credit = x.Credit
                })
                .ToList();

            var running = opening;
            foreach (var row in rows)
            {
                running += row.Credit - row.Debit;
                row.RunningBalance = running;
            }

            var openBills = await _db.VendorBills.AsNoTracking()
                .Where(x => x.VendorId == vendorId &&
                            x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.AccountingDate <= to &&
                            x.TotalAmount > x.AmountPaid)
                .OrderBy(x => x.DueDate)
                .Select(x => new VendorOpenBillViewModel
                {
                    BillNumber = x.BillNumber,
                    VendorReference = x.VendorReference,
                    BillDate = x.BillDate,
                    DueDate = x.DueDate,
                    TotalAmount = x.TotalAmount,
                    AmountPaid = x.AmountPaid
                })
                .ToListAsync();

            foreach (var bill in openBills)
                bill.DaysOverdue = Math.Max(0, (to - bill.DueDate.Date).Days);

            // Advances (down payments) sit on 11550 Advances to Suppliers, not in payables.
            var advanceLines = await _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.Account != null && x.Account.Code == VendorClearingService.SupplierAdvancesAccountCode &&
                            x.VendorId == vendorId && x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.JournalEntry.EntryDate <= to)
                .OrderBy(x => x.JournalEntry!.EntryDate).ThenBy(x => x.JournalEntryId)
                .Select(x => new VendorStatementRowViewModel
                {
                    TransactionDate = x.JournalEntry!.EntryDate,
                    TransactionType = x.Debit > 0 ? "Advance paid" : "Advance applied",
                    ReferenceNumber = x.JournalEntry.DocumentNumber ?? x.JournalEntry.EntryNumber,
                    Description = x.Description ?? string.Empty,
                    Debit = x.Debit,
                    Credit = x.Credit
                })
                .ToListAsync();
            var advanceRunning = 0m;
            foreach (var row in advanceLines)
            {
                advanceRunning += row.Debit - row.Credit;
                row.RunningBalance = advanceRunning;
            }

            return new VendorStatementViewModel
            {
                AdvancesOpen = advanceRunning,
                AdvanceRows = advanceLines,
                VendorId = vendorId,
                FromDate = from,
                ToDate = to,
                VendorCode = vendor.Code,
                VendorName = vendor.Name,
                VendorTrn = vendor.TrnNumber,
                OpeningBalance = opening,
                TotalBilled = rows.Sum(x => x.Credit),
                TotalPaid = rows.Sum(x => x.Debit),
                ClosingBalance = running,
                Rows = rows,
                OpenBills = openBills
            };
        }

        private static string Describe(string? sourceType) => sourceType switch
        {
            "VendorBill" => "Bill",
            "VendorPayment" => "Payment",
            VendorClearingService.AdvanceSourceType => "Advance applied",
            "PrepaymentSchedule" => "Prepayment",
            "FixedAssetAcquisition" => "Asset purchase",
            "ManualJournal" or null or "" => "Journal",
            _ => sourceType
        };
    }
}
