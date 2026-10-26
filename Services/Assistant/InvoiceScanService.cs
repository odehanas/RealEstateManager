using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RealEstateManager.Data;
using RealEstateManager.Models;

namespace RealEstateManager.Services.Assistant
{
    /// <summary>
    /// Reads a scanned or photographed supplier invoice (PDF or image) with OpenAI's
    /// vision model, which does the OCR, and turns it into a draft vendor bill form.
    /// Nothing is saved here: the user checks the filled-in form and saves it.
    /// </summary>
    public class InvoiceScanService
    {
        public const long MaxBytes = 10 * 1024 * 1024;

        private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
            [".gif"] = "image/gif"
        };

        private readonly ApplicationDbContext _db;
        private readonly AssistantOptions _options;
        private readonly VendorBillService _bills;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<InvoiceScanService> _logger;

        public InvoiceScanService(
            ApplicationDbContext db,
            IOptions<AssistantOptions> options,
            VendorBillService bills,
            IHttpClientFactory httpClientFactory,
            ILogger<InvoiceScanService> logger)
        {
            _db = db;
            _options = options.Value;
            _bills = bills;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>Scanning uses the assistant's OpenAI key, so it is available when the assistant is.</summary>
        public bool IsAvailable => _options.IsUsable;

        public sealed class ScanException : Exception
        {
            public ScanException(string message) : base(message) { }
        }

        public sealed class ScannedLine
        {
            public string Description { get; set; } = string.Empty;
            public decimal? Quantity { get; set; }
            public decimal? UnitPrice { get; set; }
            public decimal? VatRate { get; set; }
            public decimal? Amount { get; set; }
        }

        public sealed class ScannedInvoice
        {
            public bool IsInvoice { get; set; }
            public string? SupplierName { get; set; }
            public string? SupplierTrn { get; set; }
            public string? InvoiceNumber { get; set; }
            public string? InvoiceDate { get; set; }
            public string? DueDate { get; set; }
            public string? Currency { get; set; }
            public decimal? Subtotal { get; set; }
            public decimal? VatAmount { get; set; }
            public decimal? Total { get; set; }
            public string? Notes { get; set; }
            public List<ScannedLine> Lines { get; set; } = new();
        }

        /// <summary>What was read, matched to this company's vendors and accounts.</summary>
        public sealed class ScanResult
        {
            public required ScannedInvoice Invoice { get; init; }
            public required VendorBillEditViewModel Bill { get; init; }
            public string? VendorCode { get; init; }
            public string? VendorName { get; init; }
            public List<string> Warnings { get; init; } = new();
        }

        // =====================================================================

        public async Task<ScanResult> ScanAsync(IFormFile? file, int companyId)
        {
            if (!IsAvailable)
                throw new ScanException("Invoice scanning uses the AI assistant's OpenAI key, which is not set up on this system.");
            if (file == null || file.Length == 0)
                throw new ScanException("Choose the scanned invoice to read (PDF, JPG or PNG).");
            if (file.Length > MaxBytes)
                throw new ScanException("The file is larger than 10 MB. Scan it at a lower resolution or as a PDF.");

            var extension = Path.GetExtension(file.FileName ?? string.Empty);
            if (!AllowedTypes.TryGetValue(extension, out var mediaType))
                throw new ScanException("Only PDF, JPG, PNG, WEBP or GIF files can be read.");

            byte[] data;
            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                data = stream.ToArray();
            }

            var invoice = await ReadAsync(data, mediaType, Path.GetFileName(file.FileName ?? "invoice" + extension));
            return await MatchAsync(invoice, companyId, Path.GetFileName(file.FileName ?? string.Empty));
        }

        /// <summary>Sends the document to the vision model and returns what it read.</summary>
        private async Task<ScannedInvoice> ReadAsync(byte[] data, string mediaType, string fileName)
        {
            var dataUrl = $"data:{mediaType};base64,{Convert.ToBase64String(data)}";
            JsonNode document = mediaType == "application/pdf"
                ? new JsonObject { ["type"] = "input_file", ["filename"] = fileName, ["file_data"] = dataUrl }
                : new JsonObject { ["type"] = "input_image", ["image_url"] = dataUrl, ["detail"] = "high" };

            var body = new JsonObject
            {
                ["model"] = _options.DocumentModel,
                ["input"] = new JsonArray(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray(
                        new JsonObject { ["type"] = "input_text", ["text"] = Prompt },
                        document)
                }),
                ["text"] = new JsonObject
                {
                    ["format"] = new JsonObject
                    {
                        ["type"] = "json_schema",
                        ["name"] = "supplier_invoice",
                        ["strict"] = true,
                        ["schema"] = InvoiceSchema()
                    }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenAIApiKey);

            HttpResponseMessage response;
            try
            {
                response = await _httpClientFactory.CreateClient("openai-documents").SendAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not reach OpenAI to read an invoice.");
                throw new ScanException("The document reader could not be reached. Try again in a moment.");
            }

            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenAI refused to read an invoice: {Status} {Body}", (int)response.StatusCode, text.Length > 500 ? text[..500] : text);
                throw new ScanException("The document reader refused the file. It may be unreadable, password protected, or the model name needs checking.");
            }

