using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services.Localization;

namespace RealEstateManager.Services.Import
{
    /// <summary>One filled row of a sheet: its row number in the file and its values by column key.</summary>
    public sealed class ImportRow
    {
        public int RowNumber { get; init; }
        public Dictionary<string, object?> Values { get; } = new();

        public string? Text(string key) => Values.GetValueOrDefault(key) as string;
        public decimal? Number(string key) => Values.GetValueOrDefault(key) as decimal?;
        public int? Integer(string key) => Values.GetValueOrDefault(key) as int?;
        public DateTime? Date(string key) => Values.GetValueOrDefault(key) as DateTime?;
        public bool? YesNo(string key) => Values.GetValueOrDefault(key) as bool?;
    }

    /// <summary>A read and checked file: the rows of each sheet, and everything wrong with them.</summary>
    public sealed class ImportBatch
    {
        public Dictionary<string, List<ImportRow>> Rows { get; } = ImportSchema.Sheets.ToDictionary(s => s.Key, _ => new List<ImportRow>());
        public List<ImportIssue> Issues { get; } = new();
        public List<string> NewDepartments { get; } = new();

        public bool IsValid => Issues.Count == 0;
        public int Count(string sheet) => Rows[sheet].Count;
        public int Total => Rows.Values.Sum(r => r.Count);

        public void Add(string sheet, int row, string? column, string message, params object?[] values) =>
            Issues.Add(new ImportIssue(sheet, row, column, message, values));
    }

    /// <summary>
    /// Importing master data from a customer's old system through an Excel template.
    ///
    /// 1. <see cref="BuildTemplate"/>: a workbook with one sheet per kind of record, its headers,
    ///    dropdown lists, date and number cells, and an instructions sheet, in the user's language.
    /// 2. <see cref="ReadAsync"/> and <see cref="CheckAsync"/>: every cell is read as its column's
    ///    type and every row checked against the same rules as the entry screens, the records
    ///    already in the system and the other rows of the file.
    /// 3. <see cref="ImportAsync"/>: only a file with no problems at all is imported, and all of it
    ///    or nothing, in one transaction. Codes are generated as on the screens; contracts arrive
    ///    as drafts, so nothing is invoiced or posted until someone activates them.
    /// </summary>
    public class MasterDataImportService
    {
        private readonly ApplicationDbContext _db;
        private readonly UiText _text;
        private readonly CodeGenerationService _codes;
        private readonly MasterDataCreationService _creation;
        private readonly MasterDataUniquenessService _uniqueness;
        private readonly LeaseCreationService _leases;
        private readonly LeaseWorkflowService _leaseWorkflow;
        private readonly HrService _hr;

        public MasterDataImportService(
            ApplicationDbContext db, UiText text, CodeGenerationService codes, MasterDataCreationService creation,
            MasterDataUniquenessService uniqueness, LeaseCreationService leases, LeaseWorkflowService leaseWorkflow, HrService hr)
        {
            _db = db;
            _text = text;
            _codes = codes;
            _creation = creation;
            _uniqueness = uniqueness;
            _leases = leases;
            _leaseWorkflow = leaseWorkflow;
            _hr = hr;
        }

        // =====================================================================
        //  THE TEMPLATE
        // =====================================================================

