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

            /// <summary>VAT on costs charged to the owner: included in Expenses unless the owner reclaims it.</summary>
            public decimal RechargeVat { get; set; }

            /// <summary>The owner's costs by kind (maintenance, utilities...), before reclaimable VAT.</summary>
            public Dictionary<string, decimal> CostsByCategory { get; set; } = new();

            /// <summary>What the property earned its owner in the period.</summary>
            public decimal OwnerNetIncome => RentBilled - Commission - CommissionVat - Expenses;
            public decimal? OwnerMarginPercent => RentBilled == 0m ? null : Math.Round(OwnerNetIncome / RentBilled * 100m, 1);
        }

        /// <summary>
        /// One unit of a managed property, seen from its owner's side. The line with no
        /// unit holds the costs charged for the building as a whole.
        /// </summary>
        public sealed class UnitRow
        {
            public int PropertyId { get; set; }
            public string PropertyCode { get; set; } = string.Empty;
            public string PropertyName { get; set; } = string.Empty;
            public int? UnitId { get; set; }
            public string? UnitNumber { get; set; }
            public decimal RentBilled { get; set; }
            public decimal Commission { get; set; }
            public decimal CommissionVat { get; set; }
            public decimal Expenses { get; set; }
            public decimal OwnerNetIncome => RentBilled - Commission - CommissionVat - Expenses;
            public decimal? OwnerMarginPercent => RentBilled == 0m ? null : Math.Round(OwnerNetIncome / RentBilled * 100m, 1);
        }

        public sealed class Report
        {
            /// <summary>The same figures unit by unit; each property's units add up to its row.</summary>
            public List<UnitRow> Units { get; set; } = new();

            public DateTime From { get; set; }
            public DateTime To { get; set; }
            public string? Owner { get; set; }
            public List<string> Owners { get; set; } = new();
            public List<Row> Rows { get; set; } = new();
            public decimal CommissionVatRate { get; set; }

            /// <summary>Every cost category with an amount, in the standard order.</summary>
            public List<string> Categories => OwnerCostCategories.All.Append(OwnerCostCategories.Vat)
                .Where(c => Rows.Any(r => r.CostsByCategory.GetValueOrDefault(c) != 0m)).ToList();
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
                            x.ChargeType == null &&   // cheque charges are the company's income, not the owner's rent
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
            // Costs charged to owners, by property and by kind (the OwnerCostCategory dimension
            // on the journal line). Lines with no kind - manual journals - count as Other.
            var costLines = dueToLandlords == null
                ? new List<(int PropertyId, string Category, decimal Amount)>()
                : (await _db.JournalEntryLines.AsNoTracking()
                    .Where(x => x.AccountId == dueToLandlords.Value && x.Debit > 0m && x.PropertyId != null && ids.Contains(x.PropertyId.Value) &&
                                x.JournalEntry!.IsPosted && x.JournalEntry.EntryDate >= from && x.JournalEntry.EntryDate <= to &&
                                chargeSources.Contains(x.JournalEntry.SourceType!))
                    .GroupBy(x => new { PropertyId = x.PropertyId!.Value, x.OwnerCostCategory })
                    .Select(g => new { g.Key.PropertyId, g.Key.OwnerCostCategory, Amount = g.Sum(x => x.Debit - x.Credit) })
                    .ToListAsync())
                    .Select(x => (x.PropertyId, Category: x.OwnerCostCategory ?? OwnerCostCategories.Other, x.Amount))
                    .ToList();

            // ---- The same, unit by unit -------------------------------------------
            var unitRent = (await _db.CustomerInvoices.AsNoTracking()
                    .Where(x => ids.Contains(x.Lease!.Unit!.PropertyId) && x.JournalEntry != null && x.JournalEntry.IsPosted &&
                                x.ChargeType == null && x.Status != "Cancelled" && x.InvoiceDate >= from && x.InvoiceDate <= to)
                    .GroupBy(x => x.Lease!.UnitId)
                    .Select(g => new { UnitId = g.Key, Net = g.Sum(x => x.Subtotal) })
                    .ToListAsync())
                .ToDictionary(x => x.UnitId, x => x.Net);
            var unitCredits = (await _db.CustomerCreditNotes.AsNoTracking()
                    .Where(x => ids.Contains(x.CustomerInvoice!.Lease!.Unit!.PropertyId) && x.JournalEntry != null && x.JournalEntry.IsPosted &&
                                x.CreditNoteDate >= from && x.CreditNoteDate <= to)
                    .GroupBy(x => x.CustomerInvoice!.Lease!.UnitId)
                    .Select(g => new { UnitId = g.Key, Net = g.Sum(x => x.Subtotal) })
                    .ToListAsync())
                .ToDictionary(x => x.UnitId, x => x.Net);
            var unitCosts = dueToLandlords == null
                ? new List<(int PropertyId, int? UnitId, string Category, decimal Amount)>()
                : (await _db.JournalEntryLines.AsNoTracking()
                    .Where(x => x.AccountId == dueToLandlords.Value && x.Debit > 0m && x.PropertyId != null && ids.Contains(x.PropertyId.Value) &&
                                x.JournalEntry!.IsPosted && x.JournalEntry.EntryDate >= from && x.JournalEntry.EntryDate <= to &&
                                chargeSources.Contains(x.JournalEntry.SourceType!))
                    .GroupBy(x => new { PropertyId = x.PropertyId!.Value, x.UnitId, x.OwnerCostCategory })
                    .Select(g => new { g.Key.PropertyId, g.Key.UnitId, g.Key.OwnerCostCategory, Amount = g.Sum(x => x.Debit - x.Credit) })
                    .ToListAsync())
                    .Select(x => (x.PropertyId, x.UnitId, Category: x.OwnerCostCategory ?? OwnerCostCategories.Other, x.Amount))
                    .ToList();
            var unitNames = await _db.Units.AsNoTracking()
                .Where(x => ids.Contains(x.PropertyId))
                .Select(x => new { x.Id, x.PropertyId, x.UnitNumber })
                .ToListAsync();

            foreach (var p in properties)
            {
                // A registered owner reclaims the VAT on costs charged to them; for anyone else it is a cost.
                bool Counts(string category) => category != OwnerCostCategories.Vat || string.IsNullOrWhiteSpace(p.OwnerTrn);

                foreach (var unit in unitNames.Where(x => x.PropertyId == p.Id).OrderBy(x => x.UnitNumber))
                {
                    var rent = unitRent.GetValueOrDefault(unit.Id) - unitCredits.GetValueOrDefault(unit.Id);
                    var costs = unitCosts.Where(x => x.PropertyId == p.Id && x.UnitId == unit.Id && Counts(x.Category)).Sum(x => x.Amount);
                    if (rent == 0m && costs == 0m)
                        continue;
                    var commission = Math.Round(rent * p.CommissionPercentage / 100m, 2);
                    report.Units.Add(new UnitRow
                    {
                        PropertyId = p.Id, PropertyCode = p.Code, PropertyName = p.Name, UnitId = unit.Id, UnitNumber = unit.UnitNumber,
                        RentBilled = rent, Commission = commission,
                        CommissionVat = VatDeterminationService.Round(commission * vatRate / 100m), Expenses = costs
                    });
                }

                var building = unitCosts.Where(x => x.PropertyId == p.Id && x.UnitId == null && Counts(x.Category)).Sum(x => x.Amount);
                if (building != 0m)
                    report.Units.Add(new UnitRow { PropertyId = p.Id, PropertyCode = p.Code, PropertyName = p.Name, Expenses = building });
            }

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
                    CostsByCategory = costLines.Where(x => x.PropertyId == p.Id)
                        .GroupBy(x => x.Category).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount)),
                    RechargeVat = costLines.Where(x => x.PropertyId == p.Id && x.Category == OwnerCostCategories.Vat).Sum(x => x.Amount),
                    // A registered owner reclaims the VAT on costs charged to them; for anyone else it is a cost.
                    Expenses = costLines.Where(x => x.PropertyId == p.Id &&
                                                   (x.Category != OwnerCostCategories.Vat || string.IsNullOrWhiteSpace(p.OwnerTrn)))
                                        .Sum(x => x.Amount)
                });
            }

            return report;
        }
    }
}
