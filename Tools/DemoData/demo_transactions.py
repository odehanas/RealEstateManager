"""Creates the demo company's transactions THROUGH THE APPLICATION'S OWN SCREENS, so every
journal, invoice schedule and report figure in the help screenshots is genuine.

Run order (all LOCAL - never against the live site or database):
    1. dotnet run --project Tools/DemoData            builds the RemDemo database + master data
    2. start the app against it (see Tools/README.md)  the app seeds accounts and periods
    3. python Tools/DemoData/demo_transactions.py     this script
    4. python Tools/capture_screenshots.py            take the pictures
"""
import http.cookiejar, re, subprocess, urllib.parse, urllib.request, sys

SITE = "http://localhost:5199"
SQLCMD = r"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd.exe"

jar = http.cookiejar.CookieJar()
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))


def get(path):
    with opener.open(SITE + path) as r:
        return r.read().decode("utf-8", "replace"), r.geturl()


def token(html):
    return re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', html).group(1)


TOKEN = None


def post(path, fields, expect_redirect_not_to=None):
    data = dict(fields)
    data["__RequestVerificationToken"] = TOKEN
    body = urllib.parse.urlencode(data, doseq=True).encode()
    with opener.open(urllib.request.Request(SITE + path, data=body)) as r:
        html, url = r.read().decode("utf-8", "replace"), r.geturl()
    errors = re.findall(r'<li>([^<]{6,200})</li>', html) if "validation-summary-errors" in html else []
    errors += re.findall(r'field-validation-error"[^>]*>([^<]+)<', html)
    if errors:
        print(f"   ! {path}: {errors[:3]}")
    return html, url


def sql(query):
    out = subprocess.run([SQLCMD, "-S", r"(localdb)\ClaudeREM", "-d", "RemDemo", "-E", "-C", "-W", "-h", "-1",
                          "-s", "|", "-Q", "SET NOCOUNT ON; " + query], capture_output=True, text=True).stdout
    return [line.split("|") for line in out.strip().splitlines() if line.strip()]


# ---- sign in ----------------------------------------------------------------
html, _ = get("/Auth/Login")
TOKEN = token(html)
post("/Auth/Login", {"Username": "admin", "Password": "Admin@123"})
html, _ = get("/Leases/Create")
TOKEN = token(html)   # one request token serves every form for this signed-in session
print("signed in")

# ---- leases -------------------------------------------------------------------
LEASES = [  # unit, tenant, start, end, rent, frequency, deposit
    (1, 1, "2026-01-01", "2026-12-31", 78000, 3, 3900),
    (2, 2, "2026-03-01", "2027-02-28", 84000, 3, 4200),
    (5, 3, "2026-01-01", "2026-12-31", 150000, 3, 15000),
    (6, 4, "2026-02-01", "2027-01-31", 190000, 6, 19000),
    (8, 6, "2026-01-01", "2026-12-31", 120000, 3, 12000),
    (9, 8, "2026-04-01", "2027-03-31", 96000, 3, 9600),
    (10, 5, "2026-01-01", "2026-12-31", 140000, 6, 7000),
    (3, 7, "2026-06-01", "2027-05-31", 92000, 3, 4600),
]
for unit, tenant, start, end, rent, freq, dep in LEASES:
    post("/Leases/Create", {"UnitId": unit, "TenantId": tenant, "StartDate": start, "EndDate": end,
                            "AnnualRent": rent, "BillingFrequencyMonths": freq, "SecurityDepositAmount": dep,
                            "VatCategoryOverride": "", "VatOverrideReason": "", "Notes": ""})
print("leases:", sql("SELECT COUNT(*) FROM Leases")[0][0], "| invoices:", sql("SELECT COUNT(*) FROM CustomerInvoices")[0][0])

# ---- post every invoice dated on or before today ------------------------------
due = sql("SELECT Id FROM CustomerInvoices WHERE InvoiceDate <= '2026-10-15' ORDER BY InvoiceDate, Id")
for (inv_id,) in due:
    post("/CustomerInvoices/Post", {"id": inv_id})