        public byte[] BuildTemplate(IReadOnlyCollection<ImportSheet> sheets)
        {
            var arabic = UiLanguages.IsArabic;
            using var workbook = new XLWorkbook();
            workbook.RightToLeft = arabic;

            var instructions = workbook.Worksheets.Add(_text["Instructions"]);
            var lists = workbook.Worksheets.Add("Lists");
            var listColumn = 0;

            foreach (var sheet in sheets)
            {
                var ws = workbook.Worksheets.Add(_text[sheet.Name]);
                ws.RightToLeft = arabic;

                for (var c = 0; c < sheet.Columns.Length; c++)
                {
                    var column = sheet.Columns[c];
                    var cell = ws.Cell(1, c + 1);
                    cell.Value = _text[column.Header] + (column.Required ? " *" : string.Empty);
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Fill.BackgroundColor = column.Required ? XLColor.FromHtml("#0f2740") : XLColor.FromHtml("#5b6774");
                    cell.Style.Alignment.WrapText = true;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                    cell.GetComment().AddText(Describe(column));
                    ws.Column(c + 1).Width = column.Type is ImportType.Text && column.MaxLength > 100 ? 32 : 20;

                    var range = ws.Range(2, c + 1, ImportSchema.MaxRowsPerSheet + 1, c + 1);
                    var validation = range.CreateDataValidation();
                    validation.IgnoreBlanks = true;
                    validation.ShowErrorMessage = true;
                    validation.ErrorStyle = XLErrorStyle.Stop;
                    validation.ErrorTitle = _text[column.Header];
                    validation.ErrorMessage = Describe(column);

                    switch (column.Type)
                    {
                        case ImportType.Choice:
                        case ImportType.YesNo:
                            var labels = column.Type == ImportType.YesNo
                                ? new[] { _text["Yes"], _text["No"] }
                                : column.Choices!.Select(x => _text[x.Label]).ToArray();
                            listColumn++;
                            lists.Cell(1, listColumn).Value = _text[column.Header];
                            for (var i = 0; i < labels.Length; i++)
                                lists.Cell(i + 2, listColumn).Value = labels[i];
                            validation.List(lists.Range(2, listColumn, labels.Length + 1, listColumn), true);
                            break;
                        case ImportType.Integer:
                            validation.WholeNumber.Between((int)(column.Min ?? int.MinValue / 2), (int)(column.Max ?? int.MaxValue / 2));
                            ws.Column(c + 1).Style.NumberFormat.Format = "0";
                            break;
                        case ImportType.Decimal:
                            validation.Decimal.Between((double)(column.Min ?? -999_999_999_999m), (double)(column.Max ?? 999_999_999_999m));
                            ws.Column(c + 1).Style.NumberFormat.Format = "#,##0.00";
                            break;
                        case ImportType.Date:
                            validation.Date.Between(new DateTime(1900, 1, 1), new DateTime(2100, 12, 31));
                            ws.Column(c + 1).Style.NumberFormat.Format = "dd/mm/yyyy";
                            break;
                        default:
                            validation.TextLength.EqualOrLessThan(column.MaxLength);
                            // Text, so IDs and phone numbers keep their leading zeros.
                            ws.Column(c + 1).Style.NumberFormat.Format = "@";
                            break;
                    }
                }

                // The headers stay text whatever their column holds.
                ws.Row(1).Style.NumberFormat.Format = "@";
                ws.Row(1).Height = 32;
                ws.SheetView.FreezeRows(1);
            }

            lists.Hide();
            WriteInstructions(instructions, sheets, arabic);
            instructions.SetTabActive();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private string Describe(ImportColumn column)
        {
            var type = column.Type switch
            {
                ImportType.Integer => _text["A whole number."],
                ImportType.Decimal => _text["A number, without currency or units."],
                ImportType.Date => _text["A date, e.g. 31/12/2026."],
                ImportType.YesNo => _text["Yes or No."],
                ImportType.Choice => _text.Format("One of: {0}.", string.Join(", ", column.Choices!.Select(x => _text[x.Label]))),
                _ => _text.Format("Text, up to {0} characters.", column.MaxLength)
            };
            var required = column.Required ? _text["Required."] + " " : string.Empty;
            return required + type + (column.Note == null ? string.Empty : " " + _text[column.Note]);
        }

        private void WriteInstructions(IXLWorksheet ws, IReadOnlyCollection<ImportSheet> sheets, bool arabic)
        {
            ws.RightToLeft = arabic;
            ws.Column(1).Width = 26;
            ws.Column(2).Width = 100;
            var row = 1;

            void Line(string? label, string text, bool bold = false)
            {
                if (label != null) ws.Cell(row, 1).Value = label;
                ws.Cell(row, 2).Value = text;
                ws.Cell(row, 2).Style.Alignment.WrapText = true;
                if (bold) ws.Row(row).Style.Font.Bold = true;
                row++;
            }

            ws.Cell(row, 1).Value = _text["RealEstateManager - data import template"];
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 15;
            row += 2;

            Line(_text["How to use it"], _text["Fill one row per record on each sheet, starting on row 2. Leave a sheet empty if you have nothing for it."], true);
            Line("1", _text["Do not rename the sheets or change the headers. Headers with * are required."]);
            Line("2", _text["Choose from the dropdown lists; type dates as dates (31/12/2026) and numbers without currency or units."]);
            Line("3", _text["Give every record your own reference in its ref column, unique on that sheet. Other sheets use it: a unit names its property ref, a contract its unit ref and tenant ref. A record already in the system can be named by its code instead."]);
            Line("4", _text["Upload the file in Master Data > Import Data. It is checked first: nothing is imported until every row is right, and then everything is imported together."]);
            Line("5", _text["The system gives every record its own code. After the import, download the result file: it is your file with each record's new code beside it."]);
            Line("6", _text["Contracts arrive as drafts. Review each one and activate it to create its invoices."]);
            row++;

            Line(_text["Sheet"], _text["What it holds"], true);
            foreach (var sheet in sheets)
                Line(_text[sheet.Name], _text[sheet.Purpose]);
            row++;
            Line(null, _text["Point at a header to see what the column takes."]);
        }

        // =====================================================================
        //  READING THE FILE
        // =====================================================================

        /// <summary>Reads every sheet the user may import; cells of the wrong type become issues.</summary>
        public ImportBatch Read(Stream file, IReadOnlyCollection<ImportSheet> allowed)
        {
            var batch = new ImportBatch();

            XLWorkbook workbook;
            try
            {
                workbook = new XLWorkbook(file);
            }
            catch (Exception)
            {
                batch.Add(string.Empty, 0, null, "The file could not be read as an Excel workbook (.xlsx). Download the template and fill that.");
                return batch;
            }

            using (workbook)
            {
                foreach (var sheet in ImportSchema.Sheets)
                {
                    var ws = workbook.Worksheets.FirstOrDefault(w => Same(w.Name, sheet.Name) || Same(w.Name, _text.ArabicFor(sheet.Name)));
                    if (ws == null)
                        continue;

                    var used = ws.RangeUsed();
                    if (used == null || used.LastRow().RowNumber() < 2)
                        continue;

                    if (!allowed.Contains(sheet))
                    {
                        batch.Add(sheet.Key, 0, null, "You do not have permission to import {0}. Remove the sheet's rows, or ask someone who has.", sheet.Name);
                        continue;
                    }

                    ReadSheet(ws, sheet, batch);
                }
            }

            if (batch.Total == 0 && batch.IsValid)
                batch.Add(string.Empty, 0, null, "The file has no rows to import.");

            return batch;
        }

        private void ReadSheet(IXLWorksheet ws, ImportSheet sheet, ImportBatch batch)
        {
            // The headers, in either language, wherever the columns are.
            var positions = new Dictionary<string, int>();
            var lastColumn = ws.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0;
            for (var c = 1; c <= lastColumn; c++)
            {
                var header = ws.Cell(1, c).GetString();
                if (string.IsNullOrWhiteSpace(header))
                    continue;
                var column = sheet.Columns.FirstOrDefault(x => Same(header, x.Header) || Same(header, _text.ArabicFor(x.Header)));
                if (column == null)
                {
                    // The result file adds one; anything else is a header that was changed.
                    if (!Same(header, "System code") && !Same(header, _text.ArabicFor("System code")))
                        batch.Add(sheet.Key, 1, null, "The column \"{0}\" is not one of this sheet's columns. Do not change the headers.", header.Trim());
                }
                else
                {
                    positions[column.Key] = c;
                }
            }

            foreach (var column in sheet.Columns.Where(x => x.Required && !positions.ContainsKey(x.Key)))
                batch.Add(sheet.Key, 1, column.Key, "The required column \"{0}\" is missing.", column.Header);
            if (sheet.Columns.Any(x => x.Required && !positions.ContainsKey(x.Key)))
                return;

            var lastRow = ws.LastRowUsed()!.RowNumber();
            if (lastRow - 1 > ImportSchema.MaxRowsPerSheet)
            {
                batch.Add(sheet.Key, 0, null, "{0} has more than {1} rows. Split it into several files.", sheet.Name, ImportSchema.MaxRowsPerSheet);
                return;
            }

            for (var r = 2; r <= lastRow; r++)
            {
                var cells = positions.Select(p => ws.Cell(r, p.Value)).ToList();
                if (cells.All(x => x.Value.IsBlank || (x.Value.IsText && string.IsNullOrWhiteSpace(x.Value.GetText()))))
                    continue;

                var row = new ImportRow { RowNumber = r };
                foreach (var column in sheet.Columns)
                {
                    if (!positions.TryGetValue(column.Key, out var c))
                        continue;
                    var (value, error, args) = ImportCells.Read(ws.Cell(r, c), column, _text.ArabicFor);
                    if (error != null)
                        batch.Add(sheet.Key, r, column.Key, error, args);
                    else
                        row.Values[column.Key] = value;
                }
                batch.Rows[sheet.Key].Add(row);
            }
        }

        private static bool Same(string? a, string? b) =>
            string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);

