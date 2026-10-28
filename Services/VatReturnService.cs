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
                            && x.IssuedForOwnerName == null   // billed in an owner's name: the owner's return
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

            // Our management commission to the landlords of managed properties: a standard
            // rated service of our own, whatever the VAT treatment of the rent it is earned on.
            var commissionInvoices = await _db.OwnerCommissionInvoices
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId
                            && x.JournalEntry != null && x.JournalEntry.IsPosted
                            && x.InvoiceDate >= startDate
                            && x.InvoiceDate <= endDate)
                .Select(x => new { x.Commission, x.VatAmount })
                .ToListAsync();

            if (commissionInvoices.Count > 0)
            {
                var net = commissionInvoices.Sum(x => x.Commission);
                var vat = commissionInvoices.Sum(x => x.VatAmount);

                report.OutputLines.Add(new VatReturnOutputLineDto
                {
                    VatCategory = VatCategories.Standard,
                    Label = "Standard rated - management commission to landlords",
                    SupplyCount = commissionInvoices.Count,
                    NetAmount = net,
                    VatAmount = vat
                });

                report.StandardRatedNet += net;
                report.StandardRatedVat += vat;
            }

            // Costs recharged to the owners of managed properties: our own standard-rated
            // supply, at 5% on the cost.
            var recharges = await _db.OwnerRecharges
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId
                            && x.Treatment == OwnerCostTreatments.Recharge   // a disbursement is not our supply
                            && x.JournalEntry != null && x.JournalEntry.IsPosted
                            && x.RechargeDate >= startDate
                            && x.RechargeDate <= endDate)
                .Select(x => new { x.NetAmount, x.VatAmount })
                .ToListAsync();

            if (recharges.Count > 0)
            {
                var net = recharges.Sum(x => x.NetAmount);
                var vat = recharges.Sum(x => x.VatAmount);

                report.OutputLines.Add(new VatReturnOutputLineDto
                {
                    VatCategory = VatCategories.Standard,
                    Label = "Standard rated - costs recharged to owners",
                    SupplyCount = recharges.Count,
                    NetAmount = net,
                    VatAmount = vat
                });

                report.StandardRatedNet += net;
                report.StandardRatedVat += vat;
            }

            // Unit sales. A payment received before handover is a supply when received (the tax
            // point); whatever is still unpaid at handover is a supply at handover.
            var saleReceipts = await _db.UnitSaleReceipts
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.IsBeforeHandover
                            && x.JournalEntry != null && x.JournalEntry.IsPosted
                            && x.ReceiptDate >= startDate && x.ReceiptDate <= endDate)
                .Select(x => new { x.UnitSale!.VatCategory, Net = x.NetAmount, Vat = x.VatAmount })
                .ToListAsync();
            var handovers = await _db.UnitSales
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.HandoverJournalEntry != null && x.HandoverJournalEntry.IsPosted
                            && x.HandoverDate >= startDate && x.HandoverDate <= endDate
                            && (x.HandoverNetAmount != 0m || x.HandoverVatAmount != 0m))
                .Select(x => new { x.VatCategory, Net = x.HandoverNetAmount, Vat = x.HandoverVatAmount })
                .ToListAsync();

            foreach (var group in saleReceipts.Concat(handovers).GroupBy(x => x.VatCategory))
            {
                var net = group.Sum(x => x.Net);
                var vat = group.Sum(x => x.Vat);

                report.OutputLines.Add(new VatReturnOutputLineDto
                {
                    VatCategory = group.Key,
                    Label = VatCategories.Describe(group.Key) + " - sale of units",
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

            // Credit notes reduce the supplies and the output tax of the period in which they
            // are issued (Article 61), not the period of the invoice they correct.
            var creditNotes = await _db.CustomerCreditNotes
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId
                            && x.JournalEntry != null && x.JournalEntry.IsPosted
                            && x.CustomerInvoice!.IssuedForOwnerName == null
                            && x.CreditNoteDate >= startDate
                            && x.CreditNoteDate <= endDate)
                .Select(x => new { x.VatCategory, x.Subtotal, x.VatAmount })
                .ToListAsync();

            foreach (var group in creditNotes.GroupBy(x => x.VatCategory))
            {
                var net = group.Sum(x => x.Subtotal);
                var vat = group.Sum(x => x.VatAmount);

                report.OutputLines.Add(new VatReturnOutputLineDto
                {
                    VatCategory = group.Key,
                    IsCreditNote = true,
                    SupplyCount = group.Count(),
                    NetAmount = -net,
                    VatAmount = -vat
                });

                switch (group.Key)
                {
                    case VatCategories.Standard:
                        report.StandardRatedNet -= net;
                        report.StandardRatedVat -= vat;
                        break;
                    case VatCategories.ZeroRated:
                        report.ZeroRatedNet -= net;
                        break;
                    case VatCategories.Exempt:
                        report.ExemptNet -= net;
                        break;
                }
            }

            report.OutputLines = report.OutputLines
                .OrderBy(x => x.VatCategory)
                .ThenBy(x => x.Label != null)
                .ThenBy(x => x.IsCreditNote)
                .ToList();

            // ----- INPUT TAX --------------------------------------------------
            // Input tax is summed from the BILL LINES, not the header. One supplier
            // document routinely mixes attributions - a contract covering a commercial
            // tower and a residential block recovers on one line and not the other - so
            // grouping by a single header attribution would misstate the claim.
            var billLines = await _db.VendorBillLines
                .AsNoTracking()
                .Where(x => x.VendorBill != null
                            && x.VendorBill.CompanyId == companyId
                            && !x.IsDisbursement   // paid on an owner's behalf: the supplier's VAT is the owner's
                            && x.VendorBill.JournalEntryId != null
                            && x.VendorBill.Status != "Cancelled"
                            && x.VendorBill.BillDate >= startDate
                            && x.VendorBill.BillDate <= endDate)
                .Select(x => new
                {
                    x.VendorBillId,
                    x.InputTaxAttribution,
                    x.LineSubtotal,
                    x.VatAmount,
                    x.RecoverableVatAmount,
                    x.NonRecoverableVatAmount
                })
                .ToListAsync();

            foreach (var group in billLines.GroupBy(x => x.InputTaxAttribution))
            {
                report.InputLines.Add(new VatReturnInputLineDto
                {
                    Attribution = group.Key,

                    // Documents, not lines - a five-line bill is one bill on the return.
                    BillCount = group.Select(x => x.VendorBillId).Distinct().Count(),
                    NetAmount = group.Sum(x => x.LineSubtotal),
                    VatAmount = group.Sum(x => x.VatAmount),
                    RecoverableVat = group.Sum(x => x.RecoverableVatAmount),
                    NonRecoverableVat = group.Sum(x => x.NonRecoverableVatAmount)
                });
            }

            report.InputLines = report.InputLines.OrderBy(x => x.Attribution).ToList();

            report.InputVatTotal = billLines.Sum(x => x.VatAmount);
            report.InputVatRecoverable = billLines.Sum(x => x.RecoverableVatAmount);
            report.InputVatNonRecoverable = billLines.Sum(x => x.NonRecoverableVatAmount);

            // Capital Assets Scheme: the yearly correction of input tax recovered on a building,
            // in the return for the period in which it is made.
            var casAdjustments = await _db.CapitalAssetVatAdjustments
                .AsNoTracking()
                .Where(x => x.CapitalAssetVatRecord != null && x.CapitalAssetVatRecord.CompanyId == companyId
                            && x.JournalEntry != null && x.JournalEntry.IsPosted
                            && x.AdjustmentDate >= startDate && x.AdjustmentDate <= endDate)
                .Select(x => x.Adjustment)
                .ToListAsync();
            if (casAdjustments.Count > 0)
            {
                var adjustment = casAdjustments.Sum();
                report.InputLines.Add(new VatReturnInputLineDto
                {
                    Attribution = "CapitalAssetsScheme",
                    BillCount = casAdjustments.Count,
                    RecoverableVat = adjustment
                });
                report.InputVatRecoverable += adjustment;
                report.CapitalAssetAdjustment = adjustment;
            }

            report.ResidualInputVat = billLines
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

        /// <summary>A row of credit notes, shown as a deduction from the supplies above it.</summary>
        public bool IsCreditNote { get; set; }

        /// <summary>Overrides the category name, for a row that is not tenant rent.</summary>
        public string? Label { get; set; }

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

        /// <summary>Capital Assets Scheme adjustments in the period: positive recovers more, negative repays.</summary>
        public decimal CapitalAssetAdjustment { get; set; }

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
