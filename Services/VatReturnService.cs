using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Builds the VAT return for a tax period and the annual apportionment wash-up.
    ///
    /// Output tax comes from posted customer invoices, grouped by the VAT category
    /// snapshotted on each one. Input tax comes from posted vendor bills, using the
    /// recoverable / irrecoverable split that was fixed when each bill was posted -
    /// this report never re-derives it, so it always agrees with the ledger.
    /// </summary>
    public class VatReturnService
    {
        private readonly ApplicationDbContext _db;

        public VatReturnService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<VatReturnReportDto> GetReturnAsync(int companyId, DateTime startDate, DateTime endDate)
        {
            var company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId);

            var report = new VatReturnReportDto
            {
                StartDate = startDate,
                EndDate = endDate,
                CompanyName = company.Name,
                CompanyTrn = company.TrnNumber,
                ProvisionalRecoveryRate = company.ProvisionalVatRecoveryRate
            };

            // ----- OUTPUT TAX -------------------------------------------------
            // Only posted invoices. A draft invoice has no ledger effect and is not
            // a supply for return purposes.
            var invoices = await _db.CustomerInvoices
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId
                            && x.JournalEntryId != null
                            && x.Status != "Cancelled"
                            && x.InvoiceDate >= startDate
                            && x.InvoiceDate <= endDate)
                .Select(x => new { x.VatCategory, x.Subtotal, x.VatAmount })
                .ToListAsync();

            foreach (var group in invoices.GroupBy(x => x.VatCategory))
            {
                var net = group.Sum(x => x.Subtotal);
                var vat = group.Sum(x => x.VatAmount);

                report.OutputLines.Add(new VatReturnOutputLineDto
                {
                    VatCategory = group.Key,
                    SupplyCount = group.Count(),
                    NetAmount = net,
                    VatAmount = vat
                });

                switch (group.Key)
                {
                    case VatCategories.Standard:
                        report.StandardRatedNet += net;
                        report.StandardRatedVat += vat;
                        break;
                    case VatCategories.ZeroRated:
                        report.ZeroRatedNet += net;
                        break;
                    case VatCategories.Exempt:
                        report.ExemptNet += net;
                        break;
                }
            }

            report.OutputLines = report.OutputLines.OrderBy(x => x.VatCategory).ToList();

            // ----- INPUT TAX --------------------------------------------------
            var bills = await _db.VendorBills
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId
                            && x.JournalEntryId != null
                            && x.Status != "Cancelled"
                            && x.BillDate >= startDate
                            && x.BillDate <= endDate)
                .Select(x => new
                {
                    x.InputTaxAttribution,
                    x.Subtotal,
                    x.VatAmount,
                    x.RecoverableVatAmount,
                    x.NonRecoverableVatAmount
                })
                .ToListAsync();

            foreach (var group in bills.GroupBy(x => x.InputTaxAttribution))
            {
                report.InputLines.Add(new VatReturnInputLineDto
                {
                    Attribution = group.Key,
                    BillCount = group.Count(),
                    NetAmount = group.Sum(x => x.Subtotal),
                    VatAmount = group.Sum(x => x.VatAmount),
                    RecoverableVat = group.Sum(x => x.RecoverableVatAmount),
                    NonRecoverableVat = group.Sum(x => x.NonRecoverableVatAmount)
                });
            }

            report.InputLines = report.InputLines.OrderBy(x => x.Attribution).ToList();

            report.InputVatTotal = bills.Sum(x => x.VatAmount);
            report.InputVatRecoverable = bills.Sum(x => x.RecoverableVatAmount);
            report.InputVatNonRecoverable = bills.Sum(x => x.NonRecoverableVatAmount);

            report.ResidualInputVat = bills
                .Where(x => x.InputTaxAttribution == InputTaxAttributions.Residual)
                .Sum(x => x.VatAmount);

            return report;
        }

        /// <summary>
        /// The annual wash-up. Compares the recovery rate actually earned over the
        /// year against the provisional rate applied to residual bills during it, and
        /// returns the correction. A positive figure is additional VAT recoverable; a
        /// negative figure must be repaid.
        /// </summary>
        public async Task<VatWashUpDto> GetAnnualWashUpAsync(int companyId, DateTime yearStart, DateTime yearEnd)
        {
            var yearReturn = await GetReturnAsync(companyId, yearStart, yearEnd);

            var washUp = new VatWashUpDto
            {
                YearStart = yearStart,
                YearEnd = yearEnd,
                StandardRatedNet = yearReturn.StandardRatedNet,
                ZeroRatedNet = yearReturn.ZeroRatedNet,
                ExemptNet = yearReturn.ExemptNet,
                ResidualInputVat = yearReturn.ResidualInputVat,
                ProvisionalRecoveryRate = yearReturn.ProvisionalRecoveryRate,
                ActualRecoveryRate = yearReturn.ActualRecoveryRate
            };

            // Recovery is allowed against supplies that carry the right of recovery -
            // standard and zero rated. Exempt supplies do not.
            var recoverableAtActual = VatDeterminationService.Round(
                washUp.ResidualInputVat * washUp.ActualRecoveryRate / 100m);

            var recoveredProvisionally = VatDeterminationService.Round(
                washUp.ResidualInputVat * washUp.ProvisionalRecoveryRate / 100m);

            washUp.RecoverableAtActualRate = recoverableAtActual;
            washUp.AlreadyRecovered = recoveredProvisionally;
            washUp.Adjustment = recoverableAtActual - recoveredProvisionally;

            return washUp;
        }
    }

    // =========================================================================
    //  DTOs
    // =========================================================================

    public class VatReturnOutputLineDto
    {
        public string VatCategory { get; set; } = string.Empty;
        public int SupplyCount { get; set; }
        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }
    }

    public class VatReturnInputLineDto
    {
        public string Attribution { get; set; } = string.Empty;
        public int BillCount { get; set; }
        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal RecoverableVat { get; set; }
        public decimal NonRecoverableVat { get; set; }
    }

    public class VatReturnReportDto
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string? CompanyTrn { get; set; }

        // Output
        public decimal StandardRatedNet { get; set; }
        public decimal StandardRatedVat { get; set; }
        public decimal ZeroRatedNet { get; set; }
        public decimal ExemptNet { get; set; }
        public List<VatReturnOutputLineDto> OutputLines { get; set; } = new();

        // Input
        public decimal InputVatTotal { get; set; }
        public decimal InputVatRecoverable { get; set; }
        public decimal InputVatNonRecoverable { get; set; }
        public decimal ResidualInputVat { get; set; }
        public List<VatReturnInputLineDto> InputLines { get; set; } = new();

        public decimal ProvisionalRecoveryRate { get; set; }

        public decimal TotalSuppliesNet => StandardRatedNet + ZeroRatedNet + ExemptNet;

        /// <summary>
        /// Supplies carrying the right of recovery, over total supplies. This is the
        /// standard apportionment method.
        /// </summary>
        public decimal ActualRecoveryRate => TotalSuppliesNet == 0m
            ? 0m
            : Math.Round((StandardRatedNet + ZeroRatedNet) / TotalSuppliesNet * 100m, 2);

        /// <summary>Output tax less recoverable input tax. Positive means payable to the FTA.</summary>
        public decimal NetVatPayable => StandardRatedVat - InputVatRecoverable;

        public bool HasExemptSupplies => ExemptNet > 0m;
    }

    public class VatWashUpDto
    {
        public DateTime YearStart { get; set; }
        public DateTime YearEnd { get; set; }

        public decimal StandardRatedNet { get; set; }
        public decimal ZeroRatedNet { get; set; }
        public decimal ExemptNet { get; set; }

        public decimal ResidualInputVat { get; set; }
        public decimal ProvisionalRecoveryRate { get; set; }
        public decimal ActualRecoveryRate { get; set; }

        public decimal RecoverableAtActualRate { get; set; }
        public decimal AlreadyRecovered { get; set; }

        /// <summary>Positive = recover more; negative = repay.</summary>
        public decimal Adjustment { get; set; }

        public decimal TotalSuppliesNet => StandardRatedNet + ZeroRatedNet + ExemptNet;
    }
}