        private static string Clean(string? text) => (text ?? string.Empty).Replace("*", string.Empty).Trim();

        // =====================================================================
        //  CHECKING THE ROWS
        // =====================================================================

        /// <summary>The rules of the entry screens, the records already held, and the other rows of the file.</summary>
        public async Task CheckAsync(ImportBatch batch, int companyId)
        {
            // Rows with a cell of the wrong type have been reported; their other checks would only repeat it.
            var badRows = batch.Issues.Where(i => i.Row > 1).Select(i => (i.Sheet, i.Row)).ToHashSet();
            bool Ok(string sheet, ImportRow row) => !badRows.Contains((sheet, row.RowNumber));

            CheckUniqueRefs(batch, ImportSchema.Properties);
            CheckUniqueRefs(batch, ImportSchema.Units);
            CheckUniqueRefs(batch, ImportSchema.Tenants);
            CheckUniqueRefs(batch, ImportSchema.Vendors);
            CheckUniqueRefs(batch, ImportSchema.Employees);

            var fileProperties = RefSet(batch, ImportSchema.Properties);
            var fileUnits = RefSet(batch, ImportSchema.Units);
            var fileTenants = RefSet(batch, ImportSchema.Tenants);

            // ---- Properties ----------------------------------------------------------------
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in batch.Rows[ImportSchema.Properties].Where(r => Ok(ImportSchema.Properties, r)))
            {
                var property = ToProperty(row);
                if (property.OwnershipType == "Managed" && string.IsNullOrWhiteSpace(property.OwnerName))
                    batch.Add(ImportSchema.Properties, row.RowNumber, "OwnerName", "A managed property needs the owner's name.");
                if (!names.Add(property.Name))
                    batch.Add(ImportSchema.Properties, row.RowNumber, "Name", "Another row of this file has the same property name.");
                if (property.TitleDeedNumber != null && !deeds.Add($"{property.Emirate}|{property.TitleDeedNumber}"))
                    batch.Add(ImportSchema.Properties, row.RowNumber, "TitleDeedNumber", "Another row of this file has the same title deed.");

                var state = new ModelStateDictionary();
                await _uniqueness.ValidatePropertyAsync(state, companyId, property);
                if (property.TitleDeedNumber != null && await _db.Properties.AnyAsync(x =>
                        x.CompanyId == companyId && x.TitleDeedNumber == property.TitleDeedNumber && x.Emirate == property.Emirate))
                    state.AddModelError("TitleDeedNumber", $"Title deed {property.TitleDeedNumber} is already recorded on another property.");
                AddState(batch, ImportSchema.Properties, row, state);
            }

