"""
Validates UAE e-invoices (PINT AE) against the official OpenPeppol / Ministry of Finance rules.

    python Tools/EInvoicing/validate_einvoice.py file1.xml [file2.xml ...]
    python Tools/EInvoicing/validate_einvoice.py some-folder/

Runs the three checks an Accredited Service Provider applies before accepting a document:
  - UBL 2.1 XML schema                the structure and element order (SchemaCheck, needs the .NET SDK)
  - PINT-UBL-validation-preprocessed  the shared PINT rules (totals, code lists, mandatory fields)
  - PINT-jurisdiction-aligned-rules   the UAE-specific rules (TRN/TIN format, emirates, VAT categories)

The rule files in schematron/ are the official ones from
https://docs.peppol.eu/poac/ae/pint-ae/ (PINT AE Billing 1.0.4). When the Ministry publishes a new
version, replace those two files and re-run.

Exit code 0 when every file passes, 1 otherwise.

Requires: pip install saxonche, and the .NET 8 SDK for the schema check.
"""

import os
import subprocess
import sys
import xml.etree.ElementTree as ET

from saxonche import PySaxonProcessor

HERE = os.path.dirname(os.path.abspath(__file__))
RULE_SETS = [
    ("Shared PINT rules", os.path.join(HERE, "schematron", "PINT-UBL-validation-preprocessed.xslt")),
    ("UAE rules", os.path.join(HERE, "schematron", "PINT-jurisdiction-aligned-rules.xslt")),
]
SVRL = "{http://purl.oclc.org/dsdl/svrl}"


def collect(paths):
    files = []
    for path in paths:
        if os.path.isdir(path):
            files += [os.path.join(path, f) for f in sorted(os.listdir(path)) if f.lower().endswith(".xml")]
        else:
            files.append(path)
    return files


def validate(processor, executables, path):
    problems = []
    document = processor.parse_xml(xml_file_name=os.path.abspath(path))
    for name, executable in executables:
        report = executable.transform_to_string(xdm_node=document)
        root = ET.fromstring(report)
        for failed in root.iter(SVRL + "failed-assert"):
            text = failed.find(SVRL + "text")
            message = " ".join((text.text or "").split()) if text is not None else ""
            problems.append((name, failed.get("id") or "", failed.get("flag") or "fatal",
                             failed.get("location") or "", message))
    return problems


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2

    files = collect(argv[1:])
    if not files:
        print("No XML files found.")
        return 2

    with PySaxonProcessor(license=False) as processor:
        xslt = processor.new_xslt30_processor()
        executables = [(name, xslt.compile_stylesheet(stylesheet_file=path)) for name, path in RULE_SETS]

        failures = 0
        for path in files:
            problems = validate(processor, executables, path)
            fatal = [p for p in problems if p[2] == "fatal"]
            warnings = [p for p in problems if p[2] != "fatal"]
            status = "PASS" if not fatal else "FAIL"
            print(f"{status}  {os.path.basename(path)}"
                  + (f"  ({len(fatal)} error(s))" if fatal else "")
                  + (f"  ({len(warnings)} warning(s))" if warnings else ""))
            for rule_set, rule, flag, location, message in problems:
                print(f"      [{flag}] {rule} ({rule_set}): {message}")
                if location:
                    print(f"             at {location}")
            if fatal:
                failures += 1

        print()
        print(f"{len(files) - failures} of {len(files)} file(s) passed the PINT AE rules.")

    print()
    print("UBL 2.1 schema check:")
    schema = subprocess.run(["dotnet", "run", "-v", "q", "--project", os.path.join(HERE, "SchemaCheck"), "--"]
                            + [os.path.abspath(f) for f in files])
    return 0 if failures == 0 and schema.returncode == 0 else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