print("posted invoices:", len(due))

# ---- receipts: most paid in full, a few left open for realism ------------------
invoices = sql("""SELECT i.Id, i.InvoiceNumber, i.TotalAmount, CONVERT(char(10), i.DueDate, 23), l.TenantId
                  FROM CustomerInvoices i JOIN Leases l ON l.Id = i.LeaseId
                  WHERE i.InvoiceDate <= '2026-10-15' ORDER BY i.InvoiceDate, i.Id""")
n = 0
for inv_id, number, total, due_date, tenant in invoices:
    total = float(total)
    tenant = int(tenant)
    late_month = due_date >= "2026-09-01"
    if tenant == 7 and due_date >= "2026-09-01":   # Khalid: September and October unpaid
        continue
    if tenant == 6 and due_date >= "2026-10-01":   # Desert Bloom: October unpaid
        continue
    if tenant == 3 and due_date >= "2026-10-01":   # Northwind: October covered by a PDC
        continue
    if tenant == 4 and due_date >= "2026-08-01":   # Falcon: August covered by a cleared PDC
        continue
    amount = round(total * (0.5 if (tenant == 2 and late_month) else 1.0), 2)
    n += 1
    pay_day = due_date[:8] + ("05" if tenant in (1, 3, 5) else "12")
    post("/CustomerReceipts/Create", {"CustomerInvoiceId": inv_id, "ReceiptNumber": f"RCT-2026-{n:04d}",
                                      "ReceiptDate": pay_day, "Amount": f"{amount:.2f}",
                                      "PaymentMethod": "Bank Transfer" if tenant in (3, 4, 6, 8) else "Cheque"})
print("receipts:", n)

# ---- PDCs -------------------------------------------------------------------------
def invoice_for(tenant, due_prefix):
    return sql(f"""SELECT TOP 1 i.Id, i.LeaseId, i.TotalAmount FROM CustomerInvoices i JOIN Leases l ON l.Id = i.LeaseId
                   WHERE l.TenantId = {tenant} AND CONVERT(char(10), i.DueDate, 23) LIKE '{due_prefix}%' ORDER BY i.DueDate""")[0]

for tenant, due_prefix, cheque, bank, cheque_date, received in [
        (3, "2026-10", "004418", "Emirates NBD", "2026-10-01", "2026-01-04"),
        (4, "2026-08", "117250", "RAKBANK", "2026-08-01", "2026-02-03"),
        (2, "2026-12", "000932", "ADCB", "2026-12-01", "2026-03-02")]:
    inv_id, lease_id, total = invoice_for(tenant, due_prefix)
    post("/PdcReceipts/Create", {"LeaseId": lease_id, "SettlementMode": "Receivable", "CustomerInvoiceId": inv_id,
                                 "ChequeNumber": cheque, "BankName": bank, "ChequeDate": cheque_date,
                                 "ReceivedDate": received, "Amount": total, "Notes": ""})

# ---- security deposits -------------------------------------------------------------
for lease_id, ref, day in [(1, "SD-2026-001", "2025-12-28"), (3, "SD-2026-002", "2025-12-30"), (5, "SD-2026-003", "2025-12-29"),
                           (4, "SD-2026-004", "2026-01-29"), (7, "SD-2026-005", "2025-12-27")]:
    amount = sql(f"SELECT SecurityDepositAmount FROM Leases WHERE Id = {lease_id}")[0][0]
    post("/SecurityDeposits/Create", {"LeaseId": lease_id, "ReferenceNumber": ref, "ReceiptDate": day if day >= "2026-01-01" else "2026-01-02",
                                      "Amount": amount, "PaymentMethod": "Cheque", "Status": "Held"})

ACC = {r[1]: r[0] for r in sql("SELECT Id, Code FROM Accounts WHERE CompanyId = 1")}