            // ---- Units ---------------------------------------------------------------------
            var existingPropertyCodes = await _db.Properties.AsNoTracking().Where(x => x.CompanyId == companyId)
                .Select(x => x.Code).ToListAsync();
            var propertyCodes = existingPropertyCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var row in batch.Rows[ImportSchema.Units].Where(r => Ok(ImportSchema.Units, r)))
            {
                var reference = row.Text("PropertyRef")!;
                if (!fileProperties.Contains(reference) && !propertyCodes.Contains(reference))
                    batch.Add(ImportSchema.Units, row.RowNumber, "PropertyRef", "No property \"{0}\" on the Properties sheet or in the system.", reference);
            }

            // ---- Tenants -------------------------------------------------------------------
            var tenantKeys = new Dictionary<string, HashSet<string>>
            {
                ["IdNumber"] = new(StringComparer.OrdinalIgnoreCase), ["PassportNumber"] = new(StringComparer.OrdinalIgnoreCase),
                ["TrnNumber"] = new(StringComparer.OrdinalIgnoreCase), ["TradeLicenseNumber"] = new(StringComparer.OrdinalIgnoreCase)
            };
            foreach (var row in batch.Rows[ImportSchema.Tenants].Where(r => Ok(ImportSchema.Tenants, r)))
            {
                foreach (var key in tenantKeys.Keys)
                    if (row.Text(key) is { } value && !tenantKeys[key].Add(value))
                        batch.Add(ImportSchema.Tenants, row.RowNumber, key, "Another row of this file has the same {0}.", ImportSchema.Sheet(ImportSchema.Tenants).Columns.First(c => c.Key == key).Header);

                var tenant = ToTenant(row);
                var state = new ModelStateDictionary();
                await _creation.ValidateTenantAsync(companyId, tenant, state);
                AddState(batch, ImportSchema.Tenants, row, state);
            }

