-- Managed-lease management fee check (READ ONLY - every statement is a SELECT)
-- Written 2026-10-19.
--
-- Background: before the fix in Services/LeaseScheduleBuilder.cs, a lease on a
-- MANAGED property got revenue recognition schedule lines for the FULL monthly rent.
-- Posting such a line does Dr 21500 Due to Landlords / Cr 42100 Management Fee Revenue,
-- so the whole rent was booked as our fee. The right amount is only our commission:
--     AnnualRent / 12 x CommissionPercentage / 100
--
-- Run each numbered statement on its own in the site4now SQL console (or all at once
-- in sqlcmd). Nothing here changes any data.


-- 1. Every managed lease, per company, with totals.
SELECT
    c.Id AS CompanyId,
    c.Name AS Company,
    l.Id AS LeaseId,
    l.ContractNumber,
    l.Status AS LeaseStatus,
    l.StartDate,
    l.EndDate,
    p.Name AS Property,
    p.CommissionPercentage,
    l.AnnualRent,
    ROUND(l.AnnualRent / 12, 2) AS MonthlyRent,
    ROUND(l.AnnualRent / 12 * p.CommissionPercentage / 100, 2) AS CorrectMonthlyFee,
    COUNT(r.Id) AS ScheduleLines,
    SUM(CASE WHEN r.JournalEntryId IS NULL THEN 1 ELSE 0 END) AS LinesNoJournalYet,
    SUM(CASE WHEN j.Id IS NOT NULL AND j.IsPosted = 0 THEN 1 ELSE 0 END) AS LinesWithDraftJournal,
    SUM(CASE WHEN j.IsPosted = 1 THEN 1 ELSE 0 END) AS LinesPostedToLedger,
    SUM(r.Amount) AS ScheduledTotal,
    SUM(r.Amount - ROUND(l.AnnualRent / 12 * p.CommissionPercentage / 100, 2)) AS OverstatementAllLines,
    SUM(CASE WHEN j.IsPosted = 1 THEN r.Amount - ROUND(l.AnnualRent / 12 * p.CommissionPercentage / 100, 2) ELSE 0 END) AS OverstatementAlreadyInLedger
FROM Leases l
JOIN Units u ON u.Id = l.UnitId
JOIN Properties p ON p.Id = u.PropertyId
JOIN Companies c ON c.Id = l.CompanyId
LEFT JOIN RevenueRecognitionSchedules r ON r.LeaseId = l.Id
LEFT JOIN JournalEntries j ON j.Id = r.JournalEntryId
WHERE p.OwnershipType = 'Managed'
GROUP BY c.Id, c.Name, l.Id, l.ContractNumber, l.Status, l.StartDate, l.EndDate,
         p.Name, p.CommissionPercentage, l.AnnualRent
ORDER BY c.Id, l.Id;


-- 2. Line by line: every schedule row on a managed lease, what it should be, and
--    whether its journal exists, is posted, and sits in a closed period.
SELECT
    c.Name AS Company,
    l.ContractNumber,
    r.Id AS ScheduleId,
    r.RecognitionDate,
    r.Status AS ScheduleStatus,
    r.Amount AS AmountNow,
    ROUND(l.AnnualRent / 12 * p.CommissionPercentage / 100, 2) AS AmountShouldBe,
    r.Amount - ROUND(l.AnnualRent / 12 * p.CommissionPercentage / 100, 2) AS Overstatement,
    r.JournalEntryId,
    j.EntryNumber,
    CASE WHEN j.Id IS NULL THEN 'No journal yet'
         WHEN j.IsPosted = 1 THEN 'Posted'
         ELSE 'Draft' END AS JournalState,
    ap.Name AS Period,
    CASE WHEN ap.Id IS NULL THEN 'No period'
         WHEN ap.IsClosed = 1 THEN 'CLOSED'
         ELSE 'Open' END AS PeriodState
FROM RevenueRecognitionSchedules r
JOIN Leases l ON l.Id = r.LeaseId
JOIN Units u ON u.Id = l.UnitId
JOIN Properties p ON p.Id = u.PropertyId
JOIN Companies c ON c.Id = l.CompanyId
LEFT JOIN JournalEntries j ON j.Id = r.JournalEntryId
LEFT JOIN AccountingPeriods ap
       ON ap.CompanyId = r.CompanyId
      AND r.RecognitionDate >= ap.StartDate
      AND r.RecognitionDate <= ap.EndDate
WHERE p.OwnershipType = 'Managed'
ORDER BY c.Name, l.ContractNumber, r.RecognitionDate;


-- 3. What the ledger shows today: posted revenue recognition amounts on accounts
--    21500 and 42100 that came from managed leases, by period.
SELECT
    c.Name AS Company,
    ap.Name AS Period,
    ap.IsClosed AS PeriodClosed,
    a.Code AS AccountCode,
    a.Name AS AccountName,
    SUM(jl.Debit) AS Debit,
    SUM(jl.Credit) AS Credit
FROM JournalEntries j
JOIN JournalEntryLines jl ON jl.JournalEntryId = j.Id
JOIN Accounts a ON a.Id = jl.AccountId
JOIN Companies c ON c.Id = j.CompanyId
LEFT JOIN AccountingPeriods ap ON ap.Id = j.AccountingPeriodId
JOIN RevenueRecognitionSchedules r ON r.Id = j.SourceId AND j.SourceType = 'RevenueRecognitionSchedule'
JOIN Leases l ON l.Id = r.LeaseId
JOIN Units u ON u.Id = l.UnitId
JOIN Properties p ON p.Id = u.PropertyId
WHERE p.OwnershipType = 'Managed'
  AND j.IsPosted = 1
GROUP BY c.Name, ap.StartDate, ap.Name, ap.IsClosed, a.Code, a.Name
ORDER BY c.Name, ap.StartDate, a.Code;


-- 4. The open accounting periods, per company that has a managed lease - the
--    candidates for dating a correcting journal.
SELECT
    ap.CompanyId,
    ap.Id AS PeriodId,
    ap.FiscalYear,
    ap.PeriodNumber,
    ap.Name,
    ap.StartDate,
    ap.EndDate
FROM AccountingPeriods ap
WHERE ap.IsClosed = 0
  AND ap.CompanyId IN (
        SELECT l.CompanyId
        FROM Leases l
        JOIN Units u ON u.Id = l.UnitId
        JOIN Properties p ON p.Id = u.PropertyId
        WHERE p.OwnershipType = 'Managed')
ORDER BY ap.CompanyId, ap.StartDate;
