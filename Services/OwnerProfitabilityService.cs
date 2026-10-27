using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Profitability of each managed property for a period, seen from both sides: what
    /// the property earned its OWNER (rent less our commission, its VAT and the costs
    /// charged to the owner) and what it earned the COMPANY (the commission).
    /// </summary>
    public class OwnerProfitabilityService
    {
        private readonly ApplicationDbContext _db;

        public OwnerProfitabilityService(ApplicationDbContext db)
        {
            _db = db;
        }

        public sealed class Row
        {
            public int PropertyId { get; set; }
            public string PropertyCode { get; set; } = string.Empty;
            public string PropertyName { get; set; } = string.Empty;
            public string OwnerName { get; set; } = string.Empty;
            public string? OwnerTrn { get; set; }
            public bool BilledInOwnerName { get; set; }
            public decimal CommissionPercentage { get; set; }
            public int Units { get; set; }
            public int Let { get; set; }

            public decimal RentBilled { get; set; }
            public decimal RentVatForOwner { get; set; }
            public decimal Commission { get; set; }
            public decimal CommissionVat { get; set; }
            public decimal Expenses { get; set; }

            /// <summary>What the property earned its owner in the period.</summary>
            public decimal OwnerNetIncome => RentBilled - Commission - CommissionVat - Expenses;
            public decimal? OwnerMarginPercent => RentBilled == 0m ? null : Math.Round(OwnerNetIncome / RentBilled * 100m, 1);
        }

        public sealed class Report
        {
            public DateTime From { get; set; }
            public DateTime To { get; set; }
            public string? Owner { get; set; }
            public List<string> Owners { get; set; } = new();
            public List<Row> Rows { get; set; } = new();
            public decimal CommissionVatRate { get; set; }
        }

        public async Task<Report> GetAsync(int companyId, DateTime from, DateTime to, string? owner)
        {
            var company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId);
            var vatRate = OwnerCommissionInvoiceService.CommissionVatRate(company);

            var properties = await _db.Properties.AsNoTracking()
                .Where(x => x.OwnershipType == "Managed" && x.OwnerName != null)
                .OrderBy(x => x.Code)
                .ToListAsync();
            var report = new Report
            {
                From = from, To = to, Owner = owner, CommissionVatRate = vatRate,
                Owners = properties.Select(x => x.OwnerName!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList()
            };
            if (!string.IsNullOrWhiteSpace(owner))
                properties = properties.Where(x => string.Equals(x.OwnerName!.Trim(), owner.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (properties.Count == 0)
                return report;

            var ids = properties.Select(x => x.Id).ToList();

            var invoices = await _db.CustomerInvoices.AsNoTracking()
                .Where(x => ids.Contains(x.Lease!.Unit!.PropertyId) && x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.Status != "Cancelled" && x.InvoiceDate >= from && x.InvoiceDate <= to)
                .GroupBy(x => x.Lease!.Unit!.PropertyId)
                .Select(g => new { PropertyId = g.Key, Net = g.Sum(x => x.Subtotal), OwnerVat = g.Sum(x => x.IssuedForOwnerName != null ? x.VatAmount : 0m) })
                .ToListAsync();

            var credits = await _db.CustomerCreditNotes.AsNoTracking()
                .Where(x => ids.Contains(x.CustomerInvoice!.Lease!.Unit!.PropertyId) && x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.CreditNoteDate >= from && x.CreditNoteDate <= to)
                .GroupBy(x => x.CustomerInvoice!.Lease!.Unit!.PropertyId)
                .Select(g => new { PropertyId = g.Key, Net = g.Sum(x => x.Subtotal), OwnerVat = g.Sum(x => x.CustomerInvoice!.IssuedForOwnerName != null ? x.VatAmount : 0m) })
                .ToListAsync();

            var dueToLandlords = await _db.CompanyAccountSettings.AsNoTracking()
                .Where(x => x.CompanyId == companyId).Select(x => x.DueToLandlordsAccountId).FirstOrDefaultAsync();
            var chargeSources = new[] { "MaintenanceMaterial", "VendorBill", "PettyCashExpense", "ManualJournal" };
            var expenses = dueToLandlords == null
                ? new Dictionary<int, decimal>()
                : await _db.JournalEntryLines.AsNoTracking()
                    .Where(x => x.AccountId == dueToLandlords.Value && x.Debit > 0m && x.PropertyId != null && ids.Contains(x.PropertyId.Value) &&
                                x.JournalEntry!.IsPosted && x.JournalEntry.EntryDate >= from && x.JournalEntry.EntryDate <= to &&
                                chargeSources.Contains(x.JournalEntry.SourceType!))
                    .GroupBy(x => x.PropertyId!.Value)
                    .Select(g => new { PropertyId = g.Key, Amount = g.Sum(x => x.Debit - x.Credit) })
                    .ToDictionaryAsync(x => x.PropertyId, x => x.Amount);

            var units = await _db.Units.AsNoTracking()
                .Where(x => ids.Contains(x.PropertyId) && x.IsActive)
                .GroupBy(x => x.PropertyId)
                .Select(g => new { PropertyId = g.Key, Units = g.Count(), Let = g.Count(u => u.Status == "Occupied") })
                .ToDictionaryAsync(x => x.PropertyId);

            foreach (var p in properties)
            {
                var inv = invoices.FirstOrDefault(x => x.PropertyId == p.Id);
                var cr = credits.FirstOrDefault(x => x.PropertyId == p.Id);
                var rent = (inv?.Net ?? 0m) - (cr?.Net ?? 0m);
                var commission = Math.Round(rent * p.CommissionPercentage / 100m, 2);
                report.Rows.Add(new Row
                {
                    PropertyId = p.Id,
                    PropertyCode = p.Code,
                    PropertyName = p.Name,
                    OwnerName = p.OwnerName!.Trim(),
                    OwnerTrn = p.OwnerTrn,
                    BilledInOwnerName = p.BillsInOwnerName,
                    CommissionPercentage = p.CommissionPercentage,
                    Units = units.TryGetValue(p.Id, out var u) ? u.Units : 0,
                    Let = units.TryGetValue(p.Id, out var l) ? l.Let : 0,
                    RentBilled = rent,
                    RentVatForOwner = (inv?.OwnerVat ?? 0m) - (cr?.OwnerVat ?? 0m),
                    Commission = commission,
                    CommissionVat = VatDeterminationService.Round(commission * vatRate / 100m),
                    Expenses = expenses.TryGetValue(p.Id, out var e) ? e : 0m
                });
            }

            return report;
        }
    }
}
