// Checks e-invoice files against the UBL 2.1 XML schema - the structural check every
// Accredited Service Provider runs before it applies the UAE business rules.
//
//   dotnet run --project Tools/EInvoicing/SchemaCheck -- file1.xml [folder ...]
//
// Called by validate_einvoice.py; the schema files are in Tools/EInvoicing/ubl-2.1
// (OASIS UBL 2.1, https://docs.oasis-open.org/ubl/os-UBL-2.1/xsd/).
using System.Xml;
using System.Xml.Schema;

var schemaRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ubl-2.1");
var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
set.Add(null, Path.GetFullPath(Path.Combine(schemaRoot, "maindoc", "UBL-Invoice-2.1.xsd")));
set.Add(null, Path.GetFullPath(Path.Combine(schemaRoot, "maindoc", "UBL-CreditNote-2.1.xsd")));
set.Compile();

var files = args.SelectMany(a => Directory.Exists(a) ? Directory.GetFiles(a, "*.xml") : new[] { a }).ToList();
var failed = 0;

foreach (var file in files)
{
    var errors = new List<string>();
    var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = set, DtdProcessing = DtdProcessing.Prohibit };
    settings.ValidationEventHandler += (_, e) => errors.Add($"[{e.Severity}] line {e.Exception?.LineNumber}: {e.Message}");

    try
    {
        using var reader = XmlReader.Create(file, settings);
        while (reader.Read()) { }
    }
    catch (XmlException ex)
    {
        errors.Add("[Error] not well-formed XML: " + ex.Message);
    }

    Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")}  {Path.GetFileName(file)}");
    foreach (var error in errors.Take(10))
        Console.WriteLine("      " + error);

    if (errors.Count > 0)
        failed++;
}

Console.WriteLine();
Console.WriteLine($"{files.Count - failed} of {files.Count} file(s) are valid UBL 2.1.");
return failed == 0 ? 0 : 1;
