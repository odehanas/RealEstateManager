namespace RealEstateManager.Domain
{
    public class Vendor
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? ServiceType { get; set; }
        public string? ContactPerson { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }

        // Supplier's TRN, required as evidence supporting an input tax claim.
        public string? TrnNumber { get; set; }

        // Commercial licence number. Unique per company when supplied, so the same
        // supplier cannot be created twice under two vendor codes.
        public string? TradeLicenseNumber { get; set; }

        /// <summary>
        /// The supplier's Tax Identification Number - how a received e-invoice is matched
        /// to this vendor when the supplier is not VAT registered. Blank means "take it
        /// from the TRN".
        /// </summary>
        public string? TaxIdentificationNumber { get; set; }

        public string? EffectiveTin =>
            !string.IsNullOrWhiteSpace(TaxIdentificationNumber)
                ? TaxIdentificationNumber.Trim()
                : Company.TinFromTrn(TrnNumber);

        // Whether the supplier holds a LIVE VAT registration. Separate from having a
        // TRN on file, because a registration can be cancelled while the number stays
        // on old paperwork - and billing tax against a deregistered supplier is a
        // disallowed input tax claim. Only a vendor that is registered AND has a TRN
        // may charge VAT on a bill.
        public bool IsVatRegistered { get; set; } = false;

        /// <summary>
        /// True when this supplier may legally charge VAT. Anything else means the
        /// document is a commercial invoice and the whole amount is a direct cost.
        /// </summary>
        public bool CanChargeVat => IsVatRegistered && !string.IsNullOrWhiteSpace(TrnNumber);

        // Opening balance brought forward from a legacy system / prior period.
        // Positive = payable to this vendor (credit nature in AP control account).
        // Negative = advance paid to this vendor (debit balance).
        public decimal OpeningBalance { get; set; } = 0m;

        public bool IsActive { get; set; } = true;
    }
}