using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Project Costs &amp; WIP: what each development project has cost, split into the
    /// supplier's net price, the VAT capitalised into the cost and the VAT recovered;
    /// what is committed but not yet posted; what has been paid; and a reconciliation of
    /// every work-in-progress account in the ledger to the projects that make it up.
    ///
    /// The ledger is the authority: posted cost is read from journal lines tagged to the
    /// project on its WIP account. Bills supply the VAT split and the paid position.
    /// </summary>
    public class ProjectCostReportService
    {
        private readonly ApplicationDbContext _db;
        private readonly FinancialReportService _reports;

        public ProjectCostReportService(ApplicationDbContext db, FinancialReportService reports)
        {
            _db = db;
            _reports = reports;
        }

        public sealed class ProjectRow
        {
            public int Id { get; set; }
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string IntendedUse { get; set; } = string.Empty;
            public string WipAccount { get; set; } = string.Empty;
            public int WipAccountId { get; set; }
            public string? VatTreatment { get; set; }

            public decimal Budget { get; set; }
            /// <summary>Posted cost to date on the WIP account (before transfers out).</summary>
            public decimal PostedCost { get; set; }
            public decimal BillNet { get; set; }
            public decimal BillCapitalisedVat { get; set; }
            public decimal BillRecoverableVat { get; set; }
            /// <summary>Posted cost that did not come from vendor bills: journals, petty cash, finance costs.</summary>
            public decimal OtherCost => PostedCost - BillNet - BillCapitalisedVat;
            public decimal Transferred { get; set; }
            public decimal WipBalance => PostedCost - Transferred;
            /// <summary>Draft bills and journals tagged to the project, not yet in the accounts.</summary>
            public decimal Committed { get; set; }
            public decimal Paid { get; set; }
            public decimal Unpaid { get; set; }
            public decimal BudgetRemaining => Budget - PostedCost - Committed;
        }

        public sealed record CategoryRow(string Category, decimal Budget, decimal Posted, decimal Committed)
        {
            public decimal Remaining => Budget - Posted - Committed;
        }

        public sealed class VendorRow
        {
            public string Vendor { get; set; } = string.Empty;
            public bool IsMainContractor { get; set; }
            public int Bills { get; set; }
            public decimal Net { get; set; }
            public decimal CapitalisedVat { get; set; }
            public decimal RecoverableVat { get; set; }
            public decimal Total => Net + CapitalisedVat + RecoverableVat;
            public decimal Paid { get; set; }
            public decimal Unpaid => Total - Paid;
        }

        public sealed class BillRow
        {
            public int BillId { get; set; }
            public string BillNumber { get; set; } = string.Empty;
            public string? VendorReference { get; set; }
            public string Vendor { get; set; } = string.Empty;
            public DateTime Date { get; set; }
            public string Status { get; set; } = string.Empty;
            public bool Posted { get; set; }
            public decimal Net { get; set; }
            public decimal CapitalisedVat { get; set; }
            public decimal RecoverableVat { get; set; }
            public decimal Total => Net + CapitalisedVat + RecoverableVat;
            public decimal Paid { get; set; }
        }

        public sealed class ReconciliationRow
        {
            public int AccountId { get; set; }
            public string Account { get; set; } = string.Empty;
            public decimal LedgerBalance { get; set; }
            public decimal ProjectsTotal { get; set; }
            public decimal Untagged { get; set; }
            public decimal Difference => LedgerBalance - ProjectsTotal;
            /// <summary>Opening balances and anything else not explained by untagged postings.</summary>
            public decimal Unexplained => Difference - Untagged;
            public List<UntaggedLine> UntaggedLines { get; set; } = new();
        }

        public sealed record UntaggedLine(DateTime Date, string EntryNumber, string? Source, string? Description, decimal Amount);

        public sealed record MonthRow(DateTime Month, decimal Amount);

        public sealed class ReportModel
        {
            public DateTime AsOf { get; set; }
            public string Currency { get; set; } = "AED";
            public List<ProjectRow> Projects { get; set; } = new();
            public List<ReconciliationRow> Reconciliation { get; set; } = new();

            public ProjectRow? Project { get; set; }
            public string? MainContractor { get; set; }
            public List<CategoryRow> Categories { get; set; } = new();
            public List<VendorRow> Vendors { get; set; } = new();
            public List<BillRow> Bills { get; set; } = new();
            public List<MonthRow> Months { get; set; } = new();
        }

        private sealed record BillLineFact(
            int ProjectId, int BillId, string BillNumber, string? VendorReference, int VendorId, string Vendor, DateTime Date,
            string Status, bool Posted, decimal BillTotal, decimal BillPaid, string? Category,
            decimal Net, decimal Vat, decimal Recoverable, decimal NonRecoverable);

        public async Task<ReportModel> BuildAsync(int companyId, DateTime asOf, int? projectId)
        {
            asOf = asOf.Date;
            var end = asOf.AddDays(1);
            var model = new ReportModel
            {
                AsOf = asOf,
                Currency = await _db.Companies.Where(x => x.Id == companyId).Select(x => x.BaseCurrency).FirstOrDefaultAsync() ?? "AED"
            };

            var projects = await _db.DevelopmentProjects.AsNoTracking()
                .Include(x => x.WipAccount).Include(x => x.BudgetLines).Include(x => x.MainContractorVendor)
                .OrderBy(x => x.Code).ToListAsync();

            // ---- the ledger: every posted line tagged to a project on its own WIP account
            var ledger = await _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.DevelopmentProjectId != null && x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.JournalEntry.EntryDate < end &&
                            x.DevelopmentProject != null && x.AccountId == x.DevelopmentProject.WipAccountId)
                .Select(x => new
                {
                    ProjectId = x.DevelopmentProjectId!.Value,
                    x.DevelopmentCostCategory,
                    IsTransfer = x.JournalEntry!.SourceType == DevelopmentTransferService.SourceType,
                    x.JournalEntry.EntryDate,
                    Amount = x.Debit - x.Credit
                })
                .ToListAsync();

            // ---- draft journal lines (manual journals and the like) not yet posted
            var draftJournals = await _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.DevelopmentProjectId != null && x.JournalEntry != null && !x.JournalEntry.IsPosted &&
                            x.JournalEntry.SourceType != DevelopmentTransferService.SourceType &&
                            x.JournalEntry.SourceType != "VendorBill")
                .Select(x => new { ProjectId = x.DevelopmentProjectId!.Value, x.DevelopmentCostCategory, Amount = x.Debit - x.Credit })
                .ToListAsync();

            // ---- bill lines tagged to a project: the VAT split, drafts and payments
            var bills = await _db.VendorBillLines.AsNoTracking()
                .Where(x => x.DevelopmentProjectId != null && x.VendorBill != null && x.VendorBill.Status != "Cancelled" &&
                            (x.VendorBill.AccountingDate == default ? x.VendorBill.BillDate : x.VendorBill.AccountingDate) < end)
                .Select(x => new BillLineFact(
                    x.DevelopmentProjectId!.Value, x.VendorBillId, x.VendorBill!.BillNumber, x.VendorBill.VendorReference,
                    x.VendorBill.VendorId, x.VendorBill.Vendor!.Name,
                    x.VendorBill.AccountingDate == default ? x.VendorBill.BillDate : x.VendorBill.AccountingDate,
                    x.VendorBill.Status, x.VendorBill.JournalEntry != null && x.VendorBill.JournalEntry.IsPosted,
                    x.VendorBill.TotalAmount, x.VendorBill.AmountPaid, x.DevelopmentCostCategory,
                    x.LineSubtotal, x.VatAmount, x.RecoverableVatAmount, x.NonRecoverableVatAmount))
                .ToListAsync();

            // A bill's payments are shared across its lines in proportion to each line's total.
            decimal PaidShare(BillLineFact f) =>
                !f.Posted || f.BillTotal == 0m ? 0m : Math.Round(f.BillPaid * (f.Net + f.Vat) / f.BillTotal, 2);

            foreach (var p in projects)
            {
                var mine = ledger.Where(x => x.ProjectId == p.Id).ToList();
                var myBills = bills.Where(x => x.ProjectId == p.Id).ToList();
                var posted = myBills.Where(x => x.Posted).ToList();
                var drafts = myBills.Where(x => !x.Posted).ToList();

                model.Projects.Add(new ProjectRow
                {
                    Id = p.Id,
                    Code = p.Code,
                    Name = p.Name,
                    Status = DevelopmentProjectStatuses.Describe(p.Status),
                    IntendedUse = DevelopmentIntendedUses.Describe(p.IntendedUse),
                    WipAccountId = p.WipAccountId,
                    WipAccount = p.WipAccount == null ? string.Empty : p.WipAccount.Code + " " + p.WipAccount.Name,
                    VatTreatment = p.InputTaxAttribution,
                    Budget = p.BudgetLines.Sum(x => x.Amount),
                    PostedCost = mine.Where(x => !x.IsTransfer).Sum(x => x.Amount),
                    Transferred = -mine.Where(x => x.IsTransfer).Sum(x => x.Amount),
                    BillNet = posted.Sum(x => x.Net),
                    BillCapitalisedVat = posted.Sum(x => x.NonRecoverable),
                    BillRecoverableVat = posted.Sum(x => x.Recoverable),
                    Committed = drafts.Sum(x => x.Net + x.NonRecoverable) + draftJournals.Where(x => x.ProjectId == p.Id).Sum(x => x.Amount),
                    Paid = posted.Sum(PaidShare),
                    Unpaid = posted.Sum(x => x.Net + x.Vat) - posted.Sum(PaidShare)
                });
            }

            // ---- reconcile every WIP account the projects use to the ledger
            var trialBalance = await _reports.GetTrialBalanceAsync(companyId, asOf);
            foreach (var accountId in projects.Select(x => x.WipAccountId).Distinct())
            {
                var untagged = await _db.JournalEntryLines.AsNoTracking()
                    .Where(x => x.AccountId == accountId && x.DevelopmentProjectId == null &&
                                x.JournalEntry != null && x.JournalEntry.IsPosted && x.JournalEntry.EntryDate < end)
                    .OrderBy(x => x.JournalEntry!.EntryDate)
                    .Select(x => new UntaggedLine(x.JournalEntry!.EntryDate, x.JournalEntry.EntryNumber, x.JournalEntry.SourceType, x.Description, x.Debit - x.Credit))
                    .ToListAsync();

                var row = trialBalance.Rows.FirstOrDefault(x => x.AccountId == accountId);
                var account = projects.First(x => x.WipAccountId == accountId).WipAccount;
                model.Reconciliation.Add(new ReconciliationRow
                {
                    AccountId = accountId,
                    Account = account == null ? accountId.ToString() : account.Code + " " + account.Name,
                    LedgerBalance = row?.NetBalance ?? 0m,
                    ProjectsTotal = model.Projects.Where(x => x.WipAccountId == accountId).Sum(x => x.WipBalance),
                    Untagged = untagged.Sum(x => x.Amount),
                    UntaggedLines = untagged.TakeLast(50).ToList()
                });
            }

            // ---- one project in detail
            if (projectId.HasValue)
            {
                var project = projects.FirstOrDefault(x => x.Id == projectId.Value);
                model.Project = model.Projects.FirstOrDefault(x => x.Id == projectId.Value);
                if (project != null && model.Project != null)
                {
                    model.MainContractor = project.MainContractorVendor?.Name;
                    var mine = ledger.Where(x => x.ProjectId == project.Id && !x.IsTransfer).ToList();
                    var myBills = bills.Where(x => x.ProjectId == project.Id).ToList();
                    var myDraftJournals = draftJournals.Where(x => x.ProjectId == project.Id).ToList();

                    model.Categories = DevelopmentCostCategories.All
                        .Select(c => new CategoryRow(
                            DevelopmentCostCategories.Describe(c),
                            project.BudgetLines.Where(b => b.CostCategory == c).Sum(b => b.Amount),
                            mine.Where(l => l.DevelopmentCostCategory == c).Sum(l => l.Amount),
                            myBills.Where(b => !b.Posted && b.Category == c).Sum(b => b.Net + b.NonRecoverable) +
                            myDraftJournals.Where(j => j.DevelopmentCostCategory == c).Sum(j => j.Amount)))
                        .ToList();
                    var uncategorised = mine.Where(l => !DevelopmentCostCategories.All.Contains(l.DevelopmentCostCategory ?? "")).Sum(l => l.Amount);
                    if (uncategorised != 0m)
                        model.Categories.Add(new CategoryRow("(no category)", 0m, uncategorised, 0m));

                    model.Vendors = myBills.Where(x => x.Posted)
                        .GroupBy(x => new { x.VendorId, x.Vendor })
                        .Select(g => new VendorRow
                        {
                            Vendor = g.Key.Vendor,
                            IsMainContractor = g.Key.VendorId == project.MainContractorVendorId,
                            Bills = g.Select(x => x.BillId).Distinct().Count(),
                            Net = g.Sum(x => x.Net),
                            CapitalisedVat = g.Sum(x => x.NonRecoverable),
                            RecoverableVat = g.Sum(x => x.Recoverable),
                            Paid = g.Sum(PaidShare)
                        })
                        .OrderByDescending(x => x.Total)
                        .ToList();

                    model.Bills = myBills
                        .GroupBy(x => x.BillId)
                        .Select(g => new BillRow
                        {
                            BillId = g.Key,
                            BillNumber = g.First().BillNumber,
                            VendorReference = g.First().VendorReference,
                            Vendor = g.First().Vendor,
                            Date = g.First().Date,
                            Status = g.First().Status,
                            Posted = g.First().Posted,
                            Net = g.Sum(x => x.Net),
                            CapitalisedVat = g.Sum(x => x.NonRecoverable),
                            RecoverableVat = g.Sum(x => x.Recoverable),
                            Paid = g.Sum(PaidShare)
                        })
                        .OrderByDescending(x => x.Date)
                        .ToList();

                    model.Months = mine
                        .GroupBy(x => new DateTime(x.EntryDate.Year, x.EntryDate.Month, 1))
                        .OrderBy(g => g.Key)
                        .Select(g => new MonthRow(g.Key, g.Sum(x => x.Amount)))
                        .ToList();
                }
            }

            return model;
        }
    }
}