# ---- vendor bills -------------------------------------------------------------------
def bill(vendor, ref, day, due, category, prop, lines):
    fields = {"Id": 0, "VendorId": vendor, "VendorReference": ref, "BillDate": day, "AccountingDate": day,
              "DueDate": due, "PropertyId": prop or "", "ExpenseCategory": category, "Notes": ""}
    for i, (desc, account, line_prop, qty, price, taxable, attribution) in enumerate(lines):
        fields.update({f"Lines[{i}].Id": 0, f"Lines[{i}].IsDeleted": "false", f"Lines[{i}].Description": desc,
                       f"Lines[{i}].AccountId": ACC[account], f"Lines[{i}].PropertyId": line_prop or "",
                       f"Lines[{i}].Quantity": qty, f"Lines[{i}].UnitPrice": price,
                       f"Lines[{i}].IsTaxable": ["true", "false"] if taxable else "false",
                       f"Lines[{i}].InputTaxAttribution": attribution})
    post("/VendorBills/Create", fields)

bill(1, "EFS-2026-0912", "2026-09-30", "2026-10-30", "Cleaning & security - September", None, [
    ("Common-area cleaning - Marina Heights", "52100", 1, 1, 4500, True, "Exempt"),
    ("Office cleaning - Al Naeem Business Centre", "52100", 2, 1, 6200, True, "Taxable"),
    ("Security guards - Corniche Retail Plaza", "51100", 3, 2, 1900, True, "Taxable"),
    ("Head office cleaning", "61100", None, 1, 1800, True, "Residual")])
bill(2, "CT-7781", "2026-10-05", "2026-11-04", "AC maintenance", 2, [
    ("Quarterly AC service - floors 2 to 6", "52100", 2, 1, 8750, True, "Taxable"),
    ("Replacement filters", "52100", 2, 12, 85, True, "Taxable")])
bill(3, "RH-118", "2026-10-12", "2026-10-26", "Minor repairs", 1, [
    ("Door lock repair - unit U03", "52100", 1, 1, 650, False, "Taxable"),
    ("Paint touch-up - corridor level 7", "52100", 1, 1, 1200, False, "Taxable")])
for b_id, in sql("SELECT Id FROM VendorBills WHERE VendorReference IN ('EFS-2026-0912','CT-7781')"):
    post("/VendorBills/Post", {"id": b_id})

efs = sql("SELECT Id, TotalAmount FROM VendorBills WHERE VendorReference = 'EFS-2026-0912'")[0]
post("/VendorPayments/Create", {"VendorBillId": efs[0], "PaymentNumber": "VPAY-2026-0001", "PaymentDate": "2026-10-10",
                                "Amount": "10000.00", "PaymentMethod": "Bank Transfer"})

# ---- petty cash ----------------------------------------------------------------------
post("/PettyCash/Replenish", {"PettyCashAccountId": ACC["11150"], "ReplenishmentDate": "2026-09-01", "FundingAccountId": ACC["11100"],
                              "Amount": "3000", "ReferenceNumber": "PCR-0009", "Description": "Monthly float top-up"})
# A voucher is refused while the cash it draws on is still a draft, so post the top-up first.
post("/Journal/PostAllDrafts", {"fromDate": "2026-09-01", "toDate": "2026-09-01", "sourceType": "PettyCashReplenishment"})
for day, paid_to, desc, account, prop, amount, ref in [
        ("2026-09-04", "Union Cleaning Supplies", "Cleaning supplies", "51100", 2, 240, "R-1182"),
        ("2026-09-11", "DHL Express", "Courier - lease documents", "61100", None, 85, "DHL-5531"),
        ("2026-09-18", "Al Ain Water", "Water dispenser refill", "61100", None, 120, "AW-7710"),
        ("2026-09-26", "City Locksmiths", "Locksmith call-out, Marina Heights", "52100", 1, 350, "CL-0291")]:
    post("/PettyCash/Create", {"PettyCashAccountId": ACC["11150"], "VoucherDate": day, "PaidTo": paid_to, "Description": desc,
                               "ExpenseAccountId": ACC[account], "PropertyId": prop or "", "UnitId": "",
                               "Amount": amount, "ReferenceNumber": ref})

