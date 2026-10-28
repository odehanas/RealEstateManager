using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;

namespace RealEstateManager.Services
{
    /// <summary>
    /// General Journal report: every journal entry in a period with its lines, and the
    /// source each entry came from (the document type, its number and, where the app has
    /// a page for it, a link to that document via DocumentsController).
    /// </summary>
    public sealed class GeneralJournalReportService
    {
        public const string StatusPosted = "Posted";
        public const string StatusDraft = "Draft";
        public const string StatusAll = "All";

        private readonly ApplicationDbContext _db;

        public GeneralJournalReportService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<GeneralJournalReport> GetAsync(
            DateTime from, DateTime to, string? source, string? status, string? search)
        {
            status = status is StatusDraft or StatusAll ? status : StatusPosted;

            var query = _db.JournalEntries.AsNoTracking()
                .Where(x => x.EntryDate >= from && x.EntryDate <= to);

            if (status == StatusPosted)
                query = query.Where(x => x.IsPosted);
            else if (status == StatusDraft)
                query = query.Where(x => !x.IsPosted);

            if (source == ManualSourceKey)
                query = query.Where(x => x.SourceType == null || x.SourceType == "");
            else if (!string.IsNullOrWhiteSpace(source))
                query = query.Where(x => x.SourceType == source);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(x =>
                    x.EntryNumber.Contains(term) ||
                    (x.DocumentNumber != null && x.DocumentNumber.Contains(term)) ||
                    (x.ReferenceNumber != null && x.ReferenceNumber.Contains(term)) ||
                    x.Description.Contains(term));
            }

            var entries = await query
                .Include(x => x.AccountingPeriod)
                .Include(x => x.Lines).ThenInclude(x => x.Account)
                .Include(x => x.Lines).ThenInclude(x => x.Property)
                .OrderBy(x => x.EntryDate).ThenBy(x => x.Id)
                .AsSplitQuery()
                .ToListAsync();

            var rows = entries.Select(x => new GeneralJournalEntryRow
            {
                Id = x.Id,
                EntryNumber = x.EntryNumber,
                EntryDate = x.EntryDate,
                PeriodName = x.AccountingPeriod?.Name,
                JournalType = x.JournalType,
                SourceType = string.IsNullOrWhiteSpace(x.SourceType) ? null : x.SourceType,
                SourceLabel = SourceLabel(x.SourceType),
                SourceId = x.SourceId,
                DocumentNumber = x.DocumentNumber,
                ReferenceNumber = x.ReferenceNumber,
                Description = x.Description,
                Status = x.ReversalJournalEntryId != null ? "Reversed" : x.IsPosted ? StatusPosted : StatusDraft,
                PostedBy = x.PostedBy,
                PostedAt = x.PostedAt,
                Lines = x.Lines
                    .OrderByDescending(l => l.Debit > 0).ThenBy(l => l.Id)
                    .Select(l => new GeneralJournalLineRow
                    {
                        AccountCode = l.Account?.Code ?? string.Empty,
                        AccountName = l.Account?.Name ?? string.Empty,
                        Description = l.Description,
                        Property = l.Property?.Name,
                        Debit = l.Debit,
                        Credit = l.Credit
                    })
                    .ToList()
            }).ToList();

            return new GeneralJournalReport
            {
                From = from,
                To = to,
                Source = source,
                Status = status,
                Search = search,
                Entries = rows,
                BySource = rows
                    .GroupBy(x => x.SourceLabel)
                    .Select(g => new GeneralJournalSourceTotal
                    {
                        SourceLabel = g.Key,
                        Entries = g.Count(),
                        Debit = g.Sum(e => e.TotalDebit),
                        Credit = g.Sum(e => e.TotalCredit)
                    })
                    .OrderByDescending(x => x.Entries)
                    .ToList()
            };
        }

