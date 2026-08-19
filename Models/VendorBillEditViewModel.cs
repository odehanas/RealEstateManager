using RealEstateManager.Domain;

namespace RealEstateManager.Models
{
    /// <summary>
    /// Edit model for a multi-line vendor bill. Mirrors the shape of an Odoo vendor
    /// bill: a header identifying the supplier and the dates, and a grid of lines each
    /// with its own account, analytic dimension, quantity, price and tax treatment.
    /// </summary>
    public class VendorBillEditViewModel
    {
        public int Id { get; set; }

        /// <summary>Our own reference. Issued by the system; shown but never typed.</summary>
        public string? BillNumber { get; set; }

        public int VendorId { get; set; }

        /// <summary>The supplier's own document number.</summary>
        public string? VendorReference { get; set; }

        public DateTime BillDate { get; set; } = DateTime.Today;

        /// <summary>
        /// Period the journal is booked into. Defaults to the bill date; kept separate
        /// so a late-arriving document can be recorded in an open period.
        /// </summary>
        public DateTime AccountingDate { get; set; } = DateTime.Today;

        public DateTime DueDate { get; set; } = DateTime.Today;

        /// <summary>Header-level property. Used as the default for new lines only.</summary>
        public int? PropertyId { get; set; }

        public string ExpenseCategory { get; set; } = string.Empty;
        public string? Notes { get; set; }

        public List<VendorBillLineViewModel> Lines { get; set; } = new();

        // ---- Read-only context for the form ---------------------------------

        public string Status { get; set; } = "Draft";
        public bool IsEditable { get; set; } = true;
        public int? JournalEntryId { get; set; }
        public decimal AmountPaid { get; set; }

        /// <summary>Whether the chosen supplier may charge VAT at all.</summary>
        public bool VendorCanChargeVat { get; set; }

        public string? VendorTrn { get; set; }

        public string DocumentType => VendorCanChargeVat ? "Tax Invoice" : "Commercial Invoice";
    }

    public class VendorBillLineViewModel
    {
        public int Id { get; set; }

        public string Description { get; set; } = string.Empty;

        public int? AccountId { get; set; }
        public int? PropertyId { get; set; }

        public decimal Quantity { get; set; } = 1m;
        public decimal UnitPrice { get; set; } = 0m;

        public bool IsTaxable { get; set; } = true;

        public string InputTaxAttribution { get; set; } = InputTaxAttributions.Taxable;

        /// <summary>
        /// Set by the browser when the user removes a row. The row stays in the form so
        /// the posted indexes do not shift; the server drops it on save.
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>True when the row carries nothing worth saving.</summary>
        public bool IsBlank =>
            string.IsNullOrWhiteSpace(Description) && AccountId == null && UnitPrice == 0m;
    }
}
