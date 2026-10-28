using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// One management view for an owner who owns both the business and properties the
    /// business manages for them: the company's income statement beside the result of
    /// the owner's own properties, and the two combined. The commission the owner's
    /// properties pay the company is income on one side and a cost on the other, so it
    /// is shown in its own column and drops out of the combined total. The two remain
    /// separate taxable persons: this is a management view, not a consolidation for tax.
    /// </summary>
    public class OwnerCombinedService
    {
        private readonly FinancialReportService _financials;
        private readonly OwnerProfitabilityService _owners;

        public OwnerCombinedService(FinancialReportService financials, OwnerProfitabilityService owners)
        {
            _financials = financials;
            _owners = owners;
        }

        public sealed class Line
        {
            public string Label { get; init; } = string.Empty;
            public decimal Business { get; init; }
            public decimal Personal { get; init; }
            public decimal Between { get; init; }
            public decimal Combined => Business + Personal + Between;

            /// <summary>Header, total or result line, for the layout.</summary>
            public string Kind { get; init; } = "line";
            public string? Note { get; init; }
        }

        public sealed class Report
        {
            public DateTime From { get; set; }
            public DateTime To { get; set; }
            public string? Owner { get; set; }
            public List<string> Owners { get; set; } = new();
            public List<Line> Lines { get; set; } = new();
            public List<OwnerProfitabilityService.Row> Properties { get; set; } = new();
            public bool OwnerIsVatRegistered { get; set; }
            public decimal RentVatForOwner { get; set; }
            public decimal BusinessNet { get; set; }
            public decimal PersonalNet { get; set; }
        }

        public async Task<Report> GetAsync(int companyId, DateTime from, DateTime to, string? owner)
        {
            var personal = await _owners.GetAsync(companyId, from, to, owner);
            var report = new Report { From = from, To = to, Owners = personal.Owners };
            if (string.IsNullOrWhiteSpace(owner) && personal.Owners.Count == 1)
            {
                owner = personal.Owners[0];
                personal = await _owners.GetAsync(companyId, from, to, owner);
            }

            report.Owner = string.IsNullOrWhiteSpace(owner) ? null : owner;
            var pl = await _financials.GetProfitAndLossAsync(companyId, from, to);
            var properties = report.Owner == null ? new List<OwnerProfitabilityService.Row>() : personal.Rows;
            report.Properties = properties;
            report.OwnerIsVatRegistered = properties.Any(p => !string.IsNullOrWhiteSpace(p.OwnerTrn));
            report.RentVatForOwner = properties.Sum(p => p.RentVatForOwner);

            var rent = properties.Sum(p => p.RentBilled);
            var commission = properties.Sum(p => p.Commission);
            // A registered owner reclaims the VAT on our commission; for anyone else it is a cost.
            var commissionVatCost = report.OwnerIsVatRegistered ? 0m : properties.Sum(p => p.CommissionVat);
            var costs = properties.Sum(p => p.Expenses);

            var lines = report.Lines;
            lines.Add(new Line { Label = "Revenue", Kind = "head" });
            lines.Add(new Line { Label = "Rental income", Business = pl.TotalOf(StatementLines.RentalIncome), Personal = rent,
                Note = "Business: owned properties. Personal: the owner's properties managed by the business." });
            lines.Add(new Line { Label = "Management commission", Business = pl.TotalOf(StatementLines.Commission), Between = -commission,
                Note = "Commission on the owner's own properties is paid from one to the other." });
            lines.Add(new Line { Label = "Property sales", Business = pl.TotalOf(StatementLines.PropertySales) });
            lines.Add(new Line { Label = "Other income", Business = pl.TotalOf(StatementLines.OtherIncome) });
            lines.Add(new Line { Label = "Total revenue", Kind = "total", Business = pl.TotalRevenue, Personal = rent, Between = -commission });

            lines.Add(new Line { Label = "Costs", Kind = "head" });
            lines.Add(new Line { Label = "Cost of revenue", Business = -pl.CostOfRevenue });
            lines.Add(new Line { Label = "Commission paid to the business", Personal = -commission, Between = commission });
            if (commissionVatCost != 0m)
                lines.Add(new Line { Label = "VAT on the commission (owner not VAT registered)", Personal = -commissionVatCost });
            lines.Add(new Line { Label = "Maintenance and costs of the properties", Personal = -costs });
            lines.Add(new Line { Label = "Fixed costs", Business = -pl.TotalOf(StatementLines.FixedCosts) });
            lines.Add(new Line { Label = "Recurring costs", Business = -pl.TotalOf(StatementLines.RecurringCosts) });
            lines.Add(new Line { Label = "Finance costs", Business = -pl.FinanceCosts });

            report.BusinessNet = pl.NetProfitOrLoss;
            report.PersonalNet = rent - commission - commissionVatCost - costs;
            lines.Add(new Line { Label = "Net profit / (loss)", Kind = "result", Business = report.BusinessNet, Personal = report.PersonalNet });
            return report;
        }
    }
}
