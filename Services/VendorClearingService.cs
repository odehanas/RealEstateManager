using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Vendor open items and their clearing, modelled on SAP:
    ///
    ///  * Outgoing payment with open-item selection (F-53): choose the vendor, then the
    ///    open bills to pay, each in full or in part (the partial-payment method - the bill
    ///    stays open with the payment linked). Every payment must be applied to at least
    ///    one open item. It gets a clearing document number; a bill that reaches zero is
    ///    cleared by that number.
    ///  * Down payment (F-48, special G/L indicator A): paid before the bill, held on
    ///    11550 Advances to Suppliers - not in Accounts Payable - as an open item of its own.
    ///  * Clearing a down payment against bills (F-54): the advance settles the bills,
    ///    Dr Accounts Payable / Cr Advances to Suppliers, with its own clearing number.
    /// </summary>
    public class VendorClearingService
    {
        public const string AdvanceSourceType = "VendorAdvanceClearing";
        public const string SupplierAdvancesAccountCode = "11550";

        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _posting;
        private readonly JournalWorkflowService _workflow;

        public VendorClearingService(ApplicationDbContext db, AccountingPostingService posting, JournalWorkflowService workflow)
        {
            _db = db;
            _posting = posting;
            _workflow = workflow;
        }

        // =====================================================================
        //  OPEN ITEMS
        // =====================================================================

        public sealed class OpenItem
        {
            public int BillId { get; set; }
            public string BillNumber { get; set; } = string.Empty;
            public string? VendorReference { get; set; }
            public DateTime BillDate { get; set; }
            public DateTime DueDate { get; set; }
            public string? Property { get; set; }
            public string? Project { get; set; }
            public decimal Total { get; set; }
            public decimal Paid { get; set; }
            /// <summary>Already in payments or applications still waiting to be posted (e.g. for approval).</summary>
            public decimal Pending { get; set; }
            public decimal Open => Total - Paid - Pending;
            public int DaysOverdue { get; set; }
        }

        public sealed class OpenAdvance
        {
            public int PaymentId { get; set; }
            public string PaymentNumber { get; set; } = string.Empty;
            public DateTime PaymentDate { get; set; }
            public string? Reference { get; set; }
            public decimal Amount { get; set; }
            public decimal Applied { get; set; }
            public decimal Pending { get; set; }
            public decimal Open => Amount - Applied - Pending;
        }

        /// <summary>The vendor's posted bills with something still to pay, oldest due first.</summary>
        public async Task<List<OpenItem>> GetOpenItemsAsync(int vendorId)
        {
            var bills = await _db.VendorBills.AsNoTracking()
                .Where(x => x.VendorId == vendorId && x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.Status != "Cancelled" && x.TotalAmount > x.AmountPaid)
                .Select(x => new OpenItem
                {
                    BillId = x.Id,
                    BillNumber = x.BillNumber,
                    VendorReference = x.VendorReference,
                    BillDate = x.BillDate,
                    DueDate = x.DueDate,
                    Property = x.Property != null ? x.Property.Name : null,
                    Project = x.DevelopmentProject != null ? x.DevelopmentProject.Code : null,
                    Total = x.TotalAmount,
                    Paid = x.AmountPaid
                })
                .OrderBy(x => x.DueDate).ThenBy(x => x.BillDate)
                .ToListAsync();

            var ids = bills.Select(x => x.BillId).ToList();
            var pending = await _db.VendorClearingLines.AsNoTracking()
                .Where(x => ids.Contains(x.VendorBillId) && x.VendorClearing!.Status == VendorClearingStatuses.Pending)
                .GroupBy(x => x.VendorBillId)
                .Select(g => new { BillId = g.Key, Amount = g.Sum(x => x.Amount) })
                .ToListAsync();

            foreach (var bill in bills)
            {
                bill.Pending = pending.FirstOrDefault(x => x.BillId == bill.BillId)?.Amount ?? 0m;
                bill.DaysOverdue = Math.Max(0, (DateTime.Today - bill.DueDate.Date).Days);
            }

            return bills;
        }

        /// <summary>The vendor's posted advances not yet fully applied to bills.</summary>
        public async Task<List<OpenAdvance>> GetOpenAdvancesAsync(int vendorId)
        {
            var advances = await _db.VendorPayments.AsNoTracking()
                .Where(x => x.IsAdvance && x.VendorId == vendorId && x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.Amount > x.AdvanceApplied)
                .OrderBy(x => x.PaymentDate)
                .Select(x => new OpenAdvance
                {
                    PaymentId = x.Id,
                    PaymentNumber = x.PaymentNumber,
                    PaymentDate = x.PaymentDate,
                    Reference = x.Reference,
                    Amount = x.Amount,
                    Applied = x.AdvanceApplied
                })
                .ToListAsync();

            var ids = advances.Select(x => x.PaymentId).ToList();
            var pending = await _db.VendorClearings.AsNoTracking()
                .Where(x => x.AdvancePaymentId != null && ids.Contains(x.AdvancePaymentId.Value) && x.Status == VendorClearingStatuses.Pending)
                .GroupBy(x => x.AdvancePaymentId!.Value)
                .Select(g => new { Id = g.Key, Amount = g.Sum(x => x.Amount) })
                .ToListAsync();

            foreach (var advance in advances)
                advance.Pending = pending.FirstOrDefault(x => x.Id == advance.PaymentId)?.Amount ?? 0m;

            return advances.Where(x => x.Open > 0m).ToList();
        }

        /// <summary>CLR-2026-000001: the next clearing document number of the company for the year.</summary>
        private async Task<string> NextClearingNumberAsync(int companyId, DateTime date)
        {
            var prefix = $"CLR-{date.Year}-";
            var last = await _db.VendorClearings.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.ClearingNumber.StartsWith(prefix))
                .OrderByDescending(x => x.ClearingNumber)
                .Select(x => x.ClearingNumber)
                .FirstOrDefaultAsync();
            var next = last != null && int.TryParse(last[prefix.Length..], out var n) ? n + 1 : 1;
            return $"{prefix}{next:000000}";
        }

        // =====================================================================
        //  PAYMENTS
        // =====================================================================

        public sealed class Allocation
        {
            public int BillId { get; set; }
            public decimal Amount { get; set; }
        }

        public sealed class PaymentRequest
        {
            public int VendorId { get; set; }
            public bool IsAdvance { get; set; }
            public DateTime PaymentDate { get; set; } = DateTime.Today;
            public string PaymentMethod { get; set; } = "Bank";
            public int? PaidFromAccountId { get; set; }
            public string? PaymentNumber { get; set; }
            public string? Reference { get; set; }
            public string? Notes { get; set; }

            /// <summary>The advance amount (for an advance only).</summary>
            public decimal AdvanceAmount { get; set; }

            /// <summary>The open bills paid and how much of each (for a payment of bills).</summary>
            public List<Allocation> Allocations { get; set; } = new();

            public decimal Total => IsAdvance ? AdvanceAmount : Allocations.Sum(x => x.Amount);
        }

        public sealed record Result(bool Ok, string Message, VendorPayment? Payment = null, string? ClearingNumber = null, string? NotPostedReason = null);

        /// <summary>Why the payment cannot be made, or null.</summary>
        public async Task<string?> ValidateAsync(int companyId, PaymentRequest request)
        {
            var vendor = await _db.Vendors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.VendorId);
            if (vendor == null)
                return "Choose the vendor first.";

            if (request.PaidFromAccountId.HasValue)
            {
                var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.PaidFromAccountId.Value);
                if (account == null || !account.IsPostable || account.IsGroup || !account.Code.StartsWith("111"))
                    return "Pay from a cash or bank account.";
            }

            if (!string.IsNullOrWhiteSpace(request.PaymentNumber) &&
                await _db.VendorPayments.AnyAsync(x => x.PaymentNumber == request.PaymentNumber.Trim()))
                return "Payment number already exists.";

            if (request.IsAdvance)
            {
                if (request.AdvanceAmount <= 0m)
                    return "Enter the amount of the advance.";
                if (await _db.Accounts.AnyAsync(x => x.Code == SupplierAdvancesAccountCode) == false)
                    return $"Account {SupplierAdvancesAccountCode} Advances to Suppliers is missing from the chart of accounts.";
                return null;
            }

            // A payment to a vendor always settles open items; money paid ahead of a bill is an advance.
            var lines = request.Allocations.Where(x => x.Amount != 0m).ToList();
            if (lines.Count == 0)
                return "Select at least one open bill to pay. To pay before a bill arrives, record an advance instead.";
            if (lines.Any(x => x.Amount < 0m))
                return "Amounts paid must be positive.";
            if (lines.GroupBy(x => x.BillId).Any(g => g.Count() > 1))
                return "Each bill can appear only once.";

            var open = (await GetOpenItemsAsync(request.VendorId)).ToDictionary(x => x.BillId);
            foreach (var line in lines)
            {
                if (!open.TryGetValue(line.BillId, out var item))
                    return "One of the bills is not an open, posted bill of this vendor.";
                if (line.Amount > item.Open)
                    return $"Bill {item.BillNumber} has {item.Open:N2} open; {line.Amount:N2} cannot be paid against it.";
            }

            return null;
        }

        /// <summary>
        /// Records the payment and its clearing document, builds the entry and posts it (or
        /// sends it for approval when payment approval is on). Call <see cref="ValidateAsync"/> first.
        /// </summary>
        public async Task<Result> PayAsync(int companyId, PaymentRequest request, string userName)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();

            var payment = new VendorPayment
            {
                CompanyId = companyId,
                VendorId = request.VendorId,
                IsAdvance = request.IsAdvance,
                PaymentNumber = string.IsNullOrWhiteSpace(request.PaymentNumber)
                    ? await NextPaymentNumberAsync(request.IsAdvance, request.PaymentDate)
                    : request.PaymentNumber.Trim(),
                PaymentDate = request.PaymentDate.Date,
                Amount = request.Total,
                PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "Bank" : request.PaymentMethod,
                PaidFromAccountId = request.PaidFromAccountId,
                Reference = Trim(request.Reference, 100),
                Notes = Trim(request.Notes, 500)
            };
            _db.VendorPayments.Add(payment);
            await _db.SaveChangesAsync();

            if (!request.IsAdvance)
            {
                var clearing = new VendorClearing
                {
                    CompanyId = companyId,
                    ClearingNumber = await NextClearingNumberAsync(companyId, payment.PaymentDate),
                    ClearingDate = payment.PaymentDate,
                    VendorId = request.VendorId,
                    ClearingType = VendorClearingTypes.Payment,
                    VendorPaymentId = payment.Id,
                    Amount = payment.Amount,
                    Status = VendorClearingStatuses.Pending,
                    CreatedByName = Trim(userName, 200),
                    Lines = request.Allocations.Where(x => x.Amount > 0m)
                        .Select(x => new VendorClearingLine { VendorBillId = x.BillId, Amount = x.Amount }).ToList()
                };
                _db.VendorClearings.Add(clearing);
                payment.ClearingNumber = clearing.ClearingNumber;
                await _db.SaveChangesAsync();
            }

            var journal = await _posting.PostVendorPaymentAsync(payment.Id);
            if (payment.ClearingNumber != null)
            {
                var clearing = await _db.VendorClearings.FirstAsync(x => x.VendorPaymentId == payment.Id);
                clearing.JournalEntryId = journal.Id;
                await _db.SaveChangesAsync();
            }

            await transaction.CommitAsync();

            // "Save and Post" posts through the same checks as Journal Inquiry; with payment
            // approval on, it waits in Payment Approvals and its clearing stays pending.
            var notPosted = await _workflow.PostNewEntryAsync(journal.Id);
            return new Result(true,
                payment.IsAdvance
                    ? $"Advance {payment.PaymentNumber} of {payment.Amount:N2} recorded."
                    : $"Payment {payment.PaymentNumber} of {payment.Amount:N2} recorded, clearing document {payment.ClearingNumber}.",
                payment, payment.ClearingNumber, notPosted);
        }

        // =====================================================================
        //  APPLYING AN ADVANCE (SAP F-54)
        // =====================================================================

        public async Task<string?> ValidateAdvanceApplicationAsync(int advancePaymentId, List<Allocation> allocations)
        {
            var advance = await _db.VendorPayments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == advancePaymentId && x.IsAdvance);
            if (advance?.VendorId == null)
                return "Choose an advance of this vendor.";

            var open = (await GetOpenAdvancesAsync(advance.VendorId.Value)).FirstOrDefault(x => x.PaymentId == advancePaymentId);
            if (open == null)
                return $"Advance {advance.PaymentNumber} is not posted or has nothing left to apply.";

            var lines = allocations.Where(x => x.Amount != 0m).ToList();
            if (lines.Count == 0)
                return "Select at least one open bill to settle from the advance.";
            if (lines.Any(x => x.Amount < 0m))
                return "Amounts must be positive.";
            if (lines.Sum(x => x.Amount) > open.Open)
                return $"Advance {advance.PaymentNumber} has {open.Open:N2} left to apply.";

            var bills = (await GetOpenItemsAsync(advance.VendorId.Value)).ToDictionary(x => x.BillId);
            foreach (var line in lines)
            {
                if (!bills.TryGetValue(line.BillId, out var item))
                    return "One of the bills is not an open, posted bill of this vendor.";
                if (line.Amount > item.Open)
                    return $"Bill {item.BillNumber} has {item.Open:N2} open; {line.Amount:N2} cannot be settled against it.";
            }

            return null;
        }

        public async Task<Result> ApplyAdvanceAsync(int companyId, int advancePaymentId, List<Allocation> allocations, DateTime date, string userName)
        {
            var advance = await _db.VendorPayments.FirstAsync(x => x.Id == advancePaymentId);
            var lines = allocations.Where(x => x.Amount > 0m).ToList();

            await using var transaction = await _db.Database.BeginTransactionAsync();
            var clearing = new VendorClearing
            {
                CompanyId = companyId,
                ClearingNumber = await NextClearingNumberAsync(companyId, date),
                ClearingDate = date.Date,
                VendorId = advance.VendorId!.Value,
                ClearingType = VendorClearingTypes.Advance,
                AdvancePaymentId = advance.Id,
                Amount = lines.Sum(x => x.Amount),
                Status = VendorClearingStatuses.Pending,
                CreatedByName = Trim(userName, 200),
                Lines = lines.Select(x => new VendorClearingLine { VendorBillId = x.BillId, Amount = x.Amount }).ToList()
            };
            _db.VendorClearings.Add(clearing);
            await _db.SaveChangesAsync();

            var journal = await _posting.PostVendorAdvanceClearingAsync(clearing.Id);
            await transaction.CommitAsync();

            var notPosted = await _workflow.PostNewEntryAsync(journal.Id);
            return new Result(true, $"Advance {advance.PaymentNumber} applied: {clearing.Amount:N2}, clearing document {clearing.ClearingNumber}.",
                advance, clearing.ClearingNumber, notPosted);
        }

        private async Task<string> NextPaymentNumberAsync(bool advance, DateTime date)
        {
            var prefix = (advance ? "VADV-" : "VPAY-") + date.ToString("yyyyMMdd") + "-";
            var count = await _db.VendorPayments.CountAsync(x => x.PaymentNumber.StartsWith(prefix)) + 1;
            return $"{prefix}{count:D3}";
        }

        private static string? Trim(string? value, int max) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];
    }
}
