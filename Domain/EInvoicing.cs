namespace RealEstateManager.Domain
{
    // =========================================================================
    //  UAE ELECTRONIC INVOICING (PINT AE)
    //
    //  Ministerial Decisions 243 and 244 of 2025 move B2B and B2G invoicing onto the
    //  Peppol network. A business does not send an e-invoice itself: it hands a
    //  structured file (PINT AE, a UBL 2.1 XML document) to its Accredited Service
    //  Provider (ASP), which validates it, delivers it to the buyer's provider and
    //  reports it to the FTA. This system's job is to produce that file correctly,
    //  keep the copy that was sent, and track what the provider said about it.
    //
    //  The code values in this file are the official ones from the PINT AE code lists
    //  (https://docs.peppol.eu/poac/ae/pint-ae/), not inventions of this system.
    // =========================================================================

    /// <summary>Where a document is in its life on the e-invoicing network.</summary>
    public static class EInvoiceStatuses
    {
        /// <summary>
        /// Outside the mandate: the buyer is an individual (B2C is out of scope), the
        /// company has not switched e-invoicing on, or the supply predates go-live.
        /// </summary>
        public const string NotRequired = "NotRequired";

        /// <summary>In scope, but no e-invoice file has been produced yet.</summary>
        public const string Pending = "Pending";

        /// <summary>The PINT AE file has been produced and passed the system's checks.</summary>
        public const string Generated = "Generated";

        /// <summary>Handed to the Accredited Service Provider. Now part of the FTA record.</summary>
        public const string Submitted = "Submitted";

        /// <summary>The provider confirmed it was validated, delivered and reported.</summary>
        public const string Accepted = "Accepted";

        /// <summary>The provider refused it. Correct the cause and generate again.</summary>
        public const string Rejected = "Rejected";

        /// <summary>
        /// Once a document has gone to the provider it is part of the tax authority's
        /// record. From then on it can only be corrected by a credit note - never edited,
        /// reset to draft or cancelled.
        /// </summary>
        public static bool IsLocked(string? status) =>
            status == Submitted || status == Accepted;

        /// <summary>Still waiting to be handed to the provider.</summary>
        public static bool IsOutstanding(string? status) =>
            status == Pending || status == Generated || status == Rejected;

        public static string Describe(string? status) => status switch
        {
            NotRequired => "Not required",
            Pending => "To generate",
            Generated => "Ready to send",
            Submitted => "Sent to provider",
            Accepted => "Accepted",
            Rejected => "Rejected",
            _ => "—"
        };

        public static string BadgeClass(string? status) => status switch
        {
            Pending => "bg-warning text-dark",
            Generated => "bg-info text-dark",
            Submitted => "bg-primary",
            Accepted => "bg-success",
            Rejected => "bg-danger",
            _ => "bg-light text-muted border"
        };
    }

    /// <summary>
    /// Who the customer is. Decides whether an invoice is in scope: B2B and B2G are,
    /// B2C is not (for now). Kept as a choice on the tenant rather than guessed from
    /// whether a TRN is on file, because a company that is not VAT registered is still
    /// a business and still in scope.
    /// </summary>
    public static class PartyTypes
    {
        public const string Individual = "Individual";
        public const string Business = "Business";
        public const string Government = "Government";

        public static readonly string[] All = { Individual, Business, Government };

        public static bool IsInScope(string? partyType) =>
            partyType == Business || partyType == Government;

        public static string Describe(string? partyType) => partyType switch
        {
            Individual => "Individual (B2C — outside e-invoicing)",
            Business => "Business (B2B — e-invoice required)",
            Government => "Government entity (B2G — e-invoice required)",
            _ => partyType ?? string.Empty
        };
    }

    /// <summary>
    /// The nature of the registration number identifying a party (BTAE-15 / BTAE-16).
    /// </summary>
    public static class LegalRegistrationTypes
    {
        public const string TradeLicence = "TL";
        public const string EmiratesId = "EID";
        public const string Passport = "PAS";
        public const string CabinetDecision = "CD";

        public static readonly string[] All = { TradeLicence, EmiratesId, Passport, CabinetDecision };

        public static string Describe(string? code) => code switch
        {
            TradeLicence => "Commercial / trade licence",
            EmiratesId => "Emirates ID",
            Passport => "Passport",
            CabinetDecision => "Cabinet decision (government entity)",
            _ => code ?? string.Empty
        };
    }

    /// <summary>
    /// Country subdivision codes. PINT AE accepts only these seven for a UAE address
    /// (rule IBR-128-AE).
    /// </summary>
    public static class Emirates
    {
        public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
        {
            ["AUH"] = "Abu Dhabi",
            ["DXB"] = "Dubai",
            ["SHJ"] = "Sharjah",
            ["AJM"] = "Ajman",
            ["UAQ"] = "Umm Al Quwain",
            ["RAK"] = "Ras Al Khaimah",
            ["FUJ"] = "Fujairah"
        };

        public static bool IsValid(string? code) =>
            !string.IsNullOrWhiteSpace(code) && All.ContainsKey(code);

        public static string Describe(string? code) =>
            code != null && All.TryGetValue(code, out var name) ? name : code ?? string.Empty;
    }

    /// <summary>
    /// Why a credit note was issued (BTAE-03). Article 60 of the VAT Executive
    /// Regulation lists the circumstances; "VD" is the network's code for a volume
    /// discount, the one case where no original invoice need be referenced.
    /// </summary>
    public static class CreditNoteReasons
    {
        public const string SupplyCancelled = "DL8.61.1.A";
        public const string TreatmentChanged = "DL8.61.1.B";
        public const string ConsiderationAltered = "DL8.61.1.C";
        public const string GoodsReturned = "DL8.61.1.D";
        public const string ChargedInError = "DL8.61.1.E";
        public const string VolumeDiscount = "VD";

        /// <summary>The reasons that make sense for a lease. Returns of goods are left out.</summary>
        public static readonly IReadOnlyDictionary<string, string> ForLeases = new Dictionary<string, string>
        {
            [SupplyCancelled] = "The supply was cancelled (e.g. lease terminated early)",
            [ConsiderationAltered] = "The agreed rent was reduced (discount, rent-free period, bad debt)",
            [ChargedInError] = "Tax was charged, or the tax treatment applied, in error",
            [TreatmentChanged] = "The tax treatment changed because the nature of the supply changed"
        };

        public static string Describe(string? code) => code switch
        {
            SupplyCancelled => "Supply cancelled",
            TreatmentChanged => "Change in the nature of the supply",
            ConsiderationAltered => "Consideration altered",
            GoodsReturned => "Goods or services returned",
            ChargedInError => "Tax charged in error",
            VolumeDiscount => "Volume discount",
            _ => code ?? string.Empty
        };
    }

    /// <summary>
    /// The company's revenue band. It decides the dates the mandate applies from:
    /// Ministerial Decision 243 of 2025 as amended by Decision 66 of 2026.
    /// </summary>
    public static class EInvoicingRevenueBands
    {
        public const string Large = "Large";
        public const string Standard = "Standard";
        public const string Government = "Government";

        public static readonly string[] All = { Large, Standard, Government };

        public static string Describe(string? band) => band switch
        {
            Large => "Annual revenue AED 50 million or more",
            Standard => "Annual revenue below AED 50 million",
            Government => "Government entity",
            _ => "Not set"
        };

        /// <summary>Date by which an Accredited Service Provider must be appointed.</summary>
        public static DateTime? AppointmentDeadline(string? band) => band switch
        {
            Large => new DateTime(2026, 10, 30),
            Standard => new DateTime(2027, 3, 31),
            Government => new DateTime(2027, 3, 31),
            _ => null
        };

        /// <summary>Date from which e-invoices are mandatory.</summary>
        public static DateTime? MandatoryFrom(string? band) => band switch
        {
            Large => new DateTime(2027, 1, 1),
            Standard => new DateTime(2027, 7, 1),
            Government => new DateTime(2027, 10, 1),
            _ => null
        };
    }

    /// <summary>
    /// How the customer is asked to pay (IBT-081, UNCL 4461 subset used in the UAE).
    /// The network requires one on every invoice.
    /// </summary>
    public static class EInvoicePaymentMeans
    {
        public const string NotDefined = "1";
        public const string Cash = "10";
        public const string Cheque = "20";
        public const string CreditTransfer = "30";
        public const string DirectDebit = "49";
        public const string CreditCard = "54";
        public const string OnlinePayment = "68";

        public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
        {
            [CreditTransfer] = "Bank transfer (needs the company IBAN)",
            [Cheque] = "Cheque / post-dated cheque",
            [Cash] = "Cash",
            [OnlinePayment] = "Online payment",
            [CreditCard] = "Credit card",
            [DirectDebit] = "Direct debit",
            [NotDefined] = "Not specified"
        };

        public static string Describe(string? code) =>
            code != null && All.TryGetValue(code, out var name) ? name : code ?? string.Empty;

        /// <summary>The code's name in the UNCL 4461 list, as written into the file.</summary>
        public static string OfficialName(string? code) => code switch
        {
            Cash => "In cash",
            Cheque => "Cheque",
            CreditTransfer => "Credit transfer",
            DirectDebit => "Direct debit",
            CreditCard => "Credit card",
            OnlinePayment => "Online payment service",
            _ => "Instrument not defined"
        };
    }

    /// <summary>
    /// A supplier e-invoice received through the company's provider. Kept exactly as
    /// received - the XML is the legal document - and linked to the vendor bill it was
    /// booked as, so the same document can never be entered twice.
    /// </summary>
    public class ReceivedEInvoice
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>The supplier's globally unique identifier (BTAE-07). Duplicates are refused on it.</summary>
        public string Uuid { get; set; } = string.Empty;

        /// <summary>380 tax invoice, 480 out-of-scope invoice, 381 / 81 credit notes.</summary>
        public string DocumentTypeCode { get; set; } = string.Empty;

        public string DocumentNumber { get; set; } = string.Empty;
        public DateTime IssueDate { get; set; }
        public DateTime? DueDate { get; set; }

        public string SupplierName { get; set; } = string.Empty;
        public string? SupplierTrn { get; set; }
        public string? SupplierTin { get; set; }

        public int? VendorId { get; set; }
        public Vendor? Vendor { get; set; }

        public string Currency { get; set; } = "AED";
        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }

        /// <summary>The document as received. Retained for the statutory period.</summary>
        public string Xml { get; set; } = string.Empty;
        public string? FileName { get; set; }

        /// <summary>Received, Booked, Ignored.</summary>
        public string Status { get; set; } = "Received";

        public int? VendorBillId { get; set; }
        public VendorBill? VendorBill { get; set; }

        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
        public int? ReceivedByAppUserId { get; set; }

        public bool IsCreditNote => DocumentTypeCode == "381" || DocumentTypeCode == "81";
    }
}