            var output = OutputText(text);
            if (output == null)
                throw new ScanException("The document reader returned nothing. Try a clearer scan.");

            try
            {
                return JsonSerializer.Deserialize<ScannedInvoice>(output, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                }) ?? throw new ScanException("The document could not be read.");
            }
            catch (JsonException)
            {
                throw new ScanException("The document reader's answer could not be understood. Try again.");
            }
        }

        /// <summary>The model's text from a Responses API reply.</summary>
        private static string? OutputText(string responseJson)
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (!doc.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text")
                        return part.GetProperty("text").GetString();
                }
            }

            return null;
        }

        private const string Prompt = """
            Read this supplier invoice (it may be a scan or a photo, in English and/or Arabic) and return its contents.
            - supplier_name and supplier_trn: the company that ISSUED the invoice, not the customer it is addressed to.
              A UAE TRN is 15 digits; give the digits only.
            - invoice_number: the supplier's invoice number. Dates as YYYY-MM-DD; null when not printed.
            - lines: every item or service line as printed: description, quantity, unit_price (before VAT),
              vat_rate as a percentage (5 for 5%, 0 for zero-rated or exempt), amount = the line total before VAT.
              If the invoice has no item table, give one line describing the bill with its amount before VAT.
            - subtotal (before VAT), vat_amount, total (including VAT), as printed.
            - Numbers are plain numbers without currency symbols or thousands separators. Use null for anything you cannot read; never guess.
            - is_invoice: false when the document is not an invoice or bill at all.
            - notes: anything the person booking it should know (e.g. handwritten changes, unreadable parts, a credit note).
            """;

        private static JsonObject InvoiceSchema()
        {
            static JsonObject Nullable(string type) => new() { ["type"] = new JsonArray(type, "null") };

            var line = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["description"] = new JsonObject { ["type"] = "string" },
                    ["quantity"] = Nullable("number"),
                    ["unit_price"] = Nullable("number"),
                    ["vat_rate"] = Nullable("number"),
                    ["amount"] = Nullable("number")
                },
                ["required"] = new JsonArray("description", "quantity", "unit_price", "vat_rate", "amount")
            };

            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["is_invoice"] = new JsonObject { ["type"] = "boolean" },
                    ["supplier_name"] = Nullable("string"),
                    ["supplier_trn"] = Nullable("string"),
                    ["invoice_number"] = Nullable("string"),
                    ["invoice_date"] = Nullable("string"),
                    ["due_date"] = Nullable("string"),
                    ["currency"] = Nullable("string"),
                    ["subtotal"] = Nullable("number"),
                    ["vat_amount"] = Nullable("number"),
                    ["total"] = Nullable("number"),
                    ["notes"] = Nullable("string"),
                    ["lines"] = new JsonObject { ["type"] = "array", ["items"] = line }
                },
                ["required"] = new JsonArray("is_invoice", "supplier_name", "supplier_trn", "invoice_number", "invoice_date",
                    "due_date", "currency", "subtotal", "vat_amount", "total", "notes", "lines")
            };
        }

        // =====================================================================
        //  MATCHING
        // =====================================================================

        /// <summary>Finds the vendor and the usual accounts, and builds the bill form.</summary>
        private async Task<ScanResult> MatchAsync(ScannedInvoice invoice, int companyId, string fileName)
        {
            var warnings = new List<string>();
            if (!invoice.IsInvoice)
                warnings.Add("This does not look like an invoice. Check the document.");
            if (!string.IsNullOrWhiteSpace(invoice.Notes))
                warnings.Add("Reader's note: " + invoice.Notes);

            var vendors = await _db.Vendors.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive).ToListAsync();
            var trn = Digits(invoice.SupplierTrn);
            var vendor = trn.Length >= 10 ? vendors.FirstOrDefault(x => Digits(x.TrnNumber) == trn) : null;
            if (vendor == null && !string.IsNullOrWhiteSpace(invoice.SupplierName))
            {
                var name = Simplify(invoice.SupplierName);
                vendor = vendors.FirstOrDefault(x => Simplify(x.Name) == name)
                         ?? vendors.Where(x => Simplify(x.Name).Length >= 4 && (name.Contains(Simplify(x.Name)) || Simplify(x.Name).Contains(name)))
                                   .OrderByDescending(x => x.Name.Length).FirstOrDefault();
            }

            if (vendor == null)
                warnings.Add($"No vendor matched \"{invoice.SupplierName}\"" + (trn.Length > 0 ? $" (TRN {trn})" : string.Empty) +
                             ". Select the supplier, or create it first.");

            // The accounts this vendor's earlier bills were charged to, most used first.
            var usualAccounts = vendor == null
                ? new List<int>()
                : await _db.VendorBillLines.AsNoTracking()
                    .Where(x => x.VendorBill!.VendorId == vendor.Id && x.AccountId != null)
                    .GroupBy(x => x.AccountId!.Value)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .Take(1)
                    .ToListAsync();
            var defaultAccount = usualAccounts.Count > 0 ? usualAccounts[0] : (int?)null;

            var model = _bills.BuildNew();
            var billDate = ParseDate(invoice.InvoiceDate);
            if (billDate == null)
                warnings.Add("The invoice date could not be read; today's date is filled in.");
            model.VendorId = vendor?.Id ?? 0;
            model.VendorCanChargeVat = vendor?.CanChargeVat ?? false;
            model.VendorTrn = vendor?.TrnNumber;
            model.VendorReference = Cut(invoice.InvoiceNumber, 100);
            model.BillDate = billDate ?? DateTime.Today;
            model.AccountingDate = model.BillDate;
            model.DueDate = ParseDate(invoice.DueDate) is { } due && due >= model.BillDate ? due : model.BillDate.AddDays(30);
            model.ExpenseCategory = "Scanned invoice";
            model.Notes = Cut($"Read from the scanned invoice {fileName}" +
                              (invoice.SupplierName != null ? $" issued by {invoice.SupplierName.Trim().TrimEnd('.')}" : string.Empty) + ".", 1000);
            model.ScannedFileName = Cut(fileName, 200);
            model.ReceivedSupplierName = Cut(invoice.SupplierName, 200);
            model.ReceivedNet = invoice.Subtotal;
            model.ReceivedVat = invoice.VatAmount;
            model.ReceivedTotal = invoice.Total;

            var invoiceHasVat = (invoice.VatAmount ?? 0m) > 0m;
            model.Lines = invoice.Lines
                .Where(x => !string.IsNullOrWhiteSpace(x.Description) || (x.Amount ?? 0m) != 0m)
                .Take(50)
                .Select(x => ToLine(x, defaultAccount, invoiceHasVat))
                .ToList();
            if (model.Lines.Count == 0)
                model.Lines.Add(new VendorBillLineViewModel { Quantity = 1m, AccountId = defaultAccount });

            if (invoice.Currency != null && !string.Equals(invoice.Currency.Trim(), "AED", StringComparison.OrdinalIgnoreCase)
                                         && !invoice.Currency.Contains("درهم"))
                warnings.Add($"The invoice is in {invoice.Currency}; amounts are filled in as printed. Convert them if needed.");
            if (invoiceHasVat && vendor != null && !vendor.CanChargeVat)
                warnings.Add($"The invoice charges VAT, but {vendor.Name} is not set up as VAT registered with a TRN, so no VAT will be booked.");

            var net = model.Lines.Sum(x => Math.Round(x.Quantity * x.UnitPrice, 2));
            if (invoice.Subtotal.HasValue && Math.Abs(net - invoice.Subtotal.Value) > 0.05m)
                warnings.Add($"The lines add up to {net:N2} but the invoice subtotal is {invoice.Subtotal:N2}. A discount or a misread line may explain it.");
            if (defaultAccount == null)
                warnings.Add("Choose the expense account for each line.");

            return new ScanResult
            {
                Invoice = invoice,
                Bill = model,
                VendorCode = vendor?.Code,
                VendorName = vendor?.Name,
                Warnings = warnings
            };
        }

        private static VendorBillLineViewModel ToLine(ScannedLine x, int? accountId, bool invoiceHasVat)
        {
            var amount = x.Amount ?? (x.Quantity ?? 1m) * (x.UnitPrice ?? 0m);
            var quantity = x.Quantity is > 0m ? x.Quantity.Value : 1m;
            var unitPrice = x.UnitPrice ?? (quantity == 0m ? amount : amount / quantity);

            // Keep their quantity and price when they reproduce the line total;
            // otherwise one line at the printed total, so the bill matches theirs.
            unitPrice = Math.Round(unitPrice, 2);
            if (Math.Abs(Math.Round(quantity * unitPrice, 2) - amount) > 0.01m)
            {
                quantity = 1m;
                unitPrice = amount;
            }

            return new VendorBillLineViewModel
            {
                Description = Cut(x.Description?.Trim(), 250) ?? string.Empty,
                AccountId = accountId,
                Quantity = quantity,
                UnitPrice = unitPrice,
                IsTaxable = x.VatRate.HasValue ? x.VatRate.Value > 0m : invoiceHasVat
            };
        }

        private static DateTime? ParseDate(string? text) =>
            DateTime.TryParseExact(text?.Trim(), new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" }, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var value) && value.Year > 2000 && value.Year < 2100
                ? value.Date
                : null;

        private static string Digits(string? text) => new((text ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        private static string Simplify(string text)
        {
            var words = new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w is not ("llc" or "l" or "fze" or "fzc" or "fzco" or "co" or "est" or "establishment" or "trading" or "the" or "company" or "ltd"));
            return string.Join(" ", words);
        }

        private static string? Cut(string? text, int max) =>
            text == null ? null : text.Length <= max ? text : text[..max];
    }
}