        /// <summary>The sources present in the company's journal, for the filter list.</summary>
        public async Task<List<(string Key, string Label)>> SourcesAsync()
        {
            var types = await _db.JournalEntries.AsNoTracking()
                .Select(x => x.SourceType)
                .Distinct()
                .ToListAsync();

            return types
                .Select(t => string.IsNullOrWhiteSpace(t) ? (ManualSourceKey, SourceLabel(null)) : (t!, SourceLabel(t)))
                .Distinct()
                .OrderBy(x => x.Item2)
                .ToList();
        }

        /// <summary>Filter value for entries with no source document.</summary>
        public const string ManualSourceKey = "(none)";

        public static string SourceLabel(string? sourceType) => sourceType switch
        {
            null or "" => "Journal (no source document)",
            "ManualJournal" => "Manual journal",
            "CustomerInvoice" => "Customer invoice",
            "CustomerReceipt" => "Customer receipt",
            "CustomerCreditNote" => "Customer credit note",
            "VendorBill" => "Supplier bill",
            "VendorPayment" => "Supplier payment",
            VendorClearingService.AdvanceSourceType => "Supplier advance applied",
            "RevenueRecognitionSchedule" => "Rent revenue recognition",
            "PrepaymentSchedule" => "Prepayment",
            "PrepaymentRecognition" => "Prepayment amortisation",
            "SecurityDeposit" => "Security deposit",
            "PdcReceiptMemo" => "PDC received",
            "PdcClearance" => "PDC cleared",
            "PdcDeposit" => "PDC deposited to bank",
            "PdcBounce" => "PDC bounced",
            "PdcWithdrawal" => "PDC withdrawn by tenant",
            "PettyCashExpense" => "Petty cash expense",
            "PettyCashReplenishment" => "Petty cash top-up",
            "FixedAssetAcquisition" => "Fixed asset purchase",
            "FixedAssetCapitalization" => "Fixed asset capitalisation",
            "FixedAssetDepreciation" => "Depreciation",
            "OwnerSettlement" => "Owner settlement",
            "OwnerCommissionInvoice" => "Commission tax invoice",
            "OwnerWithdrawal" => "Owner withdrawal",
            "InvestmentPropertySetup" => "Investment property allocation",
            "DevelopmentCost" => "Development cost",
            "DevelopmentTransfer" => "Development completion transfer",
            "UnitSaleReceipt" => "Unit sale payment",
            "UnitSaleHandover" => "Unit handover",
            "CapitalAssetVatAdjustment" => "Capital asset VAT adjustment",
            "PayrollRun" => "Payroll",
            "PayrollPayment" => "Salary payment",
            "EmployeeAdvance" => "Salary advance",
            "FiscalYearClosing" => "Year-end closing",
            _ => sourceType
        };
    }

    public sealed class GeneralJournalReport
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string? Source { get; set; }
        public string Status { get; set; } = GeneralJournalReportService.StatusPosted;
        public string? Search { get; set; }
        public List<GeneralJournalEntryRow> Entries { get; set; } = new();
        public List<GeneralJournalSourceTotal> BySource { get; set; } = new();
        public decimal TotalDebit => Entries.Sum(x => x.TotalDebit);
        public decimal TotalCredit => Entries.Sum(x => x.TotalCredit);
    }

    public sealed class GeneralJournalEntryRow
    {
        public int Id { get; set; }
        public string EntryNumber { get; set; } = string.Empty;
        public DateTime EntryDate { get; set; }
        public string? PeriodName { get; set; }
        public string JournalType { get; set; } = string.Empty;
        public string? SourceType { get; set; }
        public string SourceLabel { get; set; } = string.Empty;
        public int? SourceId { get; set; }
        public string? DocumentNumber { get; set; }
        public string? ReferenceNumber { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? PostedBy { get; set; }
        public DateTime? PostedAt { get; set; }
        public List<GeneralJournalLineRow> Lines { get; set; } = new();
        public decimal TotalDebit => Lines.Sum(x => x.Debit);
        public decimal TotalCredit => Lines.Sum(x => x.Credit);
    }

    public sealed class GeneralJournalLineRow
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Property { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    public sealed class GeneralJournalSourceTotal
    {
        public string SourceLabel { get; set; } = string.Empty;
        public int Entries { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }
}
