-- Managed-lease management fee FIX (changes data - for Anas to run himself)
-- Written 2026-10-19, based on the live check in managed-lease-fee-check.sql.
--
-- What the check found on live: one managed lease, MNG-C-0001-U01-C001-2026
-- (Odeh Technologies, Al Naeem Mall, 20% commission, rent 60,000 a year).
-- Its 12 monthly schedule lines (Sep 2026 - Aug 2027) say 5,000 each - the full rent.
-- They should say 1,000 each (5,000 x 20%). None of them has been posted and none
-- has a journal yet, so nothing is wrong in the ledger. Correcting the 12 planned
-- lines is all that is needed; no correcting journal.
--
-- Run the statements one at a time, in order, in the site4now SQL console.
-- Statement 2 is the only one that changes anything.


-- 1. BEFORE: the rows that will change. Expect 12 rows, AmountNow 5000, AmountWillBe 1000.
SELECT r.Id AS ScheduleId, l.ContractNumber, r.RecognitionDate, r.Status, r.Amount AS AmountNow,
       ROUND(l.AnnualRent / 12 * p.CommissionPercentage / 100, 2) AS AmountWillBe
FROM RevenueRecognitionSchedules r
JOIN Leases l ON l.Id = r.LeaseId
JOIN Units u ON u.Id = l.UnitId
JOIN Properties p ON p.Id = u.PropertyId
WHERE p.OwnershipType = 'Managed'
  AND r.Status = 'Planned'
  AND r.JournalEntryId IS NULL
  AND r.Amount = ROUND(l.AnnualRent / 12, 2)
  AND p.CommissionPercentage < 100
ORDER BY l.ContractNumber, r.RecognitionDate;


-- 2. FIX: set each not-yet-posted line on a managed lease from the full rent to the
--    commission share. It only touches lines that are still Planned, have no journal,
--    and still carry exactly the full monthly rent - so it cannot touch anything posted,
--    and running it twice changes nothing the second time. Expect "12 rows affected".
UPDATE r
SET r.Amount = ROUND(l.AnnualRent / 12 * p.CommissionPercentage / 100, 2)
FROM RevenueRecognitionSchedules r
JOIN Leases l ON l.Id = r.LeaseId
JOIN Units u ON u.Id = l.UnitId
JOIN Properties p ON p.Id = u.PropertyId
WHERE p.OwnershipType = 'Managed'
  AND r.Status = 'Planned'
  AND r.JournalEntryId IS NULL
  AND r.Amount = ROUND(l.AnnualRent / 12, 2)
  AND p.CommissionPercentage < 100;


-- 3. AFTER: every schedule line on a managed lease. Expect 12 rows, all Amount 1000,
--    all Planned, no JournalEntryId.
SELECT r.Id AS ScheduleId, l.ContractNumber, r.RecognitionDate, r.Status, r.Amount,
       ROUND(l.AnnualRent / 12 * p.CommissionPercentage / 100, 2) AS AmountShouldBe,
       r.JournalEntryId
FROM RevenueRecognitionSchedules r
JOIN Leases l ON l.Id = r.LeaseId
JOIN Units u ON u.Id = l.UnitId
JOIN Properties p ON p.Id = u.PropertyId
WHERE p.OwnershipType = 'Managed'
ORDER BY l.ContractNumber, r.RecognitionDate;
