using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The VAT position of a VAT-registered owner of managed properties for a tax period,
    /// from what passes through the company: output tax on the rent invoiced in the
    /// owner's name (less credit notes), and input tax on the company's commission and
    /// recharge tax invoices to the owner. The owner files it under their own TRN; any
    /// other income or costs of the owner are outside this system.
    /// </summary>
    public class OwnerVatReturnService
    {
        private readonly ApplicationDbContext _db;

        public OwnerVatReturnService(ApplicationDbContext db)
        {
            _db = db;
        }

        public sealed class Document
        {
            public DateTime Date { get; init; }
            public string Number { get; init; } = string.Empty;
            public string Kind { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public string Category { get; init; } = VatCategories.Standard;
            public decimal Net { get; init; }
            public decimal Vat { get; init; }
            public string? Link { get; init; }
        }

        public sealed class Report
        {
            public DateTime From { get; set; }
            public DateTime To { get; set; }
            public string? Owner { get; set; }
            public string? OwnerTrn { get; set; }
            public List<(string Name, string? Trn)> Owners { get; set; } = new();

            public List<Document> Supplies { get; set; } = new();
            public List<Document> Purchases { get; set; } = new();

            /// <summary>Owner's properties whose rent is invoiced by the company itself, so not on this return.</summary>
            public List<string> NotBilledInOwnerName { get; set; } = new();

            public decimal SuppliesNet(string category) => Supplies.Where(x => x.Category == category).Sum(x => x.Net);
            public decimal SuppliesVat(string category) => Supplies.Where(x => x.Category == category).Sum(x => x.Vat);
            public decimal OutputTax => Supplies.Sum(x => x.Vat);
            public decimal InputTax => Purchases.Sum(x => x.Vat);
            public decimal NetPayable => OutputTax - InputTax;
        }

        public async Task<Report> GetAsync(int companyId, DateTime from, DateTime to, string? owner)
        {
            var report = new Report { From = from.Date, To = to.Date };

            // Owners with a TRN on one of their managed properties.
            var properties = await _db.Properties.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.OwnershipType == "Managed" && x.OwnerName != null)
                .Select(x => new { x.Code, x.Name, OwnerName = x.OwnerName!.Trim(), x.OwnerTrn, x.RentBilledInOwnerName })
                .ToListAsync();
            report.Owners = properties.Where(x => !string.IsNullOrWhiteSpace(x.OwnerTrn))
                .GroupBy(x => x.OwnerName, StringComparer.OrdinalIgnoreCase)
                .Select(g => (g.Key, (string?)g.First().OwnerTrn!.Trim()))
                .OrderBy(x => x.Key).ToList();

            if (string.IsNullOrWhiteSpace(owner) && report.Owners.Count == 1)
                owner = report.Owners[0].Name;
            var match = report.Owners.FirstOrDefault(x => string.Equals(x.Name, owner?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match.Name == null)
                return report;

            report.Owner = match.Name;
            report.OwnerTrn = match.Trn;
            report.NotBilledInOwnerName = properties
                .Where(x => string.Equals(x.OwnerName, match.Name, StringComparison.OrdinalIgnoreCase) && !x.RentBilledInOwnerName)
                .Select(x => $"{x.Code} {x.Name}").ToList();

            var name = match.Name;
            var start = report.From;
            var end = report.To;

            // ---- Output tax: rent invoiced in the owner's name, and credit notes against it.
            var invoices = await _db.CustomerInvoices.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.IssuedForOwnerName == name &&
                            x.JournalEntry != null && x.JournalEntry.IsPosted && x.Status != "Cancelled" &&
                            x.InvoiceDate >= start && x.InvoiceDate <= end)
                .OrderBy(x => x.InvoiceDate)
                .Select(x => new { x.Id, x.InvoiceDate, Number = x.TaxInvoiceNumber ?? x.InvoiceNumber, x.VatCategory, x.Subtotal, x.VatAmount,
                                   Tenant = x.Lease!.Tenant!.FullName, Unit = x.Lease.Unit!.UnitNumber })
                .ToListAsync();
            report.Supplies.AddRange(invoices.Select(x => new Document
            {
                Date = x.InvoiceDate, Number = x.Number, Kind = "Rent invoice", Category = x.VatCategory,
                Description = $"{x.Tenant}, unit {x.Unit}", Net = x.Subtotal, Vat = x.VatAmount,
                Link = $"/CustomerInvoices/TaxInvoice/{x.Id}"
            }));

            var credits = await _db.CustomerCreditNotes.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.CustomerInvoice!.IssuedForOwnerName == name &&
                            x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.CreditNoteDate >= start && x.CreditNoteDate <= end)
                .OrderBy(x => x.CreditNoteDate)
                .Select(x => new { x.Id, x.CreditNoteDate, x.CreditNoteNumber, x.VatCategory, x.Subtotal, x.VatAmount, Invoice = x.CustomerInvoice!.InvoiceNumber })
                .ToListAsync();
            report.Supplies.AddRange(credits.Select(x => new Document
            {
                Date = x.CreditNoteDate, Number = x.CreditNoteNumber, Kind = "Credit note", Category = x.VatCategory,
                Description = $"Against {x.Invoice}", Net = -x.Subtotal, Vat = -x.VatAmount,
                Link = $"/CustomerCreditNotes/Document/{x.Id}"
            }));

            // ---- Input tax: the company's tax invoices to the owner.
            var commissions = await _db.OwnerCommissionInvoices.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.OwnerName == name &&
                            x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.InvoiceDate >= start && x.InvoiceDate <= end)
                .OrderBy(x => x.InvoiceDate)
                .Select(x => new { x.Id, x.InvoiceDate, Number = x.TaxInvoiceNumber ?? x.InvoiceNumber, x.Commission, x.VatAmount, x.PeriodStart, x.PeriodEnd })
                .ToListAsync();
            report.Purchases.AddRange(commissions.Select(x => new Document
            {
                Date = x.InvoiceDate, Number = x.Number, Kind = "Management commission",
                Description = $"Commission {x.PeriodStart:dd/MM/yyyy} - {x.PeriodEnd:dd/MM/yyyy}", Net = x.Commission, Vat = x.VatAmount,
                Link = $"/OwnerSettlements/CommissionInvoice/{x.Id}"
            }));

            var recharges = await _db.OwnerRecharges.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.OwnerName == name &&
                            x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.RechargeDate >= start && x.RechargeDate <= end)
                .OrderBy(x => x.RechargeDate)
                .Select(x => new { x.Id, x.RechargeDate, x.TaxInvoiceNumber, x.SourceReference, x.Description, x.NetAmount, x.VatAmount, x.SupplierVatAmount, x.Treatment })
                .ToListAsync();
            // A recharge: our tax invoice. A disbursement: the supplier's invoice in the owner's name.
            report.Purchases.AddRange(recharges.Select(x => new Document
            {
                Date = x.RechargeDate,
                Number = x.Treatment == OwnerCostTreatments.Disbursement ? (x.SourceReference ?? "-") : x.TaxInvoiceNumber ?? "-",
                Kind = x.Treatment == OwnerCostTreatments.Disbursement ? "Supplier's invoice (paid on your behalf)" : "Recharged costs",
                Description = x.Description, Net = x.NetAmount, Vat = x.VatAmount + x.SupplierVatAmount,
                Link = $"/OwnerRecharges/Document/{x.Id}"
            }));

            return report;
        }
    }
}
