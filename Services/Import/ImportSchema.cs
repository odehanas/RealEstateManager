using System.Globalization;
using System.Net.Mail;
using ClosedXML.Excel;
using RealEstateManager.Domain;

namespace RealEstateManager.Services.Import
{
    /// <summary>The kinds of cell a template column holds; each is checked strictly on upload.</summary>
    public enum ImportType { Text, Integer, Decimal, Date, YesNo, Choice }

    /// <summary>Extra format rules for text columns, beyond their length.</summary>
    public enum ImportRule { None, Email, Trn, Tin, Iban, Digits9, Digits14, Country }

    /// <summary>One allowed value of a dropdown column: what is stored, and what the user reads.</summary>
    public sealed record ImportChoice(string Value, string Label);

    /// <summary>
    /// One column of a template sheet. The header is matched on upload in English or Arabic, so a
    /// file downloaded in one language can be filled and uploaded by someone working in the other.
    /// </summary>
    public sealed record ImportColumn(
        string Key,
        string Header,
        ImportType Type,
        bool Required = false,
        int MaxLength = 200,
        ImportChoice[]? Choices = null,
        decimal? Min = null,
        decimal? Max = null,
        ImportRule Rule = ImportRule.None,
        string? Note = null);

    /// <summary>One sheet of the template: a kind of master data, and who may import it.</summary>
    public sealed record ImportSheet(string Key, string Name, string Permission, string Purpose, ImportColumn[] Columns);

    /// <summary>
    /// The import template: one sheet per kind of master data. Rows refer to each other by the
    /// customer's own reference (the "ref" columns), so units find their property and contracts
    /// their unit and tenant inside the same file; an existing record can be named by its code.
    /// </summary>
    public static class ImportSchema
    {
        public const string Properties = "Properties";
        public const string Units = "Units";
        public const string Tenants = "Tenants";
        public const string Vendors = "Vendors";
        public const string Leases = "Leases";
        public const string Employees = "Employees";

        /// <summary>More rows than this in one sheet are refused: split the file.</summary>
        public const int MaxRowsPerSheet = 5000;

        private static ImportChoice[] Plain(params string[] values) => values.Select(v => new ImportChoice(v, v)).ToArray();

        private static readonly ImportChoice[] VatChoices =
        {
            new(VatCategories.Standard, "Standard rated (5%)"),
            new(VatCategories.Exempt, "Exempt"),
            new(VatCategories.ZeroRated, "Zero rated (0%)")
        };

        private static readonly ImportChoice[] PropertyEmirates = Plain(PropertyRegistrationLists.Emirates);
        private static readonly ImportChoice[] TenantEmirates = Emirates.All.Select(x => new ImportChoice(x.Key, x.Value)).ToArray();

