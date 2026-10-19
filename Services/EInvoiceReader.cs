using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Reads a supplier's PINT AE e-invoice (UBL 2.1 Invoice or CreditNote) received
    /// through the company's provider, so it can be booked as a vendor bill without
    /// retyping it.
    ///
    /// The file comes from outside the company, so it is parsed defensively: no DTDs, no
    /// external entities, a size limit, and every value treated as data.
    /// </summary>
    public static class EInvoiceReader
    {
        public const int MaxFileBytes = 5 * 1024 * 1024;

        private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
        private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
        private static readonly XNamespace NsInvoice = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
        private static readonly XNamespace NsCreditNote = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";

        public static ParsedEInvoice Parse(string xml)
        {
            XDocument document;
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaxFileBytes,
                    IgnoreComments = true
                };

                using var stringReader = new StringReader(xml);
                using var reader = XmlReader.Create(stringReader, settings);
                document = XDocument.Load(reader);
            }
            catch (XmlException ex)
            {
                throw new InvalidOperationException("The file is not a readable XML document: " + ex.Message);
            }

            var root = document.Root ?? throw new InvalidOperationException("The file is empty.");

            var isInvoice = root.Name == NsInvoice + "Invoice";
            var isCreditNote = root.Name == NsCreditNote + "CreditNote";

            if (!isInvoice && !isCreditNote)
                throw new InvalidOperationException("The file is not a UBL invoice or credit note, so it is not an e-invoice.");

            var parsed = new ParsedEInvoice
            {
                CustomizationId = Text(root, Cbc + "CustomizationID"),
                DocumentTypeCode = isInvoice
                    ? Text(root, Cbc + "InvoiceTypeCode") ?? "380"
                    : Text(root, Cbc + "CreditNoteTypeCode") ?? "381",
                DocumentNumber = Text(root, Cbc + "ID") ?? string.Empty,
                Uuid = Text(root, Cbc + "UUID") ?? string.Empty,
                IssueDate = Date(Text(root, Cbc + "IssueDate")) ?? DateTime.Today,
                DueDate = Date(Text(root, Cbc + "DueDate")),
                Currency = Text(root, Cbc + "DocumentCurrencyCode") ?? "AED",
                Note = Text(root, Cbc + "Note")
            };

            var supplier = root.Element(Cac + "AccountingSupplierParty")?.Element(Cac + "Party");
            if (supplier != null)
            {
                parsed.SupplierName =
                    supplier.Element(Cac + "PartyLegalEntity")?.Element(Cbc + "RegistrationName")?.Value
                    ?? supplier.Element(Cac + "PartyName")?.Element(Cbc + "Name")?.Value
                    ?? string.Empty;

                parsed.SupplierTin = supplier.Element(Cbc + "EndpointID")?.Value?.Trim();

                foreach (var scheme in supplier.Elements(Cac + "PartyTaxScheme"))
                {
                    var id = scheme.Element(Cbc + "CompanyID")?.Value?.Trim();
                    var type = scheme.Element(Cac + "TaxScheme")?.Element(Cbc + "ID")?.Value?.Trim();
                    if (string.Equals(type, "VAT", StringComparison.OrdinalIgnoreCase))
                        parsed.SupplierTrn = id;
                    else if (!string.IsNullOrWhiteSpace(id))
                        parsed.SupplierTin ??= id;
                }
            }

            var totals = root.Element(Cac + "LegalMonetaryTotal");
            parsed.NetAmount = Amount(totals?.Element(Cbc + "TaxExclusiveAmount")?.Value);
            parsed.TotalAmount = Amount(totals?.Element(Cbc + "TaxInclusiveAmount")?.Value);
            parsed.VatAmount = root.Elements(Cac + "TaxTotal")
                .Select(x => Amount(x.Element(Cbc + "TaxAmount")?.Value))
                .FirstOrDefault();

            var lineName = isInvoice ? "InvoiceLine" : "CreditNoteLine";
            var quantityName = isInvoice ? "InvoicedQuantity" : "CreditedQuantity";

            foreach (var line in root.Elements(Cac + lineName))
            {
                var item = line.Element(Cac + "Item");
                var category = item?.Element(Cac + "ClassifiedTaxCategory");

                var name = item?.Element(Cbc + "Name")?.Value?.Trim();
                var description = item?.Element(Cbc + "Description")?.Value?.Trim();

                var quantity = Amount(line.Element(Cbc + quantityName)?.Value);
                var net = Amount(line.Element(Cbc + "LineExtensionAmount")?.Value);

                parsed.Lines.Add(new ParsedEInvoiceLine
                {
                    Description = string.IsNullOrWhiteSpace(description) || description == name
                        ? name ?? "(no description)"
                        : $"{name} - {description}",
                    Quantity = quantity == 0m ? 1m : quantity,
                    NetAmount = net,
                    VatCategory = category?.Element(Cbc + "ID")?.Value?.Trim() ?? "S",
                    VatRate = Amount(category?.Element(Cbc + "Percent")?.Value)
                });
            }

            if (string.IsNullOrWhiteSpace(parsed.Uuid))
                throw new InvalidOperationException("The document has no unique identifier (UUID), which every UAE e-invoice must carry.");

            if (string.IsNullOrWhiteSpace(parsed.DocumentNumber))
                throw new InvalidOperationException("The document has no number.");

            return parsed;
        }

        private static string? Text(XElement parent, XName name)
        {
            var value = parent.Element(name)?.Value?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static DateTime? Date(string? value) =>
            DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;

        private static decimal Amount(string? value) =>
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : 0m;
    }

    public class ParsedEInvoice
    {
        public string? CustomizationId { get; set; }
        public string DocumentTypeCode { get; set; } = string.Empty;
        public string DocumentNumber { get; set; } = string.Empty;
        public string Uuid { get; set; } = string.Empty;
        public DateTime IssueDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string Currency { get; set; } = "AED";
        public string? Note { get; set; }

        public string SupplierName { get; set; } = string.Empty;
        public string? SupplierTrn { get; set; }
        public string? SupplierTin { get; set; }

        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }

        public List<ParsedEInvoiceLine> Lines { get; set; } = new();

        public bool IsCreditNote => DocumentTypeCode is "381" or "81";

        public bool IsUaeFormat =>
            CustomizationId?.StartsWith("urn:peppol:pint:", StringComparison.OrdinalIgnoreCase) == true
            && CustomizationId.Contains("@ae-", StringComparison.OrdinalIgnoreCase);
    }

    public class ParsedEInvoiceLine
    {
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; } = 1m;
        public decimal NetAmount { get; set; }
        public string VatCategory { get; set; } = "S";
        public decimal VatRate { get; set; }

        public bool IsStandardRated => VatCategory == "S" && VatRate > 0m;
    }
}
