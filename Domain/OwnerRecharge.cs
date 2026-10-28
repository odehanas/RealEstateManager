namespace RealEstateManager.Domain
{
    /// <summary>
    /// A cost the company incurred for an owner's managed property and charges on to the
    /// owner: a supplier bill line, a petty cash voucher or an item from the maintenance
    /// store. The recharge is the company's own standard-rated supply to the owner, so it
    /// carries 5% VAT and a tax invoice in the company's series; the company reclaims the
    /// input VAT it paid on the cost. Charged to the owner through Due to Landlords.
    /// </summary>
    public class OwnerRecharge
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>VendorBill (SourceId = bill id), PettyCashExpense (journal id) or MaintenanceMaterial (material id).</summary>
        public string SourceType { get; set; } = string.Empty;
        public int SourceId { get; set; }
        public string? SourceReference { get; set; }

        public int PropertyId { get; set; }
        public Property? Property { get; set; }
        public int? UnitId { get; set; }

        /// <summary>The owner as they were when the recharge was made.</summary>
        public string OwnerName { get; set; } = string.Empty;
        public string? OwnerTrn { get; set; }

        public DateTime RechargeDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal NetAmount { get; set; }
        public decimal VatRate { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }

        /// <summary>Recharge (5% VAT, our tax invoice) or Disbursement (no VAT; the supplier's VAT is the owner's).</summary>
        public string Treatment { get; set; } = OwnerCostTreatments.Recharge;

        /// <summary>On a disbursement: the supplier's VAT, which the owner reclaims with the supplier's invoice.</summary>
        public decimal SupplierVatAmount { get; set; }

        public string? CostCategory { get; set; }

        /// <summary>The VAT a registered owner reclaims on this cost: ours on a recharge, the supplier's on a disbursement.</summary>
        public decimal OwnerReclaimableVat => VatAmount + SupplierVatAmount;

        /// <summary>Sequential tax invoice number in the company's series (recharges only).</summary>
        public string? TaxInvoiceNumber { get; set; }

        /// <summary>The entry that charged it to the owner; the recharge counts once that entry is posted.</summary>
        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public static class OwnerRechargeSources
    {
        public const string VendorBill = "VendorBill";
        public const string PettyCash = "PettyCashExpense";
        public const string MaintenanceMaterial = "MaintenanceMaterial";

        public static string Describe(string source) => source switch
        {
            VendorBill => "Supplier bill",
            PettyCash => "Petty cash",
            MaintenanceMaterial => "Maintenance store",
            _ => source
        };
    }
}
