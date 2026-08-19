namespace RealEstateManager.Domain
{
    /// <summary>
    /// VAT treatment of a supply. Deliberately a small closed set - these three
    /// behave differently and must never be collapsed into a single "VAT yes/no"
    /// flag, because Exempt and Zero-rated look identical on the invoice (no tax
    /// charged) but are opposite for input tax recovery.
    /// </summary>
    public static class VatCategories
    {
        /// <summary>Commercial property. 5% output tax. Input tax fully recoverable.</summary>
        public const string Standard = "Standard";

        /// <summary>
        /// Residential lease (a subsequent supply). No output tax AND input tax on
        /// related costs is NOT recoverable. This is what forces apportionment.
        /// </summary>
        public const string Exempt = "Exempt";

        /// <summary>
        /// First supply of a new residential building within three years of
        /// completion. No output tax but input tax IS recoverable. Not currently used
        /// by this portfolio; supported so the classification is not lossy.
        /// </summary>
        public const string ZeroRated = "ZeroRated";

        public static readonly string[] All = { Standard, Exempt, ZeroRated };

        /// <summary>Does this category carry output tax?</summary>
        public static bool IsTaxable(string category) => category == Standard;

        /// <summary>
        /// Does a supply in this category allow recovery of related input tax?
        /// Standard and Zero-rated do; Exempt does not.
        /// </summary>
        public static bool AllowsInputRecovery(string category) =>
            category == Standard || category == ZeroRated;

        public static string Describe(string category) => category switch
        {
            Standard => "Standard rated (5%)",
            Exempt => "Exempt",
            ZeroRated => "Zero rated (0%)",
            _ => category
        };

        /// <summary>
        /// Default treatment implied by a property type, used to pre-fill the VAT
        /// category when a property is created. Always overridable.
        /// </summary>
        public static string FromPropertyType(string? propertyType) => propertyType switch
        {
            "Residential" => Exempt,
            "Commercial" => Standard,
            "Warehouse" => Standard,
            _ => Standard
        };
    }

    /// <summary>
    /// How the input tax on a purchase relates to the supplies the business makes.
    /// This is the attribution step that must happen before any recovery decision.
    /// </summary>
    public static class InputTaxAttributions
    {
        /// <summary>Wholly for taxable supplies - input tax recovered in full.</summary>
        public const string Taxable = "Taxable";

        /// <summary>Wholly for exempt supplies - input tax not recoverable, so it becomes part of the cost.</summary>
        public const string Exempt = "Exempt";

        /// <summary>Overheads that cannot be attributed - input tax apportioned.</summary>
        public const string Residual = "Residual";

        public static readonly string[] All = { Taxable, Exempt, Residual };

        public static string Describe(string attribution) => attribution switch
        {
            Taxable => "Taxable supplies - fully recoverable",
            Exempt => "Exempt supplies - not recoverable",
            Residual => "Residual / overhead - apportioned",
            _ => attribution
        };
    }

    /// <summary>Article 59 permits a simplified layout in defined circumstances.</summary>
    public static class TaxInvoiceTypes
    {
        /// <summary>Recipient is VAT registered - full tax invoice showing their TRN.</summary>
        public const string Full = "Full";

        /// <summary>Recipient is not VAT registered - simplified tax invoice.</summary>
        public const string Simplified = "Simplified";

        /// <summary>No output tax was charged, so the document is not a tax invoice.</summary>
        public const string NotApplicable = "NotApplicable";
    }
}