# ---- manual journals ---------------------------------------------------------------------
def journal(day, desc, lines):
    fields = {"EntryDate": day, "Description": desc, "DocumentNumber": "", "ReferenceNumber": "", "Remarks": ""}
    for i, (acct, dr, cr, text, cat) in enumerate(lines):
        fields.update({f"Lines[{i}].AccountId": ACC[acct], f"Lines[{i}].Debit": dr, f"Lines[{i}].Credit": cr,
                       f"Lines[{i}].LineDescription": text, f"Lines[{i}].OwnerWithdrawalCategoryId": cat or ""})
    post("/ManualJournals/Create", fields)

journal("2026-09-15", "Owner drawing - school fees, Term 1", [
    ("34100", 25000, 0, "School fees", 3), ("11100", 0, 25000, "Bank transfer", None)])
journal("2026-09-30", "Bank charges - September", [
    ("63100", 150, 0, "Bank charges", None), ("11100", 0, 150, "Bank", None)])

# ---- revenue recognition to the end of September --------------------------------------------
post("/RevenueRecognition/PostDue", {"asOfDate": "2026-09-30"})

# ---- post everything, then clear the August cheque --------------------------------------------
post("/Journal/PostAllDrafts", {"fromDate": "2025-01-01", "toDate": "2027-12-31", "sourceType": ""})
falcon = sql("SELECT Id FROM PdcReceipts WHERE ChequeNumber = '117250'")[0][0]
post("/PdcReceipts/Clear", {"PdcReceiptId": falcon, "ClearedDate": "2026-08-02", "ConfirmEarlyClearance": "false"})
post("/Journal/PostAllDrafts", {"fromDate": "2025-01-01", "toDate": "2027-12-31", "sourceType": ""})

# ---- a little work left in draft, so the draft screens have something to show ----------------
khalid = sql("""SELECT TOP 1 i.Id, i.TotalAmount FROM CustomerInvoices i JOIN Leases l ON l.Id = i.LeaseId
                WHERE l.TenantId = 7 AND i.InvoiceDate <= '2026-10-15' AND i.TotalAmount > i.AmountPaid ORDER BY i.DueDate""")[0]
post("/CustomerReceipts/Create", {"CustomerInvoiceId": khalid[0], "ReceiptNumber": "RCT-2026-0099",
                                  "ReceiptDate": "2026-10-14", "Amount": "11500.00", "PaymentMethod": "Cash"})
post("/PettyCash/Create", {"PettyCashAccountId": ACC["11150"], "VoucherDate": "2026-10-13", "PaidTo": "ACE Hardware",
                           "Description": "Light bulbs, common areas", "ExpenseAccountId": ACC["52100"], "PropertyId": 1,
                           "UnitId": "", "Amount": 165, "ReferenceNumber": "ACE-3390"})

summary = sql("""SELECT (SELECT COUNT(*) FROM JournalEntries WHERE IsPosted = 1),
                        (SELECT COUNT(*) FROM JournalEntries WHERE IsPosted = 0),
                        (SELECT COUNT(*) FROM CustomerReceipts), (SELECT COUNT(*) FROM PdcReceipts),
                        (SELECT COUNT(*) FROM VendorBills), (SELECT COUNT(*) FROM SecurityDeposits)""")[0]
print("posted journals %s | drafts %s | receipts %s | pdcs %s | bills %s | deposits %s" % tuple(summary))

