using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// UAE electronic invoicing: decides which documents are in scope, checks that the
    /// data behind them is complete, produces the PINT AE file, and tracks each file
    /// through the company's Accredited Service Provider (ASP).
    ///
    /// HOW THE MANDATE WORKS
    ///   The supplier never sends an e-invoice to the customer directly. It gives the
    ///   file to its ASP, which validates it, delivers it over the Peppol network to the
    ///   customer's ASP and reports the tax data to the FTA (the "five-corner" model).
    ///   Every B2B and B2G invoice and credit note is in scope; B2C is not, for now.
    ///   The file must reach the ASP within 14 days of the supply.
    ///
    /// WHAT THIS CLASS DOES NOT DO
    ///   It does not talk to any ASP. Providers expose different interfaces, and a
    ///   subscriber may use any accredited one, so the exchange is by file: the user
    ///   downloads the generated XML (one or a batch) and uploads it in the provider's
    ///   portal, then records the provider's answer here. A direct connection for a
    ///   specific provider can be added behind the same statuses without changing
    ///   anything else.
    ///
    /// The file follows PINT AE Billing 1.0.4 (https://docs.peppol.eu/poac/ae/pint-ae/)
    /// and is checked against the official validation rules by
    /// Tools/EInvoicing/validate_einvoice.py.
    /// </summary>
    public class EInvoiceService
    {
        public const string CustomizationId = "urn:peppol:pint:billing-1@ae-1";
        public const string ProfileId = "urn:peppol:bis:billing";

        /// <summary>The Peppol electronic address scheme for a UAE TIN.</summary>
        public const string UaeEndpointScheme = "0235";

        /// <summary>Days after the supply within which the file must reach the provider.</summary>
        public const int SubmissionWindowDays = 14;

        public const string KindInvoice = "Invoice";
        public const string KindCreditNote = "CreditNote";

        private const string NsInvoice = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
        private const string NsCreditNote = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";
        private const string NsCac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
        private const string NsCbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

        private static readonly Regex TrnPattern = new(@"^1\d{12}03$", RegexOptions.Compiled);
        private static readonly Regex TinPattern = new(@"^1\d{9}$", RegexOptions.Compiled);
        private static readonly Regex CountryPattern = new(@"^[A-Z]{2}$", RegexOptions.Compiled);

        private readonly ApplicationDbContext _db;

        public EInvoiceService(ApplicationDbContext db)
        {
            _db = db;
        }

        // =====================================================================
        //  SCOPE
        // =====================================================================

        /// <summary>First supply date the company must (or chose to) e-invoice.</summary>
        public static DateTime? StartDate(Company company) =>
            company.EInvoicingStartDate ?? EInvoicingRevenueBands.MandatoryFrom(company.EInvoicingRevenueBand);

        /// <summary>
        /// Whether a document for this customer and supply date must go on the network.
        /// Pending means yes and nothing has been produced yet.
        /// </summary>
        public static string DetermineStatus(Company company, Tenant tenant, DateTime supplyDate)
        {
            if (!company.EInvoicingEnabled)
                return EInvoiceStatuses.NotRequired;

            if (!PartyTypes.IsInScope(tenant.PartyType))
                return EInvoiceStatuses.NotRequired;

            var start = StartDate(company);
            if (start == null || supplyDate.Date < start.Value.Date)
                return EInvoiceStatuses.NotRequired;

            return EInvoiceStatuses.Pending;
        }

        public static DateTime DeadlineFor(DateTime supplyDate) =>
            supplyDate.Date.AddDays(SubmissionWindowDays);

        public static DateTime SupplyDateOf(CustomerInvoice invoice) =>
            (invoice.DateOfSupply ?? invoice.InvoiceDate).Date;

        /// <summary>
        /// Re-applies the scope rule to every posted document that has not been produced
        /// yet. Needed when the company switches e-invoicing on, changes its start date,
        /// or reclassifies a tenant - documents posted before the change are otherwise
        /// stuck with the answer given at the time. Anything already generated or sent is
        /// left alone.
        /// </summary>
        public async Task<int> ReassessAsync(int companyId)
        {
            var company = await _db.Companies.FirstAsync(x => x.Id == companyId);
            var changed = 0;

            var invoices = await _db.CustomerInvoices
                .Include(x => x.Lease)!.ThenInclude(x => x!.Tenant)
                .Where(x => x.CompanyId == companyId
                            && x.JournalEntry != null && x.JournalEntry.IsPosted
                            && (x.EInvoiceStatus == EInvoiceStatuses.NotRequired
                                || x.EInvoiceStatus == EInvoiceStatuses.Pending))
                .ToListAsync();

            foreach (var invoice in invoices)
            {
                var tenant = invoice.Lease?.Tenant;
                if (tenant == null) continue;

                var status = DetermineStatus(company, tenant, SupplyDateOf(invoice));
                if (status != invoice.EInvoiceStatus)
                {
                    invoice.EInvoiceStatus = status;
                    changed++;
                }
            }

            var creditNotes = await _db.CustomerCreditNotes
                .Include(x => x.CustomerInvoice)!.ThenInclude(x => x!.Lease)!.ThenInclude(x => x!.Tenant)
                .Where(x => x.CompanyId == companyId
                            && x.JournalEntry != null && x.JournalEntry.IsPosted
                            && (x.EInvoiceStatus == EInvoiceStatuses.NotRequired
                                || x.EInvoiceStatus == EInvoiceStatuses.Pending))
                .ToListAsync();

            foreach (var note in creditNotes)
            {
                var tenant = note.CustomerInvoice?.Lease?.Tenant;
                if (tenant == null) continue;

                var status = DetermineCreditNoteStatus(company, tenant, note);
                if (status != note.EInvoiceStatus)
                {
                    note.EInvoiceStatus = status;
                    changed++;
                }
            }

            await _db.SaveChangesAsync();
            return changed;
        }

        /// <summary>
        /// A credit note follows its invoice: it goes on the network when the invoice did,
        /// so the buyer's provider can match the two. One against an invoice issued before
        /// go-live is judged on its own date.
        /// </summary>
        public static string DetermineCreditNoteStatus(Company company, Tenant tenant, CustomerCreditNote note)
        {
            var invoice = note.CustomerInvoice;
            if (invoice != null && invoice.EInvoiceStatus != EInvoiceStatuses.NotRequired)
                return company.EInvoicingEnabled ? EInvoiceStatuses.Pending : EInvoiceStatuses.NotRequired;

            return DetermineStatus(company, tenant, note.CreditNoteDate);
        }

        // =====================================================================
        //  READINESS
        // =====================================================================

        /// <summary>
        /// What is missing from the company's own record. Each message says where to fix
        /// it, because the person reading it is an accountant, not a developer.
        /// </summary>
        public static List<string> CheckCompany(Company company)
        {
            var problems = new List<string>();
            const string where = "System Setup ▸ E-Invoicing Settings";

            if (string.IsNullOrWhiteSpace(company.LegalName) && string.IsNullOrWhiteSpace(company.Name))
                problems.Add($"The company's registered legal name is missing ({where}).");

            var tin = company.EffectiveTin;
            if (string.IsNullOrWhiteSpace(tin))
                problems.Add($"The company has no TIN or TRN. The TIN is its address on the e-invoicing network ({where}).");
            else if (!TinPattern.IsMatch(tin))
                problems.Add($"The company TIN \"{tin}\" is not valid - it must be 10 digits starting with 1 ({where}).");

            if (!string.IsNullOrWhiteSpace(company.TrnNumber) && !TrnPattern.IsMatch(company.TrnNumber.Trim()))
                problems.Add($"The company TRN \"{company.TrnNumber}\" is not valid - it must be 15 digits, starting with 1 and ending with 03 ({where}).");

            if (string.IsNullOrWhiteSpace(company.TradeLicenseNumber))
                problems.Add($"The company's trade licence (or other registration) number is missing ({where}).");

            if (company.LegalRegistrationType == LegalRegistrationTypes.TradeLicence
                && string.IsNullOrWhiteSpace(company.LicensingAuthority))
                problems.Add($"Enter the authority that issued the company's trade licence ({where}).");

            if (string.IsNullOrWhiteSpace(company.AddressLine1))
                problems.Add($"The company's street address is missing ({where}).");

            if (string.IsNullOrWhiteSpace(company.City))
                problems.Add($"The company's city is missing ({where}).");

            if (!Emirates.IsValid(company.Emirate))
                problems.Add($"Select the emirate of the company's registered address ({where}).");

            if (!string.Equals(company.BaseCurrency, "AED", StringComparison.OrdinalIgnoreCase))
                problems.Add("E-invoices are produced in AED. This company's base currency is " + company.BaseCurrency + ".");

            if (company.EInvoicePaymentMeansCode == EInvoicePaymentMeans.CreditTransfer
                && string.IsNullOrWhiteSpace(company.BankIban))
                problems.Add($"Payment by bank transfer is selected, so the company IBAN is required ({where}).");

            return problems;
        }

        /// <summary>What is missing from a business tenant's record.</summary>
        public static List<string> CheckTenant(Tenant tenant)
        {
            var problems = new List<string>();
            var where = $"Master Data ▸ Tenants ▸ {tenant.Code}";

            var tin = tenant.EffectiveTin;
            if (string.IsNullOrWhiteSpace(tin))
                problems.Add($"{tenant.FullName} has no TIN or TRN. Every business on the e-invoicing network is identified by its TIN ({where}).");
            else if (!TinPattern.IsMatch(tin))
                problems.Add($"{tenant.FullName}: the TIN \"{tin}\" is not valid - it must be 10 digits starting with 1 ({where}).");

            if (!string.IsNullOrWhiteSpace(tenant.TrnNumber) && !TrnPattern.IsMatch(tenant.TrnNumber.Trim()))
                problems.Add($"{tenant.FullName}: the TRN \"{tenant.TrnNumber}\" is not valid - 15 digits, starting with 1 and ending with 03 ({where}).");

            var (type, number) = ResolveRegistration(tenant);
            if (type == null || string.IsNullOrWhiteSpace(number))
                problems.Add($"{tenant.FullName}: enter the trade licence number (or Emirates ID / passport) that identifies them ({where}).");
            else if (type == LegalRegistrationTypes.TradeLicence && string.IsNullOrWhiteSpace(tenant.LicensingAuthority))
                problems.Add($"{tenant.FullName}: enter the authority that issued their trade licence ({where}).");
            else if (type == LegalRegistrationTypes.Passport
                     && (string.IsNullOrWhiteSpace(tenant.PassportIssuingCountry) || !CountryPattern.IsMatch(tenant.PassportIssuingCountry)))
                problems.Add($"{tenant.FullName}: enter the passport's issuing country as a two-letter code, e.g. IN or GB ({where}).");

            if (string.IsNullOrWhiteSpace(tenant.Address))
                problems.Add($"{tenant.FullName}: the street address is missing ({where}).");

            if (string.IsNullOrWhiteSpace(tenant.City))
                problems.Add($"{tenant.FullName}: the city is missing ({where}).");

            var country = (tenant.CountryCode ?? "AE").Trim().ToUpperInvariant();
            if (!CountryPattern.IsMatch(country))
                problems.Add($"{tenant.FullName}: the country code must be two letters, e.g. AE ({where}).");
            else if (country == "AE" && !Emirates.IsValid(tenant.Emirate))
                problems.Add($"{tenant.FullName}: select the emirate of their address ({where}).");

            return problems;
        }

        /// <summary>
        /// Which registration identifies the tenant, and its number. Uses the type chosen
        /// on the tenant; otherwise the strongest identifier on file.
        /// </summary>
        public static (string? Type, string? Number) ResolveRegistration(Tenant tenant)
        {
            var type = tenant.LegalRegistrationType;

            if (string.IsNullOrWhiteSpace(type))
            {
                type = !string.IsNullOrWhiteSpace(tenant.TradeLicenseNumber) ? LegalRegistrationTypes.TradeLicence
                    : !string.IsNullOrWhiteSpace(tenant.IdNumber) ? LegalRegistrationTypes.EmiratesId
                    : !string.IsNullOrWhiteSpace(tenant.PassportNumber) ? LegalRegistrationTypes.Passport
                    : null;
            }

            var number = type switch
            {
                LegalRegistrationTypes.TradeLicence => tenant.TradeLicenseNumber,
                LegalRegistrationTypes.CabinetDecision => tenant.TradeLicenseNumber,
                LegalRegistrationTypes.EmiratesId => tenant.IdNumber,
                LegalRegistrationTypes.Passport => tenant.PassportNumber,
                _ => null
            };

            return (type, number?.Trim());
        }

        // =====================================================================
        //  GENERATE
        // =====================================================================

        /// <summary>
        /// Produces the file for one invoice. Refused - with every reason, not just the
        /// first - when the data is incomplete, so the user can fix everything in one go.
        /// </summary>
        public async Task<EInvoiceResult> GenerateInvoiceAsync(int invoiceId)
        {
            var invoice = await _db.CustomerInvoices
                .Include(x => x.JournalEntry)
                .Include(x => x.Lease)!.ThenInclude(x => x!.Tenant)
                .Include(x => x.Lease)!.ThenInclude(x => x!.Unit)!.ThenInclude(x => x!.Property)
                .FirstOrDefaultAsync(x => x.Id == invoiceId);

            if (invoice?.Lease?.Tenant == null || invoice.Lease.Unit?.Property == null)
                return EInvoiceResult.Failed("Invoice not found.");

            var label = invoice.TaxInvoiceNumber ?? invoice.InvoiceNumber;

            if (invoice.JournalEntry?.IsPosted != true)
                return EInvoiceResult.Failed($"{label} is not posted. Only a posted invoice can be issued.");

            if (EInvoiceStatuses.IsLocked(invoice.EInvoiceStatus))
                return EInvoiceResult.Failed($"{label} has already been sent to the provider. Correct it with a credit note.");

            if (invoice.EInvoiceStatus == EInvoiceStatuses.NotRequired)
                return EInvoiceResult.Failed($"{label} does not need an e-invoice (individual tenant, or before the start date).");

            var company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == invoice.CompanyId);

            var problems = CheckCompany(company);
            problems.AddRange(CheckTenant(invoice.Lease.Tenant));

            if (invoice.Subtotal + invoice.VatAmount != invoice.TotalAmount)
                problems.Add($"{label}: the net amount and VAT do not add up to the total.");

            if (!company.IsVatRegistered && invoice.VatAmount != 0m)
                problems.Add($"{label} charges VAT, but the company has no TRN. Only a VAT registered business may charge VAT - " +
                             "enter the TRN in E-Invoicing Settings, or credit this invoice and re-issue it without VAT.");

            if (problems.Count > 0)
                return EInvoiceResult.Failed($"{label} cannot be issued yet.", problems);

            // A fresh identifier after a rejection: the provider refused the old one, and
            // some providers refuse to see the same identifier twice.
            if (invoice.EInvoiceUuid == null || invoice.EInvoiceStatus == EInvoiceStatuses.Rejected)
                invoice.EInvoiceUuid = Guid.NewGuid();

            invoice.EInvoiceXml = BuildInvoiceXml(invoice, company);
            invoice.EInvoiceStatus = EInvoiceStatuses.Generated;
            invoice.EInvoiceGeneratedAt = DateTime.UtcNow;
            invoice.EInvoiceRejectionReason = null;

            await _db.SaveChangesAsync();
            return EInvoiceResult.Succeeded($"E-invoice for {label} is ready to send.");
        }

        public async Task<EInvoiceResult> GenerateCreditNoteAsync(int creditNoteId)
        {
            var note = await _db.CustomerCreditNotes
                .Include(x => x.JournalEntry)
                .Include(x => x.CustomerInvoice)!.ThenInclude(x => x!.Lease)!.ThenInclude(x => x!.Tenant)
                .Include(x => x.CustomerInvoice)!.ThenInclude(x => x!.Lease)!.ThenInclude(x => x!.Unit)!.ThenInclude(x => x!.Property)
                .FirstOrDefaultAsync(x => x.Id == creditNoteId);

            if (note?.CustomerInvoice?.Lease?.Tenant == null || note.CustomerInvoice.Lease.Unit?.Property == null)
                return EInvoiceResult.Failed("Credit note not found.");

            var label = note.CreditNoteNumber;

            if (note.JournalEntry?.IsPosted != true)
                return EInvoiceResult.Failed($"{label} is not posted. Only a posted credit note can be issued.");

            if (EInvoiceStatuses.IsLocked(note.EInvoiceStatus))
                return EInvoiceResult.Failed($"{label} has already been sent to the provider.");

            if (note.EInvoiceStatus == EInvoiceStatuses.NotRequired)
                return EInvoiceResult.Failed($"{label} does not need an e-invoice.");

            var invoice = note.CustomerInvoice;

            // The buyer's provider matches a credit note to the invoice it reduces, so the
            // invoice has to be on the network first.
            if (invoice.EInvoiceStatus != EInvoiceStatuses.NotRequired && !EInvoiceStatuses.IsLocked(invoice.EInvoiceStatus))
            {
                return EInvoiceResult.Failed(
                    $"{label} reduces invoice {invoice.TaxInvoiceNumber ?? invoice.InvoiceNumber}, which has not been sent yet. " +
                    "Send the invoice first.");
            }

            var company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == note.CompanyId);

            var problems = CheckCompany(company);
            problems.AddRange(CheckTenant(invoice.Lease!.Tenant!));

            if (!company.IsVatRegistered && note.VatAmount != 0m)
                problems.Add($"{label} reverses VAT, but the company has no TRN. Enter the TRN in E-Invoicing Settings.");

            if (problems.Count > 0)
                return EInvoiceResult.Failed($"{label} cannot be issued yet.", problems);

            if (note.EInvoiceUuid == null || note.EInvoiceStatus == EInvoiceStatuses.Rejected)
                note.EInvoiceUuid = Guid.NewGuid();

            note.EInvoiceXml = BuildCreditNoteXml(note, company);
            note.EInvoiceStatus = EInvoiceStatuses.Generated;
            note.EInvoiceGeneratedAt = DateTime.UtcNow;
            note.EInvoiceRejectionReason = null;

            await _db.SaveChangesAsync();
            return EInvoiceResult.Succeeded($"E-credit note {label} is ready to send.");
        }

        /// <summary>Generates everything waiting. Each document stands alone; failures are listed.</summary>
        public async Task<EInvoiceBatchResult> GenerateAllPendingAsync(int companyId)
        {
            var batch = new EInvoiceBatchResult();

            var invoiceIds = await _db.CustomerInvoices
                .Where(x => x.CompanyId == companyId
                            && (x.EInvoiceStatus == EInvoiceStatuses.Pending || x.EInvoiceStatus == EInvoiceStatuses.Rejected))
                .OrderBy(x => x.InvoiceDate).ThenBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var id in invoiceIds)
                batch.Add(await GenerateInvoiceAsync(id));

            var noteIds = await _db.CustomerCreditNotes
                .Where(x => x.CompanyId == companyId
                            && (x.EInvoiceStatus == EInvoiceStatuses.Pending || x.EInvoiceStatus == EInvoiceStatuses.Rejected))
                .OrderBy(x => x.CreditNoteDate).ThenBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var id in noteIds)
                batch.Add(await GenerateCreditNoteAsync(id));

            return batch;
        }

        // =====================================================================
        //  PROVIDER EXCHANGE
        // =====================================================================

        /// <summary>
        /// Records that files were handed to the provider. From this moment the documents
        /// are locked: they are part of the FTA's record.
        /// </summary>
        public async Task<int> MarkSubmittedAsync(int companyId, IEnumerable<EInvoiceDocumentRef> documents, string? providerReference)
        {
            var refs = documents.ToList();
            var now = DateTime.UtcNow;
            var count = 0;

            var invoiceIds = refs.Where(x => x.Kind == KindInvoice).Select(x => x.Id).ToList();
            var noteIds = refs.Where(x => x.Kind == KindCreditNote).Select(x => x.Id).ToList();

            foreach (var invoice in await _db.CustomerInvoices
                         .Where(x => x.CompanyId == companyId && invoiceIds.Contains(x.Id)
                                     && x.EInvoiceStatus == EInvoiceStatuses.Generated)
                         .ToListAsync())
            {
                invoice.EInvoiceStatus = EInvoiceStatuses.Submitted;
                invoice.EInvoiceSubmittedAt = now;
                invoice.EInvoiceProviderReference = Trim(providerReference) ?? invoice.EInvoiceProviderReference;
                count++;
            }

            foreach (var note in await _db.CustomerCreditNotes
                         .Where(x => x.CompanyId == companyId && noteIds.Contains(x.Id)
                                     && x.EInvoiceStatus == EInvoiceStatuses.Generated)
                         .ToListAsync())
            {
                note.EInvoiceStatus = EInvoiceStatuses.Submitted;
                note.EInvoiceSubmittedAt = now;
                note.EInvoiceProviderReference = Trim(providerReference) ?? note.EInvoiceProviderReference;
                count++;
            }

            await _db.SaveChangesAsync();
            return count;
        }

        /// <summary>
        /// The provider's answer. Accepted is final. Rejected returns the document to the
        /// queue so the cause can be fixed and the file generated again.
        /// </summary>
        public async Task<EInvoiceResult> RecordResponseAsync(
            int companyId, EInvoiceDocumentRef document, bool accepted, string? providerReference, string? reason)
        {
            if (!accepted && string.IsNullOrWhiteSpace(reason))
                return EInvoiceResult.Failed("Enter the reason the provider gave for the rejection.");

            if (document.Kind == KindInvoice)
            {
                var invoice = await _db.CustomerInvoices.FirstOrDefaultAsync(x => x.Id == document.Id && x.CompanyId == companyId);
                if (invoice == null) return EInvoiceResult.Failed("Invoice not found.");

                var problem = ApplyResponse(invoice.EInvoiceStatus, accepted);
                if (problem != null) return EInvoiceResult.Failed(problem);

                invoice.EInvoiceStatus = accepted ? EInvoiceStatuses.Accepted : EInvoiceStatuses.Rejected;
                invoice.EInvoiceResponseAt = DateTime.UtcNow;
                invoice.EInvoiceProviderReference = Trim(providerReference) ?? invoice.EInvoiceProviderReference;
                invoice.EInvoiceRejectionReason = accepted ? null : Trim(reason);
            }
            else
            {
                var note = await _db.CustomerCreditNotes.FirstOrDefaultAsync(x => x.Id == document.Id && x.CompanyId == companyId);
                if (note == null) return EInvoiceResult.Failed("Credit note not found.");

                var problem = ApplyResponse(note.EInvoiceStatus, accepted);
                if (problem != null) return EInvoiceResult.Failed(problem);

                note.EInvoiceStatus = accepted ? EInvoiceStatuses.Accepted : EInvoiceStatuses.Rejected;
                note.EInvoiceResponseAt = DateTime.UtcNow;
                note.EInvoiceProviderReference = Trim(providerReference) ?? note.EInvoiceProviderReference;
                note.EInvoiceRejectionReason = accepted ? null : Trim(reason);
            }

            await _db.SaveChangesAsync();
            return EInvoiceResult.Succeeded(accepted
                ? "Recorded as accepted by the provider."
                : "Recorded as rejected. Fix the cause, then generate the e-invoice again.");
        }

        private static string? ApplyResponse(string status, bool accepted)
        {
            if (status == EInvoiceStatuses.Submitted)
                return null;

            // A file the provider refused before it was formally marked as sent - e.g. its
            // upload screen rejected it on the spot - can be recorded straight away.
            if (status == EInvoiceStatuses.Generated && !accepted)
                return null;

            return status == EInvoiceStatuses.Accepted
                ? "The provider has already accepted this document."
                : "Mark the document as sent to the provider first.";
        }

        // =====================================================================
        //  XML - INVOICE
        // =====================================================================

        /// <summary>
        /// The PINT AE invoice. Element order follows the UBL 2.1 schema exactly - a
        /// provider validates against the schema before applying the business rules.
        /// </summary>
        public static string BuildInvoiceXml(CustomerInvoice invoice, Company company)
        {
            var lease = invoice.Lease!;
            var tenant = lease.Tenant!;
            var unit = lease.Unit!;
            var property = unit.Property!;

            var treatment = Treatment.For(company, invoice.VatCategory, invoice.VatRate);
            var (periodStart, periodEnd) = BillingPeriod(invoice, lease);
            var frequency = FrequencyCode(lease.BillingFrequencyMonths);
            var number = invoice.TaxInvoiceNumber ?? invoice.InvoiceNumber;

            var line = new LineData(
                Name: "Rent",
                Description: $"Rent for {property.Name}, unit {unit.UnitNumber} - contract {lease.ContractNumber}, " +
                             $"{periodStart:dd/MM/yyyy} to {periodEnd:dd/MM/yyyy}",
                Net: invoice.Subtotal,
                Vat: invoice.VatAmount,
                PeriodStart: periodStart,
                PeriodEnd: periodEnd);

            return Write(w =>
            {
                w.WriteStartElement("Invoice", NsInvoice);
                w.WriteAttributeString("xmlns", "cac", null, NsCac);
                w.WriteAttributeString("xmlns", "cbc", null, NsCbc);

                Cbc(w, "CustomizationID", CustomizationId);
                Cbc(w, "ProfileID", ProfileId);
                // A lease is billed on a recurring basis: a continuous supply (flag 5 of 8).
                Cbc(w, "ProfileExecutionID", "00001000");
                Cbc(w, "ID", number);
                Cbc(w, "UUID", invoice.EInvoiceUuid!.Value.ToString());
                Cbc(w, "IssueDate", Date(invoice.InvoiceDate));
                Cbc(w, "DueDate", Date(invoice.DueDate < invoice.InvoiceDate ? invoice.InvoiceDate : invoice.DueDate));
                Cbc(w, "InvoiceTypeCode", treatment.InvoiceTypeCode);

                var note = treatment.Note;
                if (frequency == "OTH")
                    note += $" Rent is billed every {lease.BillingFrequencyMonths} months.";
                Cbc(w, "Note", note);

                // The VAT point date may only be given when it falls before the issue date.
                var supplyDate = SupplyDateOf(invoice);
                if (supplyDate < invoice.InvoiceDate.Date)
                    Cbc(w, "TaxPointDate", Date(supplyDate));

                Cbc(w, "DocumentCurrencyCode", "AED");

                w.WriteStartElement("cac", "InvoicePeriod", NsCac);
                Cbc(w, "StartDate", Date(periodStart));
                Cbc(w, "EndDate", Date(periodEnd));
                Cbc(w, "Description", frequency);
                w.WriteEndElement();

                w.WriteStartElement("cac", "ContractDocumentReference", NsCac);
                Cbc(w, "ID", lease.ContractNumber);
                Cbc(w, "DocumentDescription", "AED " + Amount(ContractValue(lease)));
                w.WriteEndElement();

                WriteSupplier(w, company, treatment);
                WriteCustomer(w, tenant);

                w.WriteStartElement("cac", "PaymentMeans", NsCac);
                var meansCode = string.IsNullOrWhiteSpace(company.EInvoicePaymentMeansCode)
                    ? EInvoicePaymentMeans.NotDefined
                    : company.EInvoicePaymentMeansCode;
                w.WriteStartElement("cbc", "PaymentMeansCode", NsCbc);
                w.WriteAttributeString("name", EInvoicePaymentMeans.OfficialName(meansCode));
                w.WriteString(meansCode);
                w.WriteEndElement();
                Cbc(w, "PaymentID", number);
                if (meansCode == EInvoicePaymentMeans.CreditTransfer)
                {
                    w.WriteStartElement("cac", "PayeeFinancialAccount", NsCac);
                    w.WriteStartElement("cbc", "ID", NsCbc);
                    w.WriteAttributeString("schemeID", "IBAN");
                    w.WriteString(company.BankIban!.Replace(" ", string.Empty).Trim());
                    w.WriteEndElement();
                    CbcIf(w, "Name", company.BankAccountName);
                    w.WriteEndElement();
                }
                w.WriteEndElement();

                w.WriteStartElement("cac", "PaymentTerms", NsCac);
                Cbc(w, "Note", $"Payable by {invoice.DueDate:dd MMMM yyyy}");
                w.WriteEndElement();

                WriteTaxTotal(w, treatment, line.Net, line.Vat);
                WriteMonetaryTotal(w, line.Net, line.Vat);
                WriteLine(w, "InvoiceLine", "InvoicedQuantity", treatment, line);

                w.WriteEndElement();
            });
        }

        // =====================================================================
        //  XML - CREDIT NOTE
        // =====================================================================

        public static string BuildCreditNoteXml(CustomerCreditNote note, Company company)
        {
            var invoice = note.CustomerInvoice!;
            var lease = invoice.Lease!;
            var tenant = lease.Tenant!;
            var unit = lease.Unit!;
            var property = unit.Property!;

            var treatment = Treatment.For(company, note.VatCategory, note.VatRate);
            var (periodStart, periodEnd) = BillingPeriod(invoice, lease);

            var line = new LineData(
                Name: "Rent credit",
                Description: $"Credit against invoice {invoice.TaxInvoiceNumber ?? invoice.InvoiceNumber} - " +
                             $"{property.Name}, unit {unit.UnitNumber}, contract {lease.ContractNumber}. {note.Reason}",
                Net: note.Subtotal,
                Vat: note.VatAmount,
                PeriodStart: periodStart,
                PeriodEnd: periodEnd);

            return Write(w =>
            {
                w.WriteStartElement("CreditNote", NsCreditNote);
                w.WriteAttributeString("xmlns", "cac", null, NsCac);
                w.WriteAttributeString("xmlns", "cbc", null, NsCbc);

                Cbc(w, "CustomizationID", CustomizationId);
                Cbc(w, "ProfileID", ProfileId);
                Cbc(w, "ProfileExecutionID", "00001000");
                Cbc(w, "ID", note.CreditNoteNumber);
                Cbc(w, "UUID", note.EInvoiceUuid!.Value.ToString());
                Cbc(w, "IssueDate", Date(note.CreditNoteDate));
                // 381 reverses a tax invoice; 81 reverses an out-of-scope invoice.
                Cbc(w, "CreditNoteTypeCode", treatment.InvoiceTypeCode == "380" ? "381" : "81");
                Cbc(w, "Note", Truncate(note.Reason, 500));
                Cbc(w, "DocumentCurrencyCode", "AED");

                w.WriteStartElement("cac", "InvoicePeriod", NsCac);
                Cbc(w, "StartDate", Date(periodStart));
                Cbc(w, "EndDate", Date(periodEnd));
                w.WriteEndElement();

                w.WriteStartElement("cac", "DiscrepancyResponse", NsCac);
                Cbc(w, "ResponseCode", note.ReasonCode);
                w.WriteEndElement();

                w.WriteStartElement("cac", "BillingReference", NsCac);
                w.WriteStartElement("cac", "InvoiceDocumentReference", NsCac);
                Cbc(w, "ID", invoice.TaxInvoiceNumber ?? invoice.InvoiceNumber);
                Cbc(w, "IssueDate", Date(invoice.InvoiceDate));
                w.WriteEndElement();
                w.WriteEndElement();

                w.WriteStartElement("cac", "ContractDocumentReference", NsCac);
                Cbc(w, "ID", lease.ContractNumber);
                Cbc(w, "DocumentDescription", "AED " + Amount(ContractValue(lease)));
                w.WriteEndElement();

                WriteSupplier(w, company, treatment);
                WriteCustomer(w, tenant);

                WriteTaxTotal(w, treatment, line.Net, line.Vat);
                WriteMonetaryTotal(w, line.Net, line.Vat);
                WriteLine(w, "CreditNoteLine", "CreditedQuantity", treatment, line);

                w.WriteEndElement();
            });
        }

        // =====================================================================
        //  XML - SHARED PARTS
        // =====================================================================

        private static void WriteSupplier(XmlWriter w, Company company, Treatment treatment)
        {
            w.WriteStartElement("cac", "AccountingSupplierParty", NsCac);
            w.WriteStartElement("cac", "Party", NsCac);

            Endpoint(w, company.EffectiveTin!);

            if (!string.IsNullOrWhiteSpace(company.LegalName) && company.LegalName != company.Name)
            {
                w.WriteStartElement("cac", "PartyName", NsCac);
                Cbc(w, "Name", company.Name);
                w.WriteEndElement();
            }

            Address(w, company.AddressLine1!, company.AddressLine2, company.City!, company.Emirate!, company.CountryCode);

            // A registered seller states its TRN under the VAT scheme; one that is not
            // registered states its TIN instead (rules IBR-177 and IBR-178).
            w.WriteStartElement("cac", "PartyTaxScheme", NsCac);
            if (treatment.SellerVatRegistered)
            {
                Cbc(w, "CompanyID", company.TrnNumber!.Trim());
                TaxScheme(w, "VAT");
            }
            else
            {
                Cbc(w, "CompanyID", company.EffectiveTin!);
                TaxScheme(w, "TIN");
            }
            w.WriteEndElement();

            w.WriteStartElement("cac", "PartyLegalEntity", NsCac);
            Cbc(w, "RegistrationName", string.IsNullOrWhiteSpace(company.LegalName) ? company.Name : company.LegalName);
            Registration(w, company.LegalRegistrationType, company.TradeLicenseNumber!, company.LicensingAuthority, null);
            w.WriteEndElement();

            if (!string.IsNullOrWhiteSpace(company.Phone) || !string.IsNullOrWhiteSpace(company.Email))
            {
                w.WriteStartElement("cac", "Contact", NsCac);
                CbcIf(w, "Telephone", company.Phone);
                CbcIf(w, "ElectronicMail", company.Email);
                w.WriteEndElement();
            }

            w.WriteEndElement();
            w.WriteEndElement();
        }

        private static void WriteCustomer(XmlWriter w, Tenant tenant)
        {
            w.WriteStartElement("cac", "AccountingCustomerParty", NsCac);
            w.WriteStartElement("cac", "Party", NsCac);

            Endpoint(w, tenant.EffectiveTin!);

            var country = (tenant.CountryCode ?? "AE").Trim().ToUpperInvariant();
            var subdivision = country == "AE" ? tenant.Emirate! : (tenant.Emirate ?? tenant.City!);
            Address(w, tenant.Address!, null, tenant.City!, subdivision, country);

            if (!string.IsNullOrWhiteSpace(tenant.TrnNumber))
            {
                w.WriteStartElement("cac", "PartyTaxScheme", NsCac);
                Cbc(w, "CompanyID", tenant.TrnNumber.Trim());
                TaxScheme(w, "VAT");
                w.WriteEndElement();
            }

            w.WriteStartElement("cac", "PartyLegalEntity", NsCac);
            Cbc(w, "RegistrationName", tenant.FullName);
            var (type, number) = ResolveRegistration(tenant);
            Registration(w, type, number!, tenant.LicensingAuthority, tenant.PassportIssuingCountry);
            w.WriteEndElement();

            if (!string.IsNullOrWhiteSpace(tenant.Phone) || !string.IsNullOrWhiteSpace(tenant.Email))
            {
                w.WriteStartElement("cac", "Contact", NsCac);
                CbcIf(w, "Telephone", tenant.Phone);
                CbcIf(w, "ElectronicMail", tenant.Email);
                w.WriteEndElement();
            }

            w.WriteEndElement();
            w.WriteEndElement();
        }

        private static void WriteTaxTotal(XmlWriter w, Treatment treatment, decimal net, decimal vat)
        {
            w.WriteStartElement("cac", "TaxTotal", NsCac);
            Money(w, "TaxAmount", vat);
            Cbc(w, "TaxIncludedIndicator", "false");

            w.WriteStartElement("cac", "TaxSubtotal", NsCac);
            Money(w, "TaxableAmount", net);
            Money(w, "TaxAmount", vat);
            TaxCategory(w, "TaxCategory", treatment, includeExemption: false);
            w.WriteEndElement();

            w.WriteEndElement();
        }

        private static void WriteMonetaryTotal(XmlWriter w, decimal net, decimal vat)
        {
            w.WriteStartElement("cac", "LegalMonetaryTotal", NsCac);
            Money(w, "LineExtensionAmount", net);
            Money(w, "TaxExclusiveAmount", net);
            Money(w, "TaxInclusiveAmount", net + vat);
            Money(w, "PayableAmount", net + vat);
            w.WriteEndElement();
        }

        /// <summary>
        /// One line. Quantity 1 at the period's rent keeps quantity x price exactly equal
        /// to the line amount (rule IBR-147), which a monthly price would not for rents
        /// that do not divide evenly.
        /// </summary>
        private static void WriteLine(XmlWriter w, string element, string quantityElement, Treatment treatment, LineData line)
        {
            w.WriteStartElement("cac", element, NsCac);
            Cbc(w, "ID", "1");

            w.WriteStartElement("cbc", quantityElement, NsCbc);
            w.WriteAttributeString("unitCode", "C62");
            w.WriteString("1");
            w.WriteEndElement();

            Money(w, "LineExtensionAmount", line.Net);

            w.WriteStartElement("cac", "InvoicePeriod", NsCac);
            Cbc(w, "StartDate", Date(line.PeriodStart));
            Cbc(w, "EndDate", Date(line.PeriodEnd));
            w.WriteEndElement();

            w.WriteStartElement("cac", "Item", NsCac);
            Cbc(w, "Description", Truncate(line.Description, 1000));
            Cbc(w, "Name", line.Name);
            TaxCategory(w, "ClassifiedTaxCategory", treatment, includeExemption: true);
            w.WriteEndElement();

            w.WriteStartElement("cac", "Price", NsCac);
            Money(w, "PriceAmount", line.Net);
            w.WriteStartElement("cbc", "BaseQuantity", NsCbc);
            w.WriteAttributeString("unitCode", "C62");
            w.WriteString("1");
            w.WriteEndElement();
            // The gross price is required (IBR-126-AE); with no discount it equals the net price.
            w.WriteStartElement("cac", "AllowanceCharge", NsCac);
            Cbc(w, "ChargeIndicator", "false");
            Money(w, "Amount", 0m);
            Money(w, "BaseAmount", line.Net);
            w.WriteEndElement();
            w.WriteEndElement();

            // Line amounts in AED (BTAE-10, BTAE-08). An exempt line carries no VAT amount
            // at all (IBR-163-AE); every other category states it, zero included (IBR-104-AE).
            w.WriteStartElement("cac", "ItemPriceExtension", NsCac);
            Money(w, "Amount", line.Net + line.Vat);
            if (treatment.CategoryCode != "E")
            {
                w.WriteStartElement("cac", "TaxTotal", NsCac);
                Money(w, "TaxAmount", line.Vat);
                w.WriteEndElement();
            }
            w.WriteEndElement();

            w.WriteEndElement();
        }

        private static void TaxCategory(XmlWriter w, string element, Treatment treatment, bool includeExemption)
        {
            w.WriteStartElement("cac", element, NsCac);
            Cbc(w, "ID", treatment.CategoryCode);

            if (treatment.CategoryCode is "S" or "Z")
                Cbc(w, "Percent", Amount(treatment.Rate));

            if (includeExemption && treatment.CategoryCode == "E")
            {
                Cbc(w, "TaxExemptionReasonCode", "DL8.46.2");
                Cbc(w, "TaxExemptionReason", "Supply of residential units (lease or sale)");
            }

            TaxScheme(w, "VAT");
            w.WriteEndElement();
        }

        private static void Endpoint(XmlWriter w, string tin)
        {
            w.WriteStartElement("cbc", "EndpointID", NsCbc);
            w.WriteAttributeString("schemeID", UaeEndpointScheme);
            w.WriteString(tin.Trim());
            w.WriteEndElement();
        }

        private static void Address(XmlWriter w, string line1, string? line2, string city, string subdivision, string? country)
        {
            w.WriteStartElement("cac", "PostalAddress", NsCac);
            Cbc(w, "StreetName", line1.Trim());
            CbcIf(w, "AdditionalStreetName", line2);
            Cbc(w, "CityName", city.Trim());
            Cbc(w, "CountrySubentity", subdivision.Trim());
            w.WriteStartElement("cac", "Country", NsCac);
            Cbc(w, "IdentificationCode", string.IsNullOrWhiteSpace(country) ? "AE" : country.Trim().ToUpperInvariant());
            w.WriteEndElement();
            w.WriteEndElement();
        }

        /// <summary>
        /// Legal registration (IBT-030 / IBT-047). The type travels in schemeAgencyID; the
        /// licensing authority, or a passport's issuing country, in schemeAgencyName.
        /// </summary>
        private static void Registration(XmlWriter w, string? type, string number, string? authority, string? passportCountry)
        {
            w.WriteStartElement("cbc", "CompanyID", NsCbc);
            var code = string.IsNullOrWhiteSpace(type) ? LegalRegistrationTypes.TradeLicence : type;
            w.WriteAttributeString("schemeAgencyID", code);

            var agencyName = code == LegalRegistrationTypes.Passport
                ? passportCountry?.Trim().ToUpperInvariant()
                : authority?.Trim();
            if (!string.IsNullOrWhiteSpace(agencyName))
                w.WriteAttributeString("schemeAgencyName", agencyName);

            w.WriteString(number.Trim());
            w.WriteEndElement();
        }

        private static void TaxScheme(XmlWriter w, string id)
        {
            w.WriteStartElement("cac", "TaxScheme", NsCac);
            Cbc(w, "ID", id);
            w.WriteEndElement();
        }

        private static void Money(XmlWriter w, string name, decimal value)
        {
            w.WriteStartElement("cbc", name, NsCbc);
            w.WriteAttributeString("currencyID", "AED");
            w.WriteString(Amount(value));
            w.WriteEndElement();
        }

        private static void Cbc(XmlWriter w, string name, string value) =>
            w.WriteElementString("cbc", name, NsCbc, value);

        private static void CbcIf(XmlWriter w, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                Cbc(w, name, value.Trim());
        }

        private static string Write(Action<XmlWriter> body)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                IndentChars = "  "
            };

            using var stream = new MemoryStream();
            using (var writer = XmlWriter.Create(stream, settings))
            {
                writer.WriteStartDocument();
                body(writer);
                writer.WriteEndDocument();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        /// <summary>The rental period an invoice bills, derived when it was not recorded.</summary>
        public static (DateTime Start, DateTime End) BillingPeriod(CustomerInvoice invoice, Lease lease)
        {
            var start = (invoice.PeriodStart ?? invoice.InvoiceDate).Date;
            var end = invoice.PeriodEnd?.Date
                      ?? start.AddMonths(Math.Max(1, lease.BillingFrequencyMonths)).AddDays(-1);

            if (end > lease.EndDate.Date) end = lease.EndDate.Date;
            if (end < start) end = start;

            return (start, end);
        }

        /// <summary>Frequency of billing (BTAE-06) from the lease's months per invoice.</summary>
        public static string FrequencyCode(int months) => months switch
        {
            1 => "MTH",
            2 => "Q60",
            3 => "QTR",
            6 => "HYR",
            12 => "YRL",
            _ => "OTH"
        };

        /// <summary>Total rent over the whole term of the lease (BTAE-05).</summary>
        public static decimal ContractValue(Lease lease)
        {
            var months = (lease.EndDate.Year - lease.StartDate.Year) * 12 + lease.EndDate.Month - lease.StartDate.Month;
            if (lease.EndDate.Day >= lease.StartDate.Day - 1) months++;
            months = Math.Max(1, months);
            return VatDeterminationService.Round(lease.AnnualRent * months / 12m);
        }

        private static string Date(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static string Amount(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

        private static string Truncate(string value, int length) =>
            value.Length <= length ? value : value[..length];

        private static string? Trim(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>A file name the provider's portal and every operating system accept.</summary>
        public static string FileNameFor(string documentNumber) =>
            Regex.Replace(documentNumber, @"[^A-Za-z0-9._-]", "_") + ".xml";

        /// <summary>
        /// How a supply is described on the network. Decided by the seller's registration
        /// and the VAT category the invoice was issued under:
        ///
        ///   Seller not VAT registered     480 out-of-scope invoice, category O
        ///   Standard rated                380 tax invoice, category S at 5%
        ///   Zero rated                    380 tax invoice, category Z at 0%
        ///   Exempt (residential lease)    480, category E with reason DL8.46.2 - an
        ///                                 invoice carrying only exempt lines may not be a
        ///                                 380 (rule IBR-151-AE)
        /// </summary>
        private sealed record Treatment(string InvoiceTypeCode, string CategoryCode, decimal Rate, bool SellerVatRegistered, string Note)
        {
            public static Treatment For(Company company, string vatCategory, decimal vatRate)
            {
                if (!company.IsVatRegistered)
                    return new("480", "O", 0m, false, "Commercial invoice - the supplier is not registered for VAT.");

                return vatCategory switch
                {
                    VatCategories.Standard => new("380", "S", vatRate > 0m ? vatRate : VatDeterminationService.StandardRate, true, "Tax invoice"),
                    VatCategories.ZeroRated => new("380", "Z", 0m, true, "Tax invoice - zero-rated supply."),
                    _ => new("480", "E", 0m, true, "Exempt supply - lease of residential property.")
                };
            }
        }

        private sealed record LineData(string Name, string Description, decimal Net, decimal Vat, DateTime PeriodStart, DateTime PeriodEnd);
    }

    public sealed record EInvoiceDocumentRef(string Kind, int Id);

    public class EInvoiceResult
    {
        public bool Success { get; private init; }
        public string Message { get; private init; } = string.Empty;
        public List<string> Problems { get; private init; } = new();

        public static EInvoiceResult Succeeded(string message) => new() { Success = true, Message = message };

        public static EInvoiceResult Failed(string message, List<string>? problems = null) =>
            new() { Success = false, Message = message, Problems = problems ?? new List<string>() };
    }

    public class EInvoiceBatchResult
    {
        public int Generated { get; private set; }
        public List<EInvoiceResult> Failures { get; } = new();

        public void Add(EInvoiceResult result)
        {
            if (result.Success) Generated++;
            else Failures.Add(result);
        }

        public string Summary => Generated == 0 && Failures.Count == 0
            ? "Nothing was waiting to be generated."
            : $"{Generated} e-invoice(s) generated." +
              (Failures.Count > 0 ? $" {Failures.Count} could not be generated - see below." : string.Empty);
    }
}
