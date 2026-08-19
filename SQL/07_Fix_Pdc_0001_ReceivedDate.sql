-- =============================================================================
-- 07_Fix_Pdc_0001_ReceivedDate.sql
-- -----------------------------------------------------------------------------
-- Corrects cheque 0001 (AED 15,000), which was recorded with
-- ReceivedDate = 2026-09-01, the same as its cheque date. Because the memorandum
-- journal is dated on the received date, the entry landed in September and is
-- invisible in the August Trial Balance and Balance Sheet.
--
-- The cheque date of 2026-09-01 is CORRECT and must not change - it is a genuine
-- post-dated cheque. Only the received date and the journal that follows it are
-- wrong.
--
-- BEFORE YOU RUN
--   Decide the real date the cheque was handed to you. STEP 1 shows when the
--   record was created, which is usually the same day. Then replace the date
--   '2026-08-13' in STEP 2 and STEP 3 with that date. It appears THREE times in
--   step 2 and once in step 3 - change every one.
--
-- HOW TO RUN
--   Your console executes only the first batch, so run each step separately.
-- =============================================================================


-- STEP 1 -- What is there now, and when was it entered.
SELECT
    p.ChequeNumber,
    p.Amount,
    p.Status,
    ReceivedDate_Wrong  = p.ReceivedDate,
    ChequeDate_Correct  = p.ChequeDate,
    MemoPostedOn        = jm.EntryDate,
    MemoEnteredOn       = jm.CreatedAt,
    MemoEntryNumber     = jm.EntryNumber,
    ClearancePostedOn   = jc.EntryDate,
    ClearanceEntryNo    = jc.EntryNumber
FROM PdcReceipts p
LEFT JOIN JournalEntries jm ON jm.Id = p.MemorandumJournalEntryId
LEFT JOIN JournalEntries jc ON jc.Id = p.ClearanceJournalEntryId
WHERE p.ChequeNumber = '0001';


-- STEP 2 -- Re-date the memorandum journal, and move it to the accounting period
-- that contains the corrected date. Both must change together: a journal dated in
-- August but still linked to the September period would not be locked when August
-- is closed.
--
-- Replace '2026-08-13' below with the real received date (3 occurrences).
UPDATE j
SET j.EntryDate = '2026-08-13',
    j.AccountingPeriodId =
    (
        SELECT TOP 1 ap.Id
        FROM AccountingPeriods ap
        WHERE ap.CompanyId = j.CompanyId
          AND ap.StartDate <= '2026-08-13'
          AND ap.EndDate   >= '2026-08-13'
    )
FROM JournalEntries j
WHERE j.Id = (SELECT p.MemorandumJournalEntryId FROM PdcReceipts p WHERE p.ChequeNumber = '0001');


-- STEP 3 -- Correct the PDC record itself so the register agrees with the ledger.
-- Replace '2026-08-13' with the same date used in step 2.
UPDATE PdcReceipts
SET ReceivedDate = '2026-08-13'
WHERE ChequeNumber = '0001';


-- STEP 4 -- Verify. ReceivedDate and MemoPostedOn should now match and be in the
-- past; ChequeDate should still read 2026-09-01. PeriodName should be the month
-- of the corrected date.
SELECT
    p.ChequeNumber,
    p.Amount,
    ReceivedDate = p.ReceivedDate,
    ChequeDate   = p.ChequeDate,
    MemoPostedOn = jm.EntryDate,
    PeriodName   = ap.Name,
    PeriodClosed = ap.IsClosed
FROM PdcReceipts p
LEFT JOIN JournalEntries jm ON jm.Id = p.MemorandumJournalEntryId
LEFT JOIN AccountingPeriods ap ON ap.Id = jm.AccountingPeriodId
WHERE p.ChequeNumber = '0001';


-- STEP 5 -- Confirm the memo now shows in the control accounts. Run the Trial
-- Balance in the application for the corrected date; 11400 PDC On Hand should
-- show 15,000 debit and 21600 PDC Contra 15,000 credit. This query is the same
-- check straight from the ledger.
SELECT
    a.Code,
    a.Name,
    Debit  = SUM(l.Debit),
    Credit = SUM(l.Credit)
FROM JournalEntryLines l
JOIN Accounts a ON a.Id = l.AccountId
JOIN JournalEntries j ON j.Id = l.JournalEntryId
WHERE a.Code IN ('11400', '21600')
  AND j.IsPosted = 1
GROUP BY a.Code, a.Name
ORDER BY a.Code;


-- =============================================================================
-- WHY NOT JUST EDIT THE PDC RECORD IN THE APPLICATION
--   Changing ReceivedDate on the PDC would not move the journal - the entry is
--   already written and the posting code does not re-date an existing memo. The
--   journal has to be corrected directly, which is what step 2 does.
--
-- WHY THIS CANNOT HAPPEN AGAIN
--   The Create screen now rejects a received date in the future, and rejects a
--   cheque date earlier than the received date. Both fields are labelled with
--   what they do, and the PDC list shows the actual journal date next to each
--   cheque so a mismatch is visible without opening Journal Inquiry.
-- =============================================================================