        public static readonly ImportSheet[] Sheets =
        {
            new(Properties, "Properties", AppPermissions.MasterDataManage,
                "Buildings and plots. The system gives each one its property code.",
                new ImportColumn[]
                {
                    new("Ref", "Property ref", ImportType.Text, true, 50, Note: "Your own reference for the property, unique in this file. Units and employees use it to name the property."),
                    new("Name", "Name", ImportType.Text, true, 200),
                    new("PropertyType", "Property type", ImportType.Choice, true, Choices: Plain("Residential", "Commercial", "Warehouse", "MixedUse")),
                    new("OwnershipType", "Ownership", ImportType.Choice, true, Choices: Plain("Owned", "Managed"), Note: "Owned: the rent is the company's. Managed: the company manages it for a landlord."),
                    new("VatCategory", "VAT treatment", ImportType.Choice, Choices: VatChoices, Note: "Blank means standard rated."),
                    new("Address", "Address", ImportType.Text, MaxLength: 300),
                    new("Emirate", "Emirate", ImportType.Choice, Choices: PropertyEmirates),
                    new("AreaName", "Area / community", ImportType.Text, MaxLength: 150),
                    new("PlotNumber", "Plot number", ImportType.Text, MaxLength: 50),
                    new("TitleDeedNumber", "Title deed number", ImportType.Text, MaxLength: 50),
                    new("OwnerName", "Owner name", ImportType.Text, MaxLength: 200, Note: "Required for a managed property: the landlord."),
                    new("OwnerTrn", "Owner TRN", ImportType.Text, MaxLength: 15, Rule: ImportRule.Trn),
                    new("CommissionPercentage", "Commission %", ImportType.Decimal, Min: 0, Max: 100, Note: "A managed property's management commission, e.g. 5."),
                    new("AssetValue", "Asset value", ImportType.Decimal, Min: 0),
                    new("NumberOfFloors", "Number of floors", ImportType.Integer, Min: 0, Max: 300),
                    new("BuiltUpAreaSqFt", "Built-up area (sq ft)", ImportType.Decimal, Min: 0),
                    new("CompletionDate", "Completion date", ImportType.Date)
                }),

            new(Units, "Units", AppPermissions.MasterDataManage,
                "The units in each property. The system gives each one its unit code.",
                new ImportColumn[]
                {
                    new("Ref", "Unit ref", ImportType.Text, true, 50, Note: "Your own reference for the unit, unique in this file. Contracts use it to name the unit."),
                    new("PropertyRef", "Property ref", ImportType.Text, true, 50, Note: "The property's ref on the Properties sheet, or the code of a property already in the system."),
                    new("Floor", "Floor", ImportType.Text, MaxLength: 30, Note: "As written, e.g. G, 3, B1."),
                    new("FloorNumber", "Floor number", ImportType.Integer, Min: -10, Max: 300, Note: "For the building drawing: 0 = ground, -1 = first basement."),
                    new("UsageType", "Use", ImportType.Choice, Choices: Plain("Residential", "Commercial", "Warehouse", "Office")),
                    new("AreaSqFt", "Area (sq ft)", ImportType.Decimal, true, Min: 0),
                    new("StandardAnnualRent", "Asking annual rent", ImportType.Decimal, Min: 0),
                    new("Status", "Status", ImportType.Choice, Choices: Plain("Vacant", "Reserved", "UnderMaintenance"),
                        Note: "Blank means Vacant. A unit becomes Occupied when its contract is activated."),
                    new("VatCategory", "VAT treatment", ImportType.Choice, Choices: VatChoices, Note: "Blank means the property's treatment."),
                    new("ParkingBays", "Parking bays", ImportType.Integer, Min: 0, Max: 1000),
                    new("TitleDeedNumber", "Title deed number", ImportType.Text, MaxLength: 50),
                    new("UtilityAccountNumber", "Electricity / water account", ImportType.Text, MaxLength: 50)
                }),

            new(Tenants, "Tenants", AppPermissions.MasterDataManage,
                "Tenants and other customers. The system gives each one its tenant code.",
                new ImportColumn[]
                {
                    new("Ref", "Tenant ref", ImportType.Text, true, 50, Note: "Your own reference for the tenant, unique in this file. Contracts use it to name the tenant."),
                    new("FullName", "Full name", ImportType.Text, true, 200),
                    new("PartyType", "Party type", ImportType.Choice, true, Choices: Plain(PartyTypes.All)),
                    new("Email", "Email", ImportType.Text, MaxLength: 200, Rule: ImportRule.Email),
                    new("Phone", "Phone", ImportType.Text, MaxLength: 50),
                    new("Nationality", "Nationality", ImportType.Text, MaxLength: 100),
                    new("IdNumber", "Emirates ID", ImportType.Text, MaxLength: 50),
                    new("IdExpiryDate", "Emirates ID expiry", ImportType.Date),
                    new("PassportNumber", "Passport number", ImportType.Text, MaxLength: 50),
                    new("PassportExpiryDate", "Passport expiry", ImportType.Date),
                    new("TradeLicenseNumber", "Trade licence number", ImportType.Text, MaxLength: 50),
                    new("TrnNumber", "TRN", ImportType.Text, MaxLength: 15, Rule: ImportRule.Trn, Note: "15 digits, for a VAT registered tenant."),
                    new("Address", "Address", ImportType.Text, MaxLength: 300),
                    new("City", "City", ImportType.Text, MaxLength: 100),
                    new("Emirate", "Emirate", ImportType.Choice, Choices: TenantEmirates),
                    new("CountryCode", "Country code", ImportType.Text, MaxLength: 2, Rule: ImportRule.Country, Note: "Two letters, e.g. AE. Blank means AE."),
                    new("OpeningBalance", "Opening balance", ImportType.Decimal, Note: "What the tenant owed at the start, from your old system. Negative when in credit.")
                }),

            new(Vendors, "Vendors", AppPermissions.MasterDataManage,
                "Suppliers and contractors. The system gives each one its vendor code.",
                new ImportColumn[]
                {
                    new("Ref", "Vendor ref", ImportType.Text, true, 50, Note: "Your own reference for the vendor, unique in this file."),
                    new("Name", "Name", ImportType.Text, true, 200),
                    new("ServiceType", "Service type", ImportType.Text, MaxLength: 100),
                    new("ContactPerson", "Contact person", ImportType.Text, MaxLength: 150),
                    new("Email", "Email", ImportType.Text, MaxLength: 200, Rule: ImportRule.Email),
                    new("Phone", "Phone", ImportType.Text, MaxLength: 50),
                    new("IsVatRegistered", "VAT registered", ImportType.YesNo, Note: "Yes or No. A VAT registered vendor needs a TRN."),
                    new("TrnNumber", "TRN", ImportType.Text, MaxLength: 15, Rule: ImportRule.Trn),
                    new("TradeLicenseNumber", "Trade licence number", ImportType.Text, MaxLength: 50),
                    new("OpeningBalance", "Opening balance", ImportType.Decimal, Note: "What the company owed the vendor at the start, from your old system.")
                }),

            new(Leases, "Leases", AppPermissions.LeasesManage,
                "Running contracts. They are imported as drafts: review each one and activate it, which creates its invoices.",
                new ImportColumn[]
                {
                    new("UnitRef", "Unit ref", ImportType.Text, true, 50, Note: "The unit's ref on the Units sheet, or the code of a unit already in the system."),
                    new("TenantRef", "Tenant ref", ImportType.Text, true, 50, Note: "The tenant's ref on the Tenants sheet, or the code of a tenant already in the system."),
                    new("LegacyContractNumber", "Old contract number", ImportType.Text, MaxLength: 50, Note: "Kept in the contract's notes; the system gives the contract its own number."),
                    new("StartDate", "Start date", ImportType.Date, true),
                    new("EndDate", "End date", ImportType.Date, true),
                    new("AnnualRent", "Annual rent", ImportType.Decimal, true, Min: 0.01m),
                    new("BillingFrequencyMonths", "Billing every (months)", ImportType.Integer, true, Min: 1, Max: 12, Note: "1, 2, 3, 4, 6 or 12."),
                    new("SecurityDepositAmount", "Security deposit", ImportType.Decimal, Min: 0),
                    new("Notes", "Notes", ImportType.Text, MaxLength: 500)
                }),

            new(Employees, "Employees", AppPermissions.HrManage,
                "Staff for HR & Payroll. The system gives each one an employee code.",
                new ImportColumn[]
                {
                    new("Ref", "Employee ref", ImportType.Text, true, 50, Note: "Your own reference for the employee, unique in this file."),
                    new("FirstName", "First name", ImportType.Text, true, 100),
                    new("LastName", "Last name", ImportType.Text, true, 100),
                    new("NameArabic", "Name in Arabic", ImportType.Text, MaxLength: 200),
                    new("Gender", "Gender", ImportType.Choice, true, Choices: Plain("Male", "Female")),
                    new("DateOfBirth", "Date of birth", ImportType.Date),
                    new("Nationality", "Nationality", ImportType.Text, MaxLength: 100),
                    new("Email", "Email", ImportType.Text, MaxLength: 200, Rule: ImportRule.Email),
                    new("Mobile", "Mobile", ImportType.Text, MaxLength: 50),
                    new("Department", "Department", ImportType.Text, MaxLength: 100, Note: "By name. A department not in the system yet is created."),
                    new("JobTitle", "Job title", ImportType.Text, true, 150),
                    new("PropertyRef", "Property ref", ImportType.Text, MaxLength: 50, Note: "The building the employee works for, if any: its ref on the Properties sheet or its property code."),
                    new("JoiningDate", "Joining date", ImportType.Date, true),
                    new("ContractType", "Contract type", ImportType.Choice, Choices: Plain(EmployeeContractTypes.All), Note: "Blank means Unlimited."),
                    new("ContractEndDate", "Contract end date", ImportType.Date, Note: "Required for a limited contract."),
                    new("PassportNumber", "Passport number", ImportType.Text, MaxLength: 50),
                    new("PassportExpiry", "Passport expiry", ImportType.Date),
                    new("EmiratesIdNumber", "Emirates ID", ImportType.Text, MaxLength: 50),
                    new("EmiratesIdExpiry", "Emirates ID expiry", ImportType.Date),
                    new("ResidenceVisaNumber", "Residence visa number", ImportType.Text, MaxLength: 50),
                    new("ResidenceVisaExpiry", "Residence visa expiry", ImportType.Date),
                    new("LabourCardNumber", "Labour card number", ImportType.Text, MaxLength: 50),
                    new("LabourCardExpiry", "Labour card expiry", ImportType.Date),
                    new("MohrePersonCode", "MOHRE person code", ImportType.Text, MaxLength: 14, Rule: ImportRule.Digits14),
                    new("BasicSalary", "Basic salary", ImportType.Decimal, true, Min: 0.01m),
                    new("HousingAllowance", "Housing allowance", ImportType.Decimal, Min: 0),
                    new("TransportAllowance", "Transport allowance", ImportType.Decimal, Min: 0),
                    new("OtherAllowance", "Other allowance", ImportType.Decimal, Min: 0),
                    new("PaymentMethod", "Salary paid by", ImportType.Choice, Choices: new ImportChoice[]
                    {
                        new(SalaryPaymentMethods.Wps, "WPS"), new(SalaryPaymentMethods.BankTransfer, "Bank transfer"), new(SalaryPaymentMethods.Cheque, "Cheque")
                    }, Note: "Blank means WPS. WPS and bank transfer need the IBAN."),
                    new("BankName", "Bank name", ImportType.Text, MaxLength: 100),
                    new("Iban", "IBAN", ImportType.Text, MaxLength: 34, Rule: ImportRule.Iban),
                    new("BankRoutingCode", "Bank routing code", ImportType.Text, MaxLength: 9, Rule: ImportRule.Digits9),
                    new("AnnualLeaveDaysPerYear", "Annual leave days", ImportType.Decimal, Min: 0, Max: 60, Note: "Blank means 30.")
                })
        };

