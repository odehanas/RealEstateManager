-- =============================================================================
-- 09_Diagnose_Receipt_Failures.sql
-- -----------------------------------------------------------------------------
-- Read-only diagnostics for the four receipts showing "Not Posted", and for the
-- HTTP 500 on /CustomerReceipts/Create.
--
-- Nothing here changes any data. Run each step separately and send me the results.
-- =============================================================================


-- STEP 1 -- The orphaned receipts. These were saved but their journal failed, so
-- they have no ledger effect at all. Note the duplicates: two receipts of the same
-- amount against the same invoice.
SELECT
    r.Id,
    r.ReceiptNumber,
    r.ReceiptDate,
    r.Amount,
    i.InvoiceNumber,
    i.InvoiceDate,
    i.TotalAmount,
    i.AmountPaid,
    InvoicePosted = CASE WHEN i.JournalEntryId IS NULL THEN 'NO' ELSE 'YES' END,
    i.Status
FROM CustomerReceipts r
JOIN CustomerInvoices i ON i.Id = r.CustomerInvoiceId
WHERE r.JournalEntryId IS NULL
ORDER BY r.ReceiptDate, r.Id;


-- STEP 2 -- MOST LIKELY CAUSE. Posting resolves the accounting period that
-- contains the receipt date; a missing or closed period throws and aborts the
-- posting. Any receipt date with PeriodName NULL, or IsClosed = 1, is the answer.
SELECT
    r.ReceiptNumber,
    r.ReceiptDate,
    PeriodName   = ap.Name,
    PeriodStart  = ap.StartDate,
    PeriodEnd    = ap.EndDate,
    PeriodClosed = ap.IsClosed
FROM CustomerReceipts r
LEFT JOIN AccountingPeriods ap
       ON ap.CompanyId = r.CompanyId
      AND ap.StartDate <= r.ReceiptDate
      AND ap.EndDate   >= r.ReceiptDate
WHERE r.JournalEntryId IS NULL
ORDER BY r.ReceiptDate;


-- STEP 3 -- All periods on file. If August 2026 is missing or closed, that is it.
SELECT FiscalYear, PeriodNumber, Name, StartDate, EndDate, IsClosed
FROM AccountingPeriods
ORDER BY FiscalYear, PeriodNumber;


-- STEP 4 -- SECOND MOST LIKELY CAUSE. Posting a receipt needs the Default Cash and
-- Accounts Receivable mappings, and each mapped account must be postable and
-- active. Anything reading MISSING, or IsPostable 0, or IsActive 0, breaks posting.
SELECT
    Mapping     = 'Default Cash',
    AccountCode = a.Code,
    AccountName = a.Name,
    a.IsPostable,
    a.IsActive,
    Status      = CASE WHEN a.Id IS NULL THEN 'MISSING' ELSE 'OK' END
FROM CompanyAccountSettings s
LEFT JOIN Accounts a ON a.Id = s.DefaultCashAccountId
UNION ALL
SELECT
    'Accounts Receivable', a.Code, a.Name, a.IsPostable, a.IsActive,
    CASE WHEN a.Id IS NULL THEN 'MISSING' ELSE 'OK' END
FROM CompanyAccountSettings s
LEFT JOIN Accounts a ON a.Id = s.AccountsReceivableAccountId
UNION ALL
SELECT
    'Unearned Revenue', a.Code, a.Name, a.IsPostable, a.IsActive,
    CASE WHEN a.Id IS NULL THEN 'MISSING' ELSE 'OK' END
FROM CompanyAccountSettings s
LEFT JOIN Accounts a ON a.Id = s.UnearnedRevenueAccountId
UNION ALL
SELECT
    'Due To Landlords', a.Code, a.Name, a.IsPostable, a.IsActive,
    CASE WHEN a.Id IS NULL THEN 'MISSING' ELSE 'OK' END
FROM CompanyAccountSettings s
LEFT JOIN Accounts a ON a.Id = s.DueToLandlordsAccountId
UNION ALL
SELECT
    'VAT Payable', a.Code, a.Name, a.IsPostable, a.IsActive,
    CASE WHEN a.Id IS NULL THEN 'MISSING' ELSE 'OK' END
FROM CompanyAccountSettings s
LEFT JOIN Accounts a ON a.Id = s.VatPayableAccountId;


-- STEP 5 -- THIRD CAUSE, and the likeliest explanation for the 500 on the Create
-- page itself. Posting reads invoice.Lease.Unit.Property; a broken link there
-- throws a null reference. Any row returned here is a broken invoice.
SELECT
    i.Id,
    i.InvoiceNumber,
    HasLease    = CASE WHEN l.Id IS NULL THEN 'NO' ELSE 'YES' END,
    HasUnit     = CASE WHEN u.Id IS NULL THEN 'NO' ELSE 'YES' END,
    HasProperty = CASE WHEN p.Id IS NULL THEN 'NO' ELSE 'YES' END,
    HasTenant   = CASE WHEN t.Id IS NULL THEN 'NO' ELSE 'YES' END,
    i.TotalAmount,
    i.AmountPaid
FROM CustomerInvoices i
LEFT JOIN Leases     l ON l.Id = i.LeaseId
LEFT JOIN Units      u ON u.Id = l.UnitId
LEFT JOIN Properties p ON p.Id = u.PropertyId
LEFT JOIN Tenants    t ON t.Id = l.TenantId
WHERE i.TotalAmount > i.AmountPaid
  AND (l.Id IS NULL OR u.Id IS NULL OR p.Id IS NULL OR t.Id IS NULL);


-- STEP 6 -- The invoices the Create page tries to list. If this returns a very
-- large number of rows, or errors, that points at the page load itself.
SELECT InvoiceCount = COUNT(*)
FROM CustomerInvoices
WHERE TotalAmount > AmountPaid;
