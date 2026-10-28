namespace RealEstateManager.Domain
{
    /// <summary>
    /// One line of a vendor bill, in the shape Odoo uses on account.move.line:
    /// a description, the account it hits, an analytic dimension (here the property),
    /// a quantity and a unit price, and its own tax treatment.
    ///
    /// Tax lives on the line rather than the header because a single supplier
    /// document routinely mixes treatments - a maintenance contract covering a
    /// commercial tower and a residential block on the same invoice recovers input
    /// tax on one line and not the other. A header-level VAT figure cannot express
    /// that, which is why the old single-amount bill could not be made correct.
    /// </summary>
    public class VendorBillLine
    {
        public int Id { get; set; }

        public int VendorBillId { get; set; }
        public VendorBill? VendorBill { get; set; }

        /// <summary>Display order, 1-based. Renumbered on every save.</summary>
        public int LineNumber { get; set; }

        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The expense or asset account this line is charged to. Nullable only so that
        /// bills migrated from the old header-only format can be loaded when the
        /// company had no default expense account mapped; posting refuses a null.
        /// </summary>
        public int? AccountId { get; set; }
        public Account? Account { get; set; }

        /// <summary>Analytic dimension. Lets one bill be split across properties.</summary>
        public int? PropertyId { get; set; }
        public Property? Property { get; set; }

        public int? UnitId { get; set; }
        public Unit? Unit { get; set; }

        public decimal Quantity { get; set; } = 1m;
        public decimal UnitPrice { get; set; } = 0m;

        /// <summary>Quantity x unit price. Stored so a report never has to recompute it.</summary>
        public decimal LineSubtotal { get; set; } = 0m;

        // ---------------------------------------------------------------------
        //  TAX
        // ---------------------------------------------------------------------

        /// <summary>
        /// Whether 5% is charged on this line. Forced to false for every line when the
        /// supplier is not VAT registered - an unregistered supplier cannot legally
        /// charge tax, so the document is a commercial invoice and the whole amount is
        /// a direct cost.
        /// </summary>
        public bool IsTaxable { get; set; } = true;

        public decimal VatRate { get; set; } = 0m;
        public decimal VatAmount { get; set; } = 0m;

        /// <summary>
        /// How this particular cost relates to the supplies the business makes.
        /// Per line, because attribution follows the property being served.
        /// </summary>
        public string InputTaxAttribution { get; set; } = InputTaxAttributions.Taxable;

        /// <summary>Portion of VatAmount debited to VAT Recoverable.</summary>
        public decimal RecoverableVatAmount { get; set; } = 0m;

        /// <summary>Portion of VatAmount capitalised into the expense account.</summary>
        public decimal NonRecoverableVatAmount { get; set; } = 0m;

        /// <summary>Recovery rate applied to a Residual line, kept for the annual wash-up.</summary>
        public decimal AppliedRecoveryRate { get; set; } = 100m;

        /// <summary>Net plus tax. What the supplier is owed for this line.</summary>
        public decimal LineTotal { get; set; } = 0m;

        /// <summary>What actually lands on the expense account: net plus irrecoverable tax.</summary>
        public decimal ChargedToExpense => LineSubtotal + NonRecoverableVatAmount;

        /// <summary>
        /// Charged on to the owner of the (managed) property: posted to Due to Landlords
        /// with 5% output VAT on a recharge tax invoice, instead of to the account chosen.
        /// </summary>
        public bool RechargeToOwner { get; set; }

        /// <summary>A development cost: the project it is capitalised into, and what for.</summary>
        public int? DevelopmentProjectId { get; set; }
        public DevelopmentProject? DevelopmentProject { get; set; }
        public string? DevelopmentCostCategory { get; set; }
    }
}