            // ---- Vendors -------------------------------------------------------------------
            var vendorTrns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var vendorLicences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in batch.Rows[ImportSchema.Vendors].Where(r => Ok(ImportSchema.Vendors, r)))
            {
                if (row.Text("TrnNumber") is { } trn && !vendorTrns.Add(trn))
                    batch.Add(ImportSchema.Vendors, row.RowNumber, "TrnNumber", "Another row of this file has the same {0}.", "TRN");
                if (row.Text("TradeLicenseNumber") is { } licence && !vendorLicences.Add(licence))
                    batch.Add(ImportSchema.Vendors, row.RowNumber, "TradeLicenseNumber", "Another row of this file has the same {0}.", "Trade licence number");

                var vendor = ToVendor(row);
                var state = new ModelStateDictionary();
                await _creation.ValidateVendorAsync(companyId, vendor, state);
                AddState(batch, ImportSchema.Vendors, row, state);
            }

            // ---- Leases --------------------------------------------------------------------
            var units = await _db.Units.AsNoTracking().Where(x => x.Property!.CompanyId == companyId)
                .Select(x => new { x.Id, x.UnitNumber }).ToListAsync();
            var unitIds = units.GroupBy(x => x.UnitNumber, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
            var tenantCodes = (await _db.Tenants.AsNoTracking().Where(x => x.CompanyId == companyId).Select(x => x.Code).ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var periods = new Dictionary<string, List<(DateTime Start, DateTime End, int Row)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in batch.Rows[ImportSchema.Leases].Where(r => Ok(ImportSchema.Leases, r)))
            {
                var unitRef = row.Text("UnitRef")!;
                var tenantRef = row.Text("TenantRef")!;
                var start = row.Date("StartDate")!.Value;
                var end = row.Date("EndDate")!.Value;
                var frequency = row.Integer("BillingFrequencyMonths")!.Value;

                var unitInFile = fileUnits.Contains(unitRef);
                if (!unitInFile && !unitIds.ContainsKey(unitRef))
                    batch.Add(ImportSchema.Leases, row.RowNumber, "UnitRef", "No unit \"{0}\" on the Units sheet or in the system.", unitRef);
                if (!fileTenants.Contains(tenantRef) && !tenantCodes.Contains(tenantRef))
                    batch.Add(ImportSchema.Leases, row.RowNumber, "TenantRef", "No tenant \"{0}\" on the Tenants sheet or in the system.", tenantRef);
                if (start >= end)
                    batch.Add(ImportSchema.Leases, row.RowNumber, "EndDate", "End date must be later than start date.");
                if (12 % frequency != 0)
                    batch.Add(ImportSchema.Leases, row.RowNumber, "BillingFrequencyMonths", "Billing frequency must be 1, 2, 3, 4, 6, or 12.");

                if (start < end)
                {
                    // Two contracts of the file for the same unit and dates, or one clashing with a running contract.
                    if (!periods.TryGetValue(unitRef, out var taken))
                        periods[unitRef] = taken = new();
                    var overlap = taken.FirstOrDefault(p => p.Start <= end && p.End >= start);
                    if (overlap != default)
                        batch.Add(ImportSchema.Leases, row.RowNumber, "UnitRef", "Row {0} of this sheet is for the same unit over some of the same dates.", overlap.Row);
                    taken.Add((start, end, row.RowNumber));

                    if (!unitInFile && unitIds.TryGetValue(unitRef, out var unitId) &&
                        await _leaseWorkflow.CheckUnitFreeAsync(0, unitId, start, end) is { } clash)
                        batch.Add(ImportSchema.Leases, row.RowNumber, "UnitRef", clash);
                }
            }

            // ---- Employees -----------------------------------------------------------------
            var departments = (await _db.Departments.AsNoTracking().Where(x => x.CompanyId == companyId).Select(x => x.Name).ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var employeeIds = (await _db.Employees.AsNoTracking().Where(x => x.CompanyId == companyId && x.EmiratesIdNumber != null)
                .Select(x => x.EmiratesIdNumber!).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var fileEmployeeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in batch.Rows[ImportSchema.Employees].Where(r => Ok(ImportSchema.Employees, r)))
            {
                var employee = ToEmployee(row);
                if (row.Text("PropertyRef") is { } propertyRef && !fileProperties.Contains(propertyRef) && !propertyCodes.Contains(propertyRef))
                    batch.Add(ImportSchema.Employees, row.RowNumber, "PropertyRef", "No property \"{0}\" on the Properties sheet or in the system.", propertyRef);
                if (employee.ContractType == EmployeeContractTypes.Limited && !employee.ContractEndDate.HasValue)
                    batch.Add(ImportSchema.Employees, row.RowNumber, "ContractEndDate", "A limited contract needs its end date.");
                if (employee.ContractEndDate.HasValue && employee.ContractEndDate.Value < employee.JoiningDate)
                    batch.Add(ImportSchema.Employees, row.RowNumber, "ContractEndDate", "The contract cannot end before the joining date.");
                if (employee.PaymentMethod is SalaryPaymentMethods.Wps or SalaryPaymentMethods.BankTransfer && string.IsNullOrWhiteSpace(employee.Iban))
                    batch.Add(ImportSchema.Employees, row.RowNumber, "Iban", "Salaries paid to a bank need the employee's IBAN.");
                if (employee.EmiratesIdNumber is { } eid)
                {
                    if (employeeIds.Contains(eid))
                        batch.Add(ImportSchema.Employees, row.RowNumber, "EmiratesIdNumber", "An employee with Emirates ID {0} is already in the system.", eid);
                    else if (!fileEmployeeIds.Add(eid))
                        batch.Add(ImportSchema.Employees, row.RowNumber, "EmiratesIdNumber", "Another row of this file has the same {0}.", "Emirates ID");
                }
                if (row.Text("Department") is { } department && !departments.Contains(department) && !batch.NewDepartments.Contains(department, StringComparer.OrdinalIgnoreCase))
                    batch.NewDepartments.Add(department);
            }
        }

        private static void CheckUniqueRefs(ImportBatch batch, string sheet)
        {
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in batch.Rows[sheet])
            {
                if (row.Text("Ref") is not { } reference)
                    continue;
                if (seen.TryGetValue(reference, out var first))
                    batch.Add(sheet, row.RowNumber, "Ref", "The ref \"{0}\" is already used on row {1}. Each row needs its own.", reference, first);
                else
                    seen[reference] = row.RowNumber;
            }
        }

        private static HashSet<string> RefSet(ImportBatch batch, string sheet) =>
            batch.Rows[sheet].Select(r => r.Text("Ref")).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static void AddState(ImportBatch batch, string sheet, ImportRow row, ModelStateDictionary state)
        {
            foreach (var (field, entry) in state)
                foreach (var error in entry.Errors)
                    batch.Add(sheet, row.RowNumber, string.IsNullOrEmpty(field) ? null : field, error.ErrorMessage);
        }

        // =====================================================================
        //  IMPORTING
        // =====================================================================

        /// <summary>The codes the system gave, by sheet and row, for the result file.</summary>
        public sealed class ImportResult
        {
            public Dictionary<string, Dictionary<int, string>> Codes { get; } = ImportSchema.Sheets.ToDictionary(s => s.Key, _ => new Dictionary<int, string>());
            public int Count(string sheet) => Codes[sheet].Count;
        }

        /// <summary>Imports a checked file, all of it or nothing.</summary>
        public async Task<ImportResult> ImportAsync(ImportBatch batch, int companyId, int? userId)
        {
            if (!batch.IsValid)
                throw new InvalidOperationException("The file has problems; nothing was imported.");

            var result = new ImportResult();
            await using var transaction = await _db.Database.BeginTransactionAsync();

            var propertyIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in batch.Rows[ImportSchema.Properties])
            {
                var property = ToProperty(row);
                property.CopyRegistrationFrom(property);
                property.Code = await _codes.GeneratePropertyCodeAsync(property.OwnershipType, property.PropertyType);
                property.CompanyId = companyId;
                property.IsActive = true;
                _db.Properties.Add(property);
                await _db.SaveChangesAsync();
                propertyIds[row.Text("Ref")!] = property.Id;
                result.Codes[ImportSchema.Properties][row.RowNumber] = property.Code;
            }

            var existingProperties = await _db.Properties.AsNoTracking().Where(x => x.CompanyId == companyId)
                .Select(x => new { x.Id, x.Code }).ToListAsync();
            int PropertyId(string reference) => propertyIds.TryGetValue(reference, out var id)
                ? id
                : existingProperties.First(x => string.Equals(x.Code, reference, StringComparison.OrdinalIgnoreCase)).Id;

            var unitIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in batch.Rows[ImportSchema.Units])
            {
                var propertyId = PropertyId(row.Text("PropertyRef")!);
                var unit = new Unit
                {
                    PropertyId = propertyId,
                    UnitNumber = await _codes.GenerateUnitCodeAsync(propertyId),
                    Floor = row.Text("Floor"),
                    FloorNumber = row.Integer("FloorNumber"),
                    UsageType = row.Text("UsageType"),
                    AreaSqFt = row.Number("AreaSqFt") ?? 0m,
                    StandardAnnualRent = row.Number("StandardAnnualRent") ?? 0m,
                    Status = row.Text("Status") ?? "Vacant",
                    VatCategory = row.Text("VatCategory"),
                    ParkingBays = row.Integer("ParkingBays"),
                    TitleDeedNumber = row.Text("TitleDeedNumber"),
                    UtilityAccountNumber = row.Text("UtilityAccountNumber"),
                    IsActive = true
                };
                _db.Units.Add(unit);
                await _db.SaveChangesAsync();
                unitIds[row.Text("Ref")!] = unit.Id;
                result.Codes[ImportSchema.Units][row.RowNumber] = unit.UnitNumber;
            }

            var tenantIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in batch.Rows[ImportSchema.Tenants])
            {
                var tenant = ToTenant(row);
                MasterDataCreationService.NormaliseTenantEInvoicingFields(tenant, new ModelStateDictionary());
                tenant = await _creation.CreateTenantAsync(companyId, tenant);
                tenantIds[row.Text("Ref")!] = tenant.Id;
                result.Codes[ImportSchema.Tenants][row.RowNumber] = tenant.Code;
            }

            foreach (var row in batch.Rows[ImportSchema.Vendors])
            {
                var vendor = await _creation.CreateVendorAsync(companyId, ToVendor(row));
                result.Codes[ImportSchema.Vendors][row.RowNumber] = vendor.Code;
            }

            if (batch.Rows[ImportSchema.Leases].Count > 0)
            {
                var existingUnits = await _db.Units.AsNoTracking().Where(x => x.Property!.CompanyId == companyId)
                    .Select(x => new { x.Id, x.UnitNumber }).ToListAsync();
                var existingTenants = await _db.Tenants.AsNoTracking().Where(x => x.CompanyId == companyId)
                    .Select(x => new { x.Id, x.Code }).ToListAsync();

                foreach (var row in batch.Rows[ImportSchema.Leases])
                {
                    var unitRef = row.Text("UnitRef")!;
                    var tenantRef = row.Text("TenantRef")!;
                    var notes = string.Join(" ", new[]
                    {
                        row.Text("LegacyContractNumber") is { } old ? $"Old contract number: {old}." : null,
                        row.Text("Notes") is { } note ? (note.TrimEnd().EndsWith('.') ? note.Trim() : note.Trim() + ".") : null,
                        "Imported from the previous system."
                    }.Where(x => !string.IsNullOrWhiteSpace(x)));

                    var lease = await _leases.CreateDraftAsync(companyId, userId, new LeaseCreateViewModel
                    {
                        UnitId = unitIds.TryGetValue(unitRef, out var u) ? u : existingUnits.First(x => string.Equals(x.UnitNumber, unitRef, StringComparison.OrdinalIgnoreCase)).Id,
                        TenantId = tenantIds.TryGetValue(tenantRef, out var t) ? t : existingTenants.First(x => string.Equals(x.Code, tenantRef, StringComparison.OrdinalIgnoreCase)).Id,
                        StartDate = row.Date("StartDate")!.Value,
                        EndDate = row.Date("EndDate")!.Value,
                        AnnualRent = row.Number("AnnualRent")!.Value,
                        BillingFrequencyMonths = row.Integer("BillingFrequencyMonths")!.Value,
                        SecurityDepositAmount = row.Number("SecurityDepositAmount") ?? 0m,
                        Notes = notes.Length > 500 ? notes[..500] : notes
                    });
                    result.Codes[ImportSchema.Leases][row.RowNumber] = lease.ContractNumber;
                }
            }

            if (batch.Rows[ImportSchema.Employees].Count > 0)
            {
                var departments = await _db.Departments.Where(x => x.CompanyId == companyId).ToListAsync();
                foreach (var row in batch.Rows[ImportSchema.Employees])
                {
                    var employee = ToEmployee(row);
                    if (row.Text("Department") is { } name)
                    {
                        var department = departments.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                        if (department == null)
                        {
                            department = new Department { CompanyId = companyId, Name = name, IsActive = true };
                            _db.Departments.Add(department);
                            await _db.SaveChangesAsync();
                            departments.Add(department);
                        }
                        employee.DepartmentId = department.Id;
                    }
                    if (row.Text("PropertyRef") is { } propertyRef)
                        employee.PropertyId = PropertyId(propertyRef);

                    employee.CompanyId = companyId;
                    employee.EmployeeCode = await _hr.GenerateEmployeeCodeAsync();
                    employee.CreatedAt = DateTime.UtcNow;
                    _db.Employees.Add(employee);
                    await _db.SaveChangesAsync();
                    result.Codes[ImportSchema.Employees][row.RowNumber] = employee.EmployeeCode;
                }
            }

            await transaction.CommitAsync();
            return result;
        }

        /// <summary>The uploaded file with each record's new code in a column beside it.</summary>
        public byte[] BuildResultFile(Stream original, ImportResult result)
        {
            using var workbook = new XLWorkbook(original);
            foreach (var sheet in ImportSchema.Sheets)
            {
                var codes = result.Codes[sheet.Key];
                if (codes.Count == 0)
                    continue;
                var ws = workbook.Worksheets.FirstOrDefault(w => Same(w.Name, sheet.Name) || Same(w.Name, _text.ArabicFor(sheet.Name)));
                if (ws == null)
                    continue;

                var column = (ws.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0) + 1;
                var header = ws.Cell(1, column);
                header.Value = _text["System code"];
                header.Style.Font.Bold = true;
                header.Style.Font.FontColor = XLColor.White;
                header.Style.Fill.BackgroundColor = XLColor.FromHtml("#2e9e5b");
                foreach (var (row, code) in codes)
                    ws.Cell(row, column).Value = code;
                ws.Column(column).Width = 26;
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // =====================================================================
        //  ROWS TO RECORDS
        // =====================================================================

        private static Property ToProperty(ImportRow row) => new()
        {
            Name = row.Text("Name")!,
            PropertyType = row.Text("PropertyType")!,
            OwnershipType = row.Text("OwnershipType")!,
            VatCategory = row.Text("VatCategory") ?? VatCategories.Standard,
            Address = row.Text("Address"),
            Emirate = row.Text("Emirate"),
            AreaName = row.Text("AreaName"),
            PlotNumber = row.Text("PlotNumber"),
            TitleDeedNumber = row.Text("TitleDeedNumber"),
            OwnerName = row.Text("OwnerName"),
            OwnerTrn = row.Text("OwnerTrn"),
            CommissionPercentage = row.Number("CommissionPercentage") ?? 0m,
            AssetValue = row.Number("AssetValue") ?? 0m,
            NumberOfFloors = row.Integer("NumberOfFloors"),
            BuiltUpAreaSqFt = row.Number("BuiltUpAreaSqFt"),
            CompletionDate = row.Date("CompletionDate")
        };

        private static Tenant ToTenant(ImportRow row) => new()
        {
            FullName = row.Text("FullName")!,
            PartyType = row.Text("PartyType")!,
            Email = row.Text("Email"),
            Phone = row.Text("Phone"),
            Nationality = row.Text("Nationality"),
            IdNumber = row.Text("IdNumber"),
            IdExpiryDate = row.Date("IdExpiryDate"),
            PassportNumber = row.Text("PassportNumber"),
            PassportExpiryDate = row.Date("PassportExpiryDate"),
            TradeLicenseNumber = row.Text("TradeLicenseNumber"),
            TrnNumber = row.Text("TrnNumber"),
            Address = row.Text("Address"),
            City = row.Text("City"),
            Emirate = row.Text("Emirate"),
            CountryCode = row.Text("CountryCode") ?? "AE",
            OpeningBalance = row.Number("OpeningBalance") ?? 0m
        };

        private static Vendor ToVendor(ImportRow row) => new()
        {
            Name = row.Text("Name")!,
            ServiceType = row.Text("ServiceType"),
            ContactPerson = row.Text("ContactPerson"),
            Email = row.Text("Email"),
            Phone = row.Text("Phone"),
            IsVatRegistered = row.YesNo("IsVatRegistered") ?? false,
            TrnNumber = row.Text("TrnNumber"),
            TradeLicenseNumber = row.Text("TradeLicenseNumber"),
            OpeningBalance = row.Number("OpeningBalance") ?? 0m
        };

        private static Employee ToEmployee(ImportRow row) => new()
        {
            FirstName = row.Text("FirstName")!.Trim(),
            LastName = row.Text("LastName")!.Trim(),
            NameArabic = row.Text("NameArabic"),
            Gender = row.Text("Gender")!,
            DateOfBirth = row.Date("DateOfBirth"),
            Nationality = row.Text("Nationality"),
            Email = row.Text("Email"),
            Mobile = row.Text("Mobile"),
            JobTitle = row.Text("JobTitle")!,
            JoiningDate = row.Date("JoiningDate")!.Value,
            ContractType = row.Text("ContractType") ?? EmployeeContractTypes.Unlimited,
            ContractEndDate = row.Date("ContractEndDate"),
            Status = EmployeeStatuses.Active,
            PassportNumber = row.Text("PassportNumber"),
            PassportExpiry = row.Date("PassportExpiry"),
            EmiratesIdNumber = row.Text("EmiratesIdNumber"),
            EmiratesIdExpiry = row.Date("EmiratesIdExpiry"),
            ResidenceVisaNumber = row.Text("ResidenceVisaNumber"),
            ResidenceVisaExpiry = row.Date("ResidenceVisaExpiry"),
            LabourCardNumber = row.Text("LabourCardNumber"),
            LabourCardExpiry = row.Date("LabourCardExpiry"),
            MohrePersonCode = row.Text("MohrePersonCode"),
            BasicSalary = row.Number("BasicSalary")!.Value,
            HousingAllowance = row.Number("HousingAllowance") ?? 0m,
            TransportAllowance = row.Number("TransportAllowance") ?? 0m,
            OtherAllowance = row.Number("OtherAllowance") ?? 0m,
            PaymentMethod = row.Text("PaymentMethod") ?? SalaryPaymentMethods.Wps,
            BankName = row.Text("BankName"),
            Iban = row.Text("Iban"),
            BankRoutingCode = row.Text("BankRoutingCode"),
            AnnualLeaveDaysPerYear = row.Number("AnnualLeaveDaysPerYear") ?? 30m
        };
    }
}