# ---- tenant requests, with a staff conversation including one internal note ----------------
sql("""
DECLARE @nwInv int = (SELECT TOP 1 i.Id FROM CustomerInvoices i JOIN Leases l ON l.Id = i.LeaseId WHERE l.TenantId = 3 AND i.DueDate >= '2026-10-01' ORDER BY i.DueDate);
INSERT INTO TenantServiceRequests (CompanyId, TenantId, LeaseId, PropertyId, UnitId, RequestNumber, RequestType, Category, Subject, Description, Priority, PreferredVisitDate, PostponementApplied, Status, SubmittedAt, SubmittedByPortalUserId, AcknowledgedAt, ResolvedAt, ClosedAt, AssignedToAppUserId, ResolutionNotes, RelatedInvoiceId, RequestedNewDueDate) VALUES
 (1, 3, 3, 2, 5, 'REQ-2026-0001', 'Maintenance', 'Air Conditioning', 'Meeting room AC not cooling', 'The AC in our main meeting room has been blowing warm air since yesterday morning. Temperature reaches 29 degrees by noon.', 'High', '2026-10-16', 0, 'InProgress', '2026-10-13 07:42', 1, '2026-10-13 08:15', NULL, NULL, 3, NULL, NULL, NULL),
 (1, 3, 3, 2, 5, 'REQ-2026-0002', 'PaymentPostponement', NULL, 'Request more time for Q4 rent', 'A major client settles at the end of the month. We can clear the Q4 rent in full by 30 October.', 'Normal', NULL, 0, 'Submitted', '2026-10-14 10:05', 1, NULL, NULL, NULL, NULL, NULL, @nwInv, '2026-10-30'),
 (1, 7, 8, 1, 3, 'REQ-2026-0003', 'Maintenance', 'Plumbing', 'Water leak under kitchen sink', 'Water is pooling under the kitchen sink and the cabinet floor is wet.', 'Urgent', '2026-10-15', 0, 'Submitted', '2026-10-15 06:58', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL),
 (1, 6, 5, 3, 8, 'REQ-2026-0004', 'ContractRenewal', NULL, 'Renewal for 2027', 'We would like to renew for another year on the same terms. Please send the renewal contract.', 'Normal', NULL, 0, 'Acknowledged', '2026-10-08 11:20', NULL, '2026-10-09 09:00', NULL, NULL, 3, NULL, NULL, NULL),
 (1, 1, 1, 1, 1, 'REQ-2026-0005', 'Maintenance', 'Electrical', 'Bedroom light switch faulty', 'The switch in the master bedroom sparks when turned on.', 'High', NULL, 0, 'Closed', '2026-09-21 18:30', NULL, '2026-09-22 08:10', '2026-09-23 14:00', '2026-09-24 09:00', 3, 'Electrician replaced the switch and checked the circuit on 23 September. No further faults found.', NULL, NULL);
DECLARE @r1 int = (SELECT Id FROM TenantServiceRequests WHERE RequestNumber = 'REQ-2026-0001');
INSERT INTO TenantServiceRequestComments (TenantServiceRequestId, AuthorType, AuthorName, AppUserId, TenantPortalUserId, Body, IsInternal, CreatedAt) VALUES
 (@r1, 'Staff', 'Ravi Menon', 3, NULL, 'Status changed from Submitted to Acknowledged.', 0, '2026-10-13 08:15'),
 (@r1, 'Staff', 'Ravi Menon', 3, NULL, 'Thank you for reporting this. CoolTech will visit on Thursday 16 October between 9 and 11 am.', 0, '2026-10-13 08:17'),
 (@r1, 'Tenant', 'Northwind Trading LLC', NULL, 1, 'Thank you. Our office manager Layla will be there to let them in.', 0, '2026-10-13 09:02'),
 (@r1, 'Staff', 'Ravi Menon', 3, NULL, 'CoolTech suspect a gas leak - covered under the AMC, no charge to tenant.', 1, '2026-10-13 12:40'),
 (@r1, 'Staff', 'Ravi Menon', 3, NULL, 'Status changed from Acknowledged to In progress.', 0, '2026-10-13 12:41');
""")
print("tenant requests:", sql("SELECT COUNT(*) FROM TenantServiceRequests")[0][0])