        public static ImportSheet Sheet(string key) => Sheets.First(x => x.Key == key);
    }

    /// <summary>A cell's problem, written in English with {0}-style values and translated where it is shown.</summary>
    public sealed record ImportIssue(string Sheet, int Row, string? Column, string Message, object?[] Values);

    /// <summary>
    /// Reads one cell as the type its column declares. A wrong type is reported, never guessed:
    /// "12 Jan" in a date column, "AED 5,000" in a number column or "Maybe" in a Yes/No column
    /// each stop the import with the sheet, row and column named.
    /// </summary>
    public static class ImportCells
    {
        private static readonly string[] DateFormats =
        {
            "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd", "dd.MM.yyyy", "d MMM yyyy", "dd MMM yyyy"
        };

        private static readonly string[] Yes = { "yes", "y", "true", "1", "نعم" };
        private static readonly string[] No = { "no", "n", "false", "0", "لا" };

        /// <summary>The cell's value as the column's type, or an error message (English, with values).</summary>
        public static (object? Value, string? Error, object?[] Args) Read(IXLCell cell, ImportColumn column, Func<string, string> arabic)
        {
            var value = cell.Value;
            if (value.IsBlank || (value.IsText && string.IsNullOrWhiteSpace(value.GetText())))
                return column.Required ? (null, "{0} is required.", new object?[] { column.Header }) : (null, null, Array.Empty<object?>());

            if (value.IsError)
                return (null, "The cell shows an Excel error ({0}).", new object?[] { cell.GetFormattedString() });

            switch (column.Type)
            {
                case ImportType.Text:
                {
                    // An ID or phone typed as a number arrives as a number: written back without ".0".
                    var text = value.IsNumber
                        ? value.GetNumber().ToString("0.##########", CultureInfo.InvariantCulture)
                        : value.IsDateTime ? value.GetDateTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : cell.GetString().Trim();
                    if (text.Length > column.MaxLength)
                        return (null, "{0} is too long: at most {1} characters.", new object?[] { column.Header, column.MaxLength });
                    var ruleError = CheckRule(column.Rule, ref text);
                    return ruleError != null ? (null, ruleError, new object?[] { column.Header }) : (text, null, Array.Empty<object?>());
                }

                case ImportType.Integer:
                case ImportType.Decimal:
                {
                    decimal number;
                    if (value.IsNumber)
                        number = (decimal)value.GetNumber();
                    else if (value.IsText && TryNumber(value.GetText(), out var parsed))
                        number = parsed;
                    else
                        return (null, column.Type == ImportType.Integer ? "{0} must be a whole number." : "{0} must be a number, without currency or units.",
                            new object?[] { column.Header });

                    if (column.Type == ImportType.Integer && number != Math.Truncate(number))
                        return (null, "{0} must be a whole number.", new object?[] { column.Header });
                    if (column.Type == ImportType.Decimal && Math.Round(number, 2) != number)
                        return (null, "{0} can have at most two decimals.", new object?[] { column.Header });
                    if (column.Min.HasValue && number < column.Min.Value)
                        return (null, "{0} cannot be less than {1}.", new object?[] { column.Header, column.Min.Value.ToString("0.##", CultureInfo.InvariantCulture) });
                    if (column.Max.HasValue && number > column.Max.Value)
                        return (null, "{0} cannot be more than {1}.", new object?[] { column.Header, column.Max.Value.ToString("0.##", CultureInfo.InvariantCulture) });

                    return column.Type == ImportType.Integer ? ((int)number, null, Array.Empty<object?>()) : (number, null, Array.Empty<object?>());
                }

                case ImportType.Date:
                {
                    DateTime date;
                    if (value.IsDateTime)
                        date = value.GetDateTime();
                    else if (value.IsNumber && value.GetNumber() is > 1 and < 80000)
                        date = DateTime.FromOADate(value.GetNumber());
                    else if (value.IsText && DateTime.TryParseExact(value.GetText().Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                        date = parsed;
                    else
                        return (null, "{0} must be a date, e.g. 31/12/2026.", new object?[] { column.Header });

                    if (date.Year < 1900 || date.Year > 2100)
                        return (null, "{0} is not a believable date.", new object?[] { column.Header });
                    return (date.Date, null, Array.Empty<object?>());
                }

                case ImportType.YesNo:
                {
                    if (value.IsBoolean)
                        return (value.GetBoolean(), null, Array.Empty<object?>());
                    var text = value.IsNumber ? value.GetNumber().ToString(CultureInfo.InvariantCulture) : cell.GetString().Trim().ToLowerInvariant();
                    if (Yes.Contains(text)) return (true, null, Array.Empty<object?>());
                    if (No.Contains(text)) return (false, null, Array.Empty<object?>());
                    return (null, "{0} must be Yes or No.", new object?[] { column.Header });
                }

                case ImportType.Choice:
                {
                    var text = cell.GetString().Trim();
                    var match = column.Choices!.FirstOrDefault(c =>
                        string.Equals(c.Value, text, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(c.Label, text, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(arabic(c.Label), text, StringComparison.Ordinal) ||
                        string.Equals(arabic(c.Value), text, StringComparison.Ordinal));
                    return match != null
                        ? (match.Value, null, Array.Empty<object?>())
                        : (null, "{0} must be one of: {1}.", new object?[] { column.Header, string.Join(", ", column.Choices!.Select(c => c.Label)) });
                }
            }

            return (null, "Unknown column type.", Array.Empty<object?>());
        }

        private static bool TryNumber(string text, out decimal number)
        {
            var cleaned = text.Trim().Replace(",", string.Empty).Replace(" ", string.Empty);
            return decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);
        }

        private static string? CheckRule(ImportRule rule, ref string text)
        {
            switch (rule)
            {
                case ImportRule.Email:
                    return MailAddress.TryCreate(text, out var address) && address.Address == text ? null : "{0} is not a valid email address.";
                case ImportRule.Trn:
                    text = text.Replace(" ", string.Empty).Replace("-", string.Empty);
                    return text.Length == 15 && text.All(char.IsDigit) ? null : "{0} must be 15 digits.";
                case ImportRule.Tin:
                    return text.Length == 10 && text.All(char.IsDigit) ? null : "{0} must be 10 digits.";
                case ImportRule.Iban:
                    text = text.Replace(" ", string.Empty).ToUpperInvariant();
                    return text.Length == 23 && text.StartsWith("AE", StringComparison.Ordinal) ? null : "{0}: a UAE IBAN has 23 characters and starts with AE.";
                case ImportRule.Digits9:
                    return text.Length == 9 && text.All(char.IsDigit) ? null : "{0} must be 9 digits.";
                case ImportRule.Digits14:
                    return text.Length == 14 && text.All(char.IsDigit) ? null : "{0} must be 14 digits.";
                case ImportRule.Country:
                    text = text.ToUpperInvariant();
                    return text.Length == 2 && text.All(c => c is >= 'A' and <= 'Z') ? null : "{0} must be a two-letter country code, e.g. AE.";
                default:
                    return null;
            }
        }
    }
}
