using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Month-end and year-end closing.
    ///
    /// MONTHLY. A period is closed from the Closing Cockpit against a checklist that
    /// compares what the sub-ledgers say should be in the accounts for the month
    /// (depreciation schedules, revenue recognition, prepayments, rent invoices) with
    /// what the general ledger actually holds. Any red check stops the close; amber
    /// checks must be acknowledged. Periods close in order, oldest first. Once closed,
    /// nothing can be posted, reset, edited or deleted in the period - see the lock in
    /// ApplicationDbContext.
    ///
    /// REOPENING. Only on a written request: the user says why, an approver
    /// (Closing.ApproveReopen) opens the period or turns the request down. The period is
    /// then closed again through the same checklist. If the year was already closed, the
    /// year close is undone with the reopening and has to be run again.
    ///
    /// YEAR-END. When all twelve periods are closed, the year close posts one entry on
    /// the last day of the year that clears every revenue and expense account into
    /// retained earnings, and records the balance sheet carried into the next year.
    /// Balance sheet accounts are perpetual, so their closing balances are the next
    /// year's opening balances without any further posting.
    /// </summary>
    public class PeriodClosingService
    {
        public const string ClosingJournalType = "Closing";
        public const string ClosingSourceType = "FiscalYearClosing";
        private const int MinimumReasonLength = 15;

        private readonly ApplicationDbContext _db;
        private readonly FinancialReportService _reports;
        private readonly FixedAssetService _fixedAssets;
        private readonly AccountingPostingService _posting;
        private readonly JournalWorkflowService _workflow;

        public PeriodClosingService(
            ApplicationDbContext db,
            FinancialReportService reports,
            FixedAssetService fixedAssets,
            AccountingPostingService posting,
            JournalWorkflowService workflow,
            Push.PushNotifier push)
        {
            _db = db;
            _reports = reports;
            _fixedAssets = fixedAssets;
            _posting = posting;
            _workflow = workflow;
            _push = push;
        }

        private readonly Push.PushNotifier _push;

        // =====================================================================
        //  CHECKLIST
        // =====================================================================

        /// <summary>
        /// The closing checklist of one period. <paramref name="includeLedgerChecks"/> adds
        /// the checks that run a trial balance; the period tiles leave them out to stay quick.
        /// </summary>
        public async Task<List<ClosingCheckItem>> GetChecklistAsync(int companyId, AccountingPeriod period, bool includeLedgerChecks = true)
        {
            var start = period.StartDate.Date;
            var end = period.EndDate.Date;
            var checks = new List<ClosingCheckItem>
            {
                await CheckPriorPeriodsAsync(companyId, period),
                await CheckDraftJournalsAsync(companyId, start, end),
                await CheckRentInvoicesAsync(companyId, start, end),
                await CheckRevenueRecognitionAsync(companyId, start, end),
                await CheckDepreciationAsync(companyId, start, end),
                await CheckPrepaymentsAsync(companyId, start, end),
                await CheckPdcAsync(companyId, end),
                await CheckActivityAsync(companyId, start, end)
            };

            if (includeLedgerChecks)
                checks.AddRange(await CheckLedgerAsync(companyId, start, end));

            return checks;
        }

        private async Task<ClosingCheckItem> CheckPriorPeriodsAsync(int companyId, AccountingPeriod period)
        {
            var item = new ClosingCheckItem
            {
                Key = "prior",
                Title = "Earlier periods closed",
                Rule = "Months close in order, so a later month never moves an earlier one that is still open."
            };

            var open = await _db.AccountingPeriods.AsNoTracking()
                .Where(x => x.CompanyId == companyId && !x.IsClosed && x.EndDate < period.StartDate)
                .OrderBy(x => x.StartDate)
                .Select(x => x.Name)
                .ToListAsync();

            if (open.Count == 0)
            {
                item.Light = ClosingLight.Green;
                item.Detail = "Every earlier period is closed.";
            }
            else
            {
                item.Light = ClosingLight.Red;
                item.Detail = $"Close {Join(open)} first.";
            }

            return item;
        }

        private async Task<ClosingCheckItem> CheckDraftJournalsAsync(int companyId, DateTime start, DateTime end)
        {
            var item = new ClosingCheckItem
            {
                Key = "drafts",
                Title = "No draft journals left in the period",
                Rule = "A draft has no effect on the accounts and cannot be posted once the month is closed.",
                LinkUrl = $"/Journal?fromDate={start:yyyy-MM-dd}&toDate={end:yyyy-MM-dd}&postedOnly=false",
                LinkLabel = "Journal inquiry"
            };

            var drafts = await _db.JournalEntries.AsNoTracking()
                .Where(x => x.CompanyId == companyId && !x.IsPosted && x.EntryDate >= start && x.EntryDate <= end)
                .GroupBy(x => x.SourceType)
                .Select(g => new { SourceType = g.Key, Count = g.Count() })
                .ToListAsync();

            var total = drafts.Sum(x => x.Count);
            if (total == 0)
            {
                item.Light = ClosingLight.Green;
                item.Detail = "Every journal dated in the period is posted.";
                return item;
            }

            item.Light = ClosingLight.Red;
            item.Detail = $"{total} draft journal(s): " +
                          string.Join(", ", drafts.OrderByDescending(x => x.Count).Select(x => $"{x.Count} {DescribeSource(x.SourceType)}")) +
                          ". Post them, or delete the documents that should not be there.";
            item.RunStep = ClosingRunSteps.Drafts;
            item.RunLabel = "Post all drafts";
            return item;
        }

        private async Task<ClosingCheckItem> CheckRentInvoicesAsync(int companyId, DateTime start, DateTime end)
        {
            var item = new ClosingCheckItem
            {
                Key = "invoices",
                Title = "Rent invoices of the month issued",
                Rule = "Every contract instalment dated in the month is posted to receivables.",
                LinkUrl = "/CustomerInvoices",
                LinkLabel = "Customer invoices"
            };

            var invoices = await _db.CustomerInvoices.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Status != "Cancelled" && x.InvoiceDate >= start && x.InvoiceDate <= end)
                .Select(x => new { x.TotalAmount, Posted = x.JournalEntry != null && x.JournalEntry.IsPosted })
                .ToListAsync();

            if (invoices.Count == 0)
            {
                item.Light = ClosingLight.NotApplicable;
                item.Detail = "No invoices fall due in this period.";
                return item;
            }

            var pending = invoices.Where(x => !x.Posted).ToList();
            item.Expected = invoices.Sum(x => x.TotalAmount);
            item.Recorded = invoices.Where(x => x.Posted).Sum(x => x.TotalAmount);

            if (pending.Count == 0)
            {
                item.Light = ClosingLight.Green;
                item.Detail = $"All {invoices.Count} invoice(s) of the period are issued.";
            }
            else
            {
                item.Light = ClosingLight.Red;
                item.Detail = $"{pending.Count} of {invoices.Count} invoice(s) totalling {pending.Sum(x => x.TotalAmount):N2} are not issued.";
                item.RunStep = ClosingRunSteps.Invoices;
                item.RunLabel = "Issue invoices";
            }

            return item;
        }

        private async Task<ClosingCheckItem> CheckRevenueRecognitionAsync(int companyId, DateTime start, DateTime end)
        {
            var item = new ClosingCheckItem
            {
                Key = "revenue",
                Title = "Revenue recognition run",
                Rule = "The month's share of rent is released from unearned revenue, and the ledger shows the same amount.",
                LinkUrl = $"/RevenueRecognition?asOfDate={end:yyyy-MM-dd}",
                LinkLabel = "Revenue recognition"
            };

            var schedules = await _db.RevenueRecognitionSchedules.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Status != "Cancelled" && x.RecognitionDate >= start && x.RecognitionDate <= end)
                .Select(x => new { x.Amount, Posted = x.JournalEntry != null && x.JournalEntry.IsPosted })
                .ToListAsync();

            var recorded = await PostedAmountAsync(companyId, start, end, "RevenueRecognitionSchedule", credit: true);

            var activeContracts = await _db.Leases.AsNoTracking()
                .CountAsync(x => x.CompanyId == companyId && x.Status == LeaseStatuses.Active && x.StartDate <= end && x.EndDate >= start);

            item.Expected = schedules.Sum(x => x.Amount);
            item.Recorded = recorded;

            ApplyScheduleLogic(item,
                scheduled: schedules.Count,
                pending: schedules.Count(x => !x.Posted),
                pendingAmount: schedules.Where(x => !x.Posted).Sum(x => x.Amount),
                noun: "recognition line",
                nothingToDo: activeContracts == 0 ? "No active contracts in this period." : null,
                missingWarning: activeContracts > 0
                    ? $"{activeContracts} active contract(s) cover the period but nothing is scheduled for recognition. Check the contracts' schedules."
                    : null);

            if (item.Light == ClosingLight.Red)
            {
                item.RunStep = ClosingRunSteps.RevenueRecognition;
                item.RunLabel = "Run recognition";
            }

            return item;
        }

        private async Task<ClosingCheckItem> CheckDepreciationAsync(int companyId, DateTime start, DateTime end)
        {
            var item = new ClosingCheckItem
            {
                Key = "depreciation",
                Title = "Depreciation run",
                Rule = "Every asset in service is depreciated for the month, and the ledger shows the scheduled amount.",
                LinkUrl = $"/FixedAssets?postingDate={end:yyyy-MM-dd}",
                LinkLabel = "Fixed assets"
            };

            var schedules = await _db.FixedAssetDepreciations.AsNoTracking()
                .Where(x => x.FixedAsset != null && x.FixedAsset.CompanyId == companyId &&
                            x.Status != "Skipped" && x.DepreciationDate >= start && x.DepreciationDate <= end)
                .Select(x => new
                {
                    x.FixedAssetId,
                    x.Amount,
                    Posted = x.Status == "Posted" && x.JournalEntry != null && x.JournalEntry.IsPosted
                })
                .ToListAsync();

            var recorded = await PostedAmountAsync(companyId, start, end, "FixedAssetDepreciation", credit: false);

            // Active assets in service by the month end that have no line for the month at all.
            var scheduledAssetIds = schedules.Select(x => x.FixedAssetId).Distinct().ToList();
            var unscheduledAssets = await _db.FixedAssets.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Status == "Active" && x.InServiceDate <= end &&
                            !scheduledAssetIds.Contains(x.Id) &&
                            (!x.DepreciationSchedules.Any() || x.DepreciationSchedules.Any(d => d.DepreciationDate > end)))
                .Select(x => x.AssetCode)
                .ToListAsync();

            item.Expected = schedules.Sum(x => x.Amount);
            item.Recorded = recorded;

            ApplyScheduleLogic(item,
                scheduled: schedules.Count,
                pending: schedules.Count(x => !x.Posted),
                pendingAmount: schedules.Where(x => !x.Posted).Sum(x => x.Amount),
                noun: "depreciation line",
                nothingToDo: unscheduledAssets.Count == 0 ? "No depreciable assets in service this period." : null,
                missingWarning: unscheduledAssets.Count > 0
                    ? $"Asset(s) {Join(unscheduledAssets)} are in service but have no depreciation for this month."
                    : null);

            if (item.Light == ClosingLight.Green && unscheduledAssets.Count > 0)
            {
                item.Light = ClosingLight.Amber;
                item.Detail += $" But asset(s) {Join(unscheduledAssets)} have no depreciation line for this month.";
            }

            if (item.Light == ClosingLight.Red)
            {
                item.RunStep = ClosingRunSteps.Depreciation;
                item.RunLabel = "Run depreciation";
            }

            return item;
        }

        private async Task<ClosingCheckItem> CheckPrepaymentsAsync(int companyId, DateTime start, DateTime end)
        {
            var item = new ClosingCheckItem
            {
                Key = "prepayments",
                Title = "Prepayment amortisation run",
                Rule = "The month's share of prepaid expenses is charged, and the ledger shows the scheduled amount.",
                LinkUrl = $"/Prepayments?asOfDate={end:yyyy-MM-dd}",
                LinkLabel = "Prepayments"
            };

            var lines = await _db.PrepaymentScheduleLines.AsNoTracking()
                .Where(x => x.PrepaymentSchedule != null && x.PrepaymentSchedule.CompanyId == companyId &&
                            x.Status != "Cancelled" && x.RecognitionDate >= start && x.RecognitionDate <= end)
                .Select(x => new { x.Amount, Posted = x.JournalEntry != null && x.JournalEntry.IsPosted })
                .ToListAsync();

            item.Expected = lines.Sum(x => x.Amount);
            item.Recorded = await PostedAmountAsync(companyId, start, end, "PrepaymentRecognition", credit: false);

            ApplyScheduleLogic(item,
                scheduled: lines.Count,
                pending: lines.Count(x => !x.Posted),
                pendingAmount: lines.Where(x => !x.Posted).Sum(x => x.Amount),
                noun: "amortisation line",
                nothingToDo: "No prepayments to amortise this period.",
                missingWarning: null);

            if (item.Light == ClosingLight.Red)
            {
                item.RunStep = ClosingRunSteps.Prepayments;
                item.RunLabel = "Run amortisation";
            }

            return item;
        }

        /// <summary>
        /// The rule shared by every scheduled run: lines not posted = red; nothing in the
        /// ledger although something was scheduled = red; ledger and schedule disagree =
        /// amber; otherwise green.
        /// </summary>
        private static void ApplyScheduleLogic(
            ClosingCheckItem item, int scheduled, int pending, decimal pendingAmount, string noun,
            string? nothingToDo, string? missingWarning)
        {
            var expected = item.Expected ?? 0m;
            var recorded = item.Recorded ?? 0m;

            if (scheduled == 0)
            {
                if (missingWarning != null)
                {
                    item.Light = ClosingLight.Amber;
                    item.Detail = missingWarning;
                }
                else
                {
                    item.Light = recorded == 0m ? ClosingLight.NotApplicable : ClosingLight.Amber;
                    item.Detail = recorded == 0m
                        ? nothingToDo ?? "Nothing scheduled this period."
                        : $"Nothing is scheduled, yet the ledger holds {recorded:N2} for it. Check the entries.";
                }
                return;
            }

            if (pending > 0)
            {
                item.Light = ClosingLight.Red;
                item.Detail = $"{pending} of {scheduled} {noun}(s) totalling {pendingAmount:N2} are not posted.";
                return;
            }

            if (recorded == 0m && expected != 0m)
            {
                item.Light = ClosingLight.Red;
                item.Detail = $"{expected:N2} is scheduled but nothing is recorded in the general ledger.";
                return;
            }

            if (recorded != expected)
            {
                item.Light = ClosingLight.Amber;
                item.Detail = $"The ledger holds {recorded:N2} against {expected:N2} scheduled - a difference of {recorded - expected:N2}.";
                return;
            }

            item.Light = ClosingLight.Green;
            item.Detail = $"{scheduled} {noun}(s) posted; the ledger agrees at {recorded:N2}.";
        }

        private async Task<ClosingCheckItem> CheckPdcAsync(int companyId, DateTime end)
        {
            var item = new ClosingCheckItem
            {
                Key = "pdc",
                Title = "Post-dated cheques due are presented",
                Rule = "A cheque dated on or before the month end should be deposited, and one at the bank cleared or recorded as bounced.",
                LinkUrl = "/PdcReceipts",
                LinkLabel = "PDC register"
            };

            var due = await _db.PdcReceipts.AsNoTracking()
                .Where(x => x.CompanyId == companyId && (x.Status == "Received" || x.Status == "Deposited") && x.ChequeDate <= end)
                .Select(x => x.Amount)
                .ToListAsync();

            if (due.Count == 0)
            {
                item.Light = ClosingLight.Green;
                item.Detail = "No cheque due by the month end is still waiting.";
            }
            else
            {
                // Amber: a cheque dated the 30th is often presented on the 1st.
                item.Light = ClosingLight.Amber;
                item.Detail = $"{due.Count} cheque(s) totalling {due.Sum():N2} dated on or before {end:dd/MM/yyyy} are not cleared yet.";
                item.Expected = due.Sum();
            }

            return item;
        }

        private async Task<ClosingCheckItem> CheckActivityAsync(int companyId, DateTime start, DateTime end)
        {
            var item = new ClosingCheckItem
            {
                Key = "activity",
                Title = "Transactions recorded in the month",
                Rule = "A month with nothing posted is unusual for a running business, and worth a second look."
            };

            var posted = await _db.JournalEntries.AsNoTracking()
                .CountAsync(x => x.CompanyId == companyId && x.IsPosted && x.EntryDate >= start && x.EntryDate <= end);

            item.Light = posted > 0 ? ClosingLight.Green : ClosingLight.Amber;
            item.Detail = posted > 0
                ? $"{posted} journal(s) posted in the period."
                : "Nothing at all is posted in this period. Confirm that the month really had no activity.";
            return item;
        }

        private async Task<List<ClosingCheckItem>> CheckLedgerAsync(int companyId, DateTime start, DateTime end)
        {
            var balanced = new ClosingCheckItem
            {
                Key = "balanced",
                Title = "Ledger in balance",
                Rule = "Every posted entry balances, and the trial balance at the month end has equal debits and credits.",
                LinkUrl = $"/Reports/TrialBalance?asOfDate={end:yyyy-MM-dd}",
                LinkLabel = "Trial balance"
            };

            var unbalanced = await _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.JournalEntry != null && x.JournalEntry.CompanyId == companyId && x.JournalEntry.IsPosted &&
                            x.JournalEntry.EntryDate >= start && x.JournalEntry.EntryDate <= end)
                .GroupBy(x => x.JournalEntry!.EntryNumber)
                .Where(g => g.Sum(x => x.Debit) != g.Sum(x => x.Credit))
                .Select(g => g.Key)
                .ToListAsync();

            var trialBalance = await _reports.GetTrialBalanceAsync(companyId, end);
            balanced.Expected = trialBalance.TotalDebit;
            balanced.Recorded = trialBalance.TotalCredit;

            if (unbalanced.Count > 0)
            {
                balanced.Light = ClosingLight.Red;
                balanced.Detail = $"Entries {Join(unbalanced)} do not balance.";
            }
            else if (trialBalance.TotalDebit != trialBalance.TotalCredit)
            {
                balanced.Light = ClosingLight.Red;
                balanced.Detail = $"Trial balance debits {trialBalance.TotalDebit:N2} against credits {trialBalance.TotalCredit:N2}. " +
                                  "Usually the opening balances do not balance.";
            }
            else
            {
                balanced.Light = ClosingLight.Green;
                balanced.Detail = $"Trial balance agrees at {trialBalance.TotalDebit:N2}.";
            }

            var suspense = new ClosingCheckItem
            {
                Key = "suspense",
                Title = "Opening balance control account is clear",
                Rule = $"Account {OpeningBalanceService.OpeningBalanceControlAccountCode} only holds differences that still need to be explained.",
                LinkUrl = "/OpeningBalances",
                LinkLabel = "Opening balances"
            };

            var control = trialBalance.Rows.FirstOrDefault(x => x.AccountCode == OpeningBalanceService.OpeningBalanceControlAccountCode);
            var controlBalance = control?.NetBalance ?? 0m;
            suspense.Recorded = controlBalance;
            suspense.Light = controlBalance == 0m ? ClosingLight.Green : ClosingLight.Amber;
            suspense.Detail = controlBalance == 0m
                ? "Nothing is left unexplained."
                : $"It carries {controlBalance:N2}. Reconcile the opening balances before the figures are reported.";

            return new List<ClosingCheckItem> { balanced, suspense };
        }

        /// <summary>Posted debits (or credits) in the period of journals from one source.</summary>
        private async Task<decimal> PostedAmountAsync(int companyId, DateTime start, DateTime end, string sourceType, bool credit)
        {
            var lines = _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.JournalEntry != null && x.JournalEntry.CompanyId == companyId && x.JournalEntry.IsPosted &&
                            x.JournalEntry.SourceType == sourceType &&
                            x.JournalEntry.EntryDate >= start && x.JournalEntry.EntryDate <= end);

            return credit
                ? await lines.SumAsync(x => (decimal?)x.Credit) ?? 0m
                : await lines.SumAsync(x => (decimal?)x.Debit) ?? 0m;
        }

        // =====================================================================
        //  COCKPIT
        // =====================================================================

        public async Task<ClosingCockpitViewModel> GetCockpitAsync(int companyId, int fiscalYear, int? periodNumber, int userId, bool canApprove)
        {
            var today = DateTime.Today;
            var model = new ClosingCockpitViewModel
            {
                FiscalYear = fiscalYear,
                CurrentUserId = userId,
                CanApproveReopen = canApprove
            };

            model.FiscalYears = await _db.AccountingPeriods.AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .Select(x => x.FiscalYear).Distinct().OrderBy(x => x).ToListAsync();

            var periods = await _db.AccountingPeriods.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.FiscalYear == fiscalYear)
                .OrderBy(x => x.PeriodNumber)
                .ToListAsync();

            var periodIds = periods.Select(x => x.Id).ToList();
            var requests = await _db.PeriodReopenRequests.AsNoTracking()
                .Where(x => periodIds.Contains(x.AccountingPeriodId))
                .OrderByDescending(x => x.RequestedAt)
                .ToListAsync();

            foreach (var period in periods)
            {
                var tile = ToTile(period, today, requests);

                if (!tile.IsClosed && !tile.IsFuture)
                {
                    // The tile shows the month's own work. That an earlier month is still open
                    // is shown as waiting, not counted as something to fix in every later month.
                    var quick = await GetChecklistAsync(companyId, period, includeLedgerChecks: false);
                    tile.WaitsForEarlier = quick.Any(x => x.Key == "prior" && x.Light == ClosingLight.Red);
                    var own = quick.Where(x => x.Key != "prior").ToList();
                    tile.RedCount = own.Count(x => x.Light == ClosingLight.Red);
                    tile.AmberCount = own.Count(x => x.Light == ClosingLight.Amber);
                    tile.Light = tile.RedCount > 0 ? ClosingLight.Red : tile.AmberCount > 0 ? ClosingLight.Amber : ClosingLight.Green;
                }

                model.Periods.Add(tile);
            }

            // Default selection: the oldest period that still needs closing, else the current one.
            var selected = periodNumber.HasValue
                ? model.Periods.FirstOrDefault(x => x.PeriodNumber == periodNumber.Value)
                : model.Periods.FirstOrDefault(x => !x.IsClosed && !x.IsFuture)
                  ?? model.Periods.LastOrDefault(x => !x.IsFuture)
                  ?? model.Periods.FirstOrDefault();

            if (selected != null)
            {
                model.Selected = selected;
                var period = periods.First(x => x.Id == selected.Id);

                if (!selected.IsClosed && !selected.IsFuture)
                {
                    model.Checks = await GetChecklistAsync(companyId, period);
                    var own = model.Checks.Where(x => x.Key != "prior").ToList();
                    selected.WaitsForEarlier = model.Checks.Any(x => x.Key == "prior" && x.Light == ClosingLight.Red);
                    selected.RedCount = own.Count(x => x.Light == ClosingLight.Red);
                    selected.AmberCount = own.Count(x => x.Light == ClosingLight.Amber);
                    selected.Light = selected.RedCount > 0 ? ClosingLight.Red : selected.AmberCount > 0 ? ClosingLight.Amber : ClosingLight.Green;
                    model.CanClose = model.RedCount == 0;
                }

                model.RequestHistory = requests.Where(x => x.AccountingPeriodId == period.Id).ToList();
                model.PendingRequest = model.RequestHistory.FirstOrDefault(x => x.Status == PeriodReopenStatuses.Pending);
                model.Events = await _db.AccountingPeriodEvents.AsNoTracking()
                    .Where(x => x.AccountingPeriodId == period.Id)
                    .OrderByDescending(x => x.At).ThenByDescending(x => x.Id)
                    .ToListAsync();

                if (selected.IsClosed)
                    model.ReopenBlockReason = await GetReopenBlockReasonAsync(companyId, period);
            }

            if (canApprove)
            {
                model.PendingRequests = await _db.PeriodReopenRequests.AsNoTracking()
                    .Include(x => x.AccountingPeriod)
                    .Where(x => x.CompanyId == companyId && x.Status == PeriodReopenStatuses.Pending)
                    .OrderBy(x => x.RequestedAt)
                    .ToListAsync();
                model.OnlyApprover = await CountApproversAsync(companyId) <= 1;
            }

            model.YearEnd = await GetYearEndPreviewAsync(companyId, fiscalYear);

            model.FiscalYearClosings = await _db.FiscalYearClosings.AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.FiscalYear)
                .Select(x => new FiscalYearClosingItemViewModel
                {
                    FiscalYear = x.FiscalYear,
                    Status = x.Status,
                    TotalRevenue = x.TotalRevenue,
                    TotalExpenses = x.TotalExpenses,
                    ProfitOrLossAmount = x.ProfitOrLossAmount,
                    ClosedAt = x.ClosedAt,
                    ClosedByName = x.ClosedByName,
                    JournalEntryNumber = x.JournalEntry != null ? x.JournalEntry.EntryNumber : null
                })
                .ToListAsync();

            return model;
        }

        private static ClosingPeriodTile ToTile(AccountingPeriod period, DateTime today, List<PeriodReopenRequest> requests)
        {
            var own = requests.Where(x => x.AccountingPeriodId == period.Id).ToList();
            return new ClosingPeriodTile
            {
                Id = period.Id,
                FiscalYear = period.FiscalYear,
                PeriodNumber = period.PeriodNumber,
                Name = period.Name,
                StartDate = period.StartDate,
                EndDate = period.EndDate,
                IsClosed = period.IsClosed,
                ClosedAt = period.ClosedAt,
                ClosedByName = period.ClosedByName,
                ReopenedAt = period.ReopenedAt,
                ReopenedByName = period.ReopenedByName,
                ReopenCount = period.ReopenCount,
                IsFuture = !period.IsClosed && period.StartDate.Date > today,
                IsReopened = !period.IsClosed && own.Any(x => x.Status == PeriodReopenStatuses.Approved),
                HasPendingRequest = own.Any(x => x.Status == PeriodReopenStatuses.Pending),
                Light = period.IsClosed ? ClosingLight.Green : ClosingLight.NotApplicable
            };
        }

        // =====================================================================
        //  RUNS FROM THE COCKPIT
        // =====================================================================

        /// <summary>
        /// Runs one closing process for exactly this period's dates, so a run from the
        /// cockpit never reaches into another month.
        /// </summary>
        public async Task<(bool Success, string Message)> RunStepAsync(int companyId, int periodId, string step)
        {
            var period = await _db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x => x.Id == periodId && x.CompanyId == companyId);
            if (period == null)
                return (false, "The accounting period was not found.");
            if (period.IsClosed)
                return (false, $"{period.Name} is closed. Nothing can be run in it.");

            var start = period.StartDate.Date;
            var end = period.EndDate.Date;
            var failures = new List<string>();

            try
            {
                switch (step)
                {
                    case ClosingRunSteps.Depreciation:
                    {
                        var result = await _fixedAssets.PostDepreciationUpToAsync(end, fromDate: start);
                        return (true, result.PostedCount == 0
                            ? "No depreciation was due for posting in the period."
                            : $"Posted {result.PostedCount} depreciation line(s) totalling {result.PostedAmount:N2}.");
                    }

                    case ClosingRunSteps.RevenueRecognition:
                    {
                        var ids = await _db.RevenueRecognitionSchedules
                            .Where(x => x.CompanyId == companyId && !x.JournalEntryId.HasValue && x.Status == "Planned" &&
                                        x.RecognitionDate >= start && x.RecognitionDate <= end)
                            .OrderBy(x => x.RecognitionDate).Select(x => x.Id).ToListAsync();

                        foreach (var id in ids)
                            await Collect(failures, () => _posting.PostRevenueRecognitionAsync(id));

                        var batch = await _workflow.PostAllDraftsAsync(start, end, "RevenueRecognitionSchedule");
                        failures.AddRange(batch.Failures);
                        return Report($"Recognised revenue: {batch.Posted} journal(s) posted.", failures);
                    }

                    case ClosingRunSteps.Prepayments:
                    {
                        var ids = await _db.PrepaymentScheduleLines
                            .Where(x => x.PrepaymentSchedule != null && x.PrepaymentSchedule.CompanyId == companyId &&
                                        !x.JournalEntryId.HasValue && x.Status == "Planned" &&
                                        x.RecognitionDate >= start && x.RecognitionDate <= end)
                            .OrderBy(x => x.RecognitionDate).Select(x => x.Id).ToListAsync();

                        foreach (var id in ids)
                            await Collect(failures, () => _posting.PostPrepaymentRecognitionAsync(id));

                        var batch = await _workflow.PostAllDraftsAsync(start, end, "PrepaymentRecognition");
                        failures.AddRange(batch.Failures);
                        return Report($"Amortised prepayments: {batch.Posted} journal(s) posted.", failures);
                    }

                    case ClosingRunSteps.Invoices:
                    {
                        var ids = await _db.CustomerInvoices
                            .Where(x => x.CompanyId == companyId && x.Status != "Cancelled" && !x.JournalEntryId.HasValue &&
                                        x.InvoiceDate >= start && x.InvoiceDate <= end)
                            .Select(x => x.Id).ToListAsync();

                        foreach (var id in ids)
                            await Collect(failures, () => _posting.PostCustomerInvoiceAsync(id));

                        var batch = await _workflow.PostAllDraftsAsync(start, end, "CustomerInvoice");
                        failures.AddRange(batch.Failures);
                        return Report($"Issued invoices: {batch.Posted} posted.", failures);
                    }

                    case ClosingRunSteps.Drafts:
                    {
                        var batch = await _workflow.PostAllDraftsAsync(start, end, null);
                        failures.AddRange(batch.Failures);
                        return Report($"Posted {batch.Posted} of {batch.Considered} draft journal(s).", failures);
                    }

                    default:
                        return (false, "Unknown closing step.");
                }
            }
            catch (InvalidOperationException ex)
            {
                return (false, ex.Message);
            }
        }

        private static async Task Collect(List<string> failures, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (InvalidOperationException ex)
            {
                failures.Add(ex.Message);
            }
        }

        private static (bool, string) Report(string done, List<string> failures)
        {
            if (failures.Count == 0)
                return (true, done);

            var shown = failures.Distinct().Take(5).ToList();
            return (false, $"{done} {failures.Count} could not be processed: {string.Join(" | ", shown)}");
        }

        // =====================================================================
        //  CLOSE A PERIOD
        // =====================================================================

        public async Task<(bool Success, string Message)> ClosePeriodAsync(
            int companyId, int periodId, bool acknowledgeWarnings, string userName)
        {
            var period = await _db.AccountingPeriods.FirstOrDefaultAsync(x => x.Id == periodId && x.CompanyId == companyId);
            if (period == null)
                return (false, "The accounting period was not found.");
            if (period.IsClosed)
                return (false, $"{period.Name} is already closed.");
            if (period.StartDate.Date > DateTime.Today)
                return (false, $"{period.Name} has not started yet.");

            var checks = await GetChecklistAsync(companyId, period);
            var red = checks.Where(x => x.Light == ClosingLight.Red).ToList();
            if (red.Count > 0)
                return (false, $"{period.Name} cannot be closed yet: " + string.Join(" ", red.Select(x => $"[{x.Title}] {x.Detail}")));

            var amber = checks.Where(x => x.Light == ClosingLight.Amber).ToList();
            if (amber.Count > 0 && !acknowledgeWarnings)
                return (false, "Review the amber warnings and tick that you have reviewed them before closing.");

            return await CloseAsync(period, userName, amber);
        }

        /// <summary>
        /// Closes a period without the checklist. Only for the sample data, which builds
        /// past months the way an accountant would have closed them.
        /// </summary>
        internal async Task CloseWithoutChecklistAsync(int companyId, int periodId, string userName)
        {
            var period = await _db.AccountingPeriods.FirstOrDefaultAsync(x => x.Id == periodId && x.CompanyId == companyId);
            if (period != null && !period.IsClosed)
                await CloseAsync(period, userName, new List<ClosingCheckItem>());
        }

        private async Task<(bool, string)> CloseAsync(AccountingPeriod period, string userName, List<ClosingCheckItem> acknowledged)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();

            period.IsClosed = true;
            period.ClosedAt = DateTime.UtcNow;
            period.ClosedByName = Limit(userName, 200);

            // A period opened on request is now closed again: that request is finished.
            var reopened = await _db.PeriodReopenRequests
                .Where(x => x.AccountingPeriodId == period.Id && x.Status == PeriodReopenStatuses.Approved)
                .ToListAsync();

            foreach (var request in reopened)
            {
                request.Status = PeriodReopenStatuses.Reclosed;
                request.ReclosedAt = DateTime.UtcNow;
                request.ReclosedByName = Limit(userName, 200);
            }

            var note = acknowledged.Count == 0
                ? "All checks green."
                : "Warnings reviewed: " + string.Join("; ", acknowledged.Select(x => $"{x.Title} - {x.Detail}"));

            AddEvent(period, reopened.Count > 0 ? AccountingPeriodActions.Reclosed : AccountingPeriodActions.Closed, note, userName);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            var isLast = !await _db.AccountingPeriods.AnyAsync(x =>
                x.CompanyId == period.CompanyId && x.FiscalYear == period.FiscalYear && !x.IsClosed);

            return (true, isLast
                ? $"{period.Name} is closed. Every period of {period.FiscalYear} is now closed - run the year-end close."
                : $"{period.Name} is closed. Nothing more can be posted into it.");
        }

        // =====================================================================
        //  REOPEN ON REQUEST
        // =====================================================================

        private async Task<string?> GetReopenBlockReasonAsync(int companyId, AccountingPeriod period)
        {
            if (!period.IsClosed)
                return $"{period.Name} is open.";

            if (await _db.PeriodReopenRequests.AnyAsync(x => x.AccountingPeriodId == period.Id && x.Status == PeriodReopenStatuses.Pending))
                return "A request to reopen this period is already waiting for a decision.";

            var laterClosedYear = await _db.FiscalYearClosings.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.FiscalYear > period.FiscalYear && x.Status == FiscalYearClosingStatuses.Closed)
                .OrderBy(x => x.FiscalYear)
                .Select(x => (int?)x.FiscalYear)
                .FirstOrDefaultAsync();

            if (laterClosedYear.HasValue)
                return $"Fiscal year {laterClosedYear} is closed, and its opening balances come from this year. " +
                       "A period of an earlier year cannot be reopened while a later year is closed.";

            return null;
        }

        public async Task<(bool Success, string Message)> RequestReopenAsync(
            int companyId, int periodId, string? reason, int userId, string userName)
        {
            var period = await _db.AccountingPeriods.FirstOrDefaultAsync(x => x.Id == periodId && x.CompanyId == companyId);
            if (period == null)
                return (false, "The accounting period was not found.");

            var blocked = await GetReopenBlockReasonAsync(companyId, period);
            if (blocked != null)
                return (false, blocked);

            reason = reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason) || reason.Length < MinimumReasonLength)
                return (false, $"Explain why the period has to be reopened (at least {MinimumReasonLength} characters): " +
                               "what is wrong, and what will be posted.");

            _db.PeriodReopenRequests.Add(new PeriodReopenRequest
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                Reason = Limit(reason, 1000),
                Status = PeriodReopenStatuses.Pending,
                RequestedByAppUserId = userId,
                RequestedByName = Limit(userName, 200),
                RequestedAt = DateTime.UtcNow
            });
            AddEvent(period, AccountingPeriodActions.ReopenRequested, reason, userName);
            await _db.SaveChangesAsync();

            await _push.ToPermissionAsync(companyId, AppPermissions.PeriodReopenApprove, userId,
                "{0} asks to reopen {1}", new object?[] { userName, period.Name },
                "{0}", new object?[] { reason },
                "/Closing/ReopenRequests", $"reopen-{period.Id}");

            return (true, $"Your request to reopen {period.Name} is sent to the approvers.");
        }

        public async Task<(bool Success, string Message)> WithdrawReopenAsync(int companyId, int requestId, int userId, string userName)
        {
            var request = await _db.PeriodReopenRequests.Include(x => x.AccountingPeriod)
                .FirstOrDefaultAsync(x => x.Id == requestId && x.CompanyId == companyId);
            if (request == null || request.Status != PeriodReopenStatuses.Pending)
                return (false, "This request is no longer waiting for a decision.");
            if (request.RequestedByAppUserId != userId)
                return (false, "Only the person who asked can withdraw the request.");

            Decide(request, PeriodReopenStatuses.Withdrawn, userId, userName, "Withdrawn by the requester.");
            AddEvent(request.AccountingPeriod!, AccountingPeriodActions.ReopenWithdrawn, null, userName);
            await _db.SaveChangesAsync();
            return (true, "The request is withdrawn.");
        }

        public async Task<(bool Success, string Message)> RejectReopenAsync(
            int companyId, int requestId, string? comment, int userId, string userName)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return (false, "Say why the request is turned down, so the requester knows what to do instead.");

            var request = await _db.PeriodReopenRequests.Include(x => x.AccountingPeriod)
                .FirstOrDefaultAsync(x => x.Id == requestId && x.CompanyId == companyId);
            if (request == null || request.Status != PeriodReopenStatuses.Pending)
                return (false, "This request is no longer waiting for a decision.");

            Decide(request, PeriodReopenStatuses.Rejected, userId, userName, comment);
            AddEvent(request.AccountingPeriod!, AccountingPeriodActions.ReopenRejected, comment.Trim(), userName);
            await _db.SaveChangesAsync();
            if (request.RequestedByAppUserId != userId)
                _push.ToStaff(new[] { request.RequestedByAppUserId }, "Reopening {0} was turned down", new object?[] { request.AccountingPeriod!.Name },
                    "{0}", new object?[] { comment.Trim() }, "/Closing", $"reopen-{request.AccountingPeriodId}");
            return (true, $"The request to reopen {request.AccountingPeriod!.Name} is turned down.");
        }

        public async Task<(bool Success, string Message)> ApproveReopenAsync(
            int companyId, int requestId, string? comment, int userId, string userName, bool isSystemAdmin)
        {
            var request = await _db.PeriodReopenRequests.Include(x => x.AccountingPeriod)
                .FirstOrDefaultAsync(x => x.Id == requestId && x.CompanyId == companyId);
            if (request == null || request.Status != PeriodReopenStatuses.Pending)
                return (false, "This request is no longer waiting for a decision.");

            if (request.RequestedByAppUserId == userId && !isSystemAdmin && await CountApproversAsync(companyId) > 1)
                return (false, "You asked for this reopening, so another approver has to decide it.");

            var period = request.AccountingPeriod!;
            if (!period.IsClosed)
                return (false, $"{period.Name} is already open.");

            var laterClosedYear = await _db.FiscalYearClosings.AnyAsync(x =>
                x.CompanyId == companyId && x.FiscalYear > period.FiscalYear && x.Status == FiscalYearClosingStatuses.Closed);
            if (laterClosedYear)
                return (false, "A later fiscal year is closed. Its close has to be undone first.");

            await using var transaction = await _db.Database.BeginTransactionAsync();

            period.IsClosed = false;
            period.ReopenedAt = DateTime.UtcNow;
            period.ReopenedByName = Limit(userName, 200);
            period.ReopenCount++;

            Decide(request, PeriodReopenStatuses.Approved, userId, userName, comment);
            AddEvent(period, AccountingPeriodActions.Reopened,
                $"Reason: {request.Reason}" + (string.IsNullOrWhiteSpace(comment) ? string.Empty : $" | Approver: {comment.Trim()}"),
                userName);

            var yearUndone = await UndoYearCloseAsync(companyId, period.FiscalYear, userName);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            if (request.RequestedByAppUserId != userId)
                _push.ToStaff(new[] { request.RequestedByAppUserId }, "{0} is open again", new object?[] { period.Name },
                    "Reopened by {0} for your correction.", new object?[] { userName }, "/Closing", $"reopen-{period.Id}");

            return (true, $"{period.Name} is open again for {request.RequestedByName}'s correction. Close it again from the cockpit when done." +
                          (yearUndone ? $" The close of fiscal year {period.FiscalYear} was undone and must be run again." : string.Empty));
        }

        /// <summary>
        /// A period of a closed year was reopened: the closing entry no longer reflects the
        /// year, so it is removed and the year has to be closed again. The row is kept.
        /// </summary>
        private async Task<bool> UndoYearCloseAsync(int companyId, int fiscalYear, string userName)
        {
            var closing = await _db.FiscalYearClosings
                .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.FiscalYear == fiscalYear && x.Status == FiscalYearClosingStatuses.Closed);
            if (closing == null)
                return false;

            if (closing.JournalEntryId.HasValue)
            {
                var journal = await _db.JournalEntries.Include(x => x.Lines)
                    .FirstOrDefaultAsync(x => x.Id == closing.JournalEntryId.Value);

                closing.JournalEntryId = null;
                if (journal != null)
                {
                    // The closing entry sits in the year's last period, which is still closed.
                    using (_db.BypassPeriodLock())
                    {
                        _db.JournalEntryLines.RemoveRange(journal.Lines);
                        _db.JournalEntries.Remove(journal);
                        await _db.SaveChangesAsync();
                    }
                }
            }

            closing.Status = FiscalYearClosingStatuses.Reopened;
            closing.ReopenedAt = DateTime.UtcNow;
            closing.ReopenedByName = Limit(userName, 200);

            var carried = await _db.FiscalYearCarryForwards.Where(x => x.FiscalYearClosingId == closing.Id).ToListAsync();
            _db.FiscalYearCarryForwards.RemoveRange(carried);

            var lastPeriod = await _db.AccountingPeriods
                .Where(x => x.CompanyId == companyId && x.FiscalYear == fiscalYear)
                .OrderByDescending(x => x.PeriodNumber).FirstAsync();
            AddEvent(lastPeriod, AccountingPeriodActions.YearReopened,
                $"Closing entry removed; fiscal year {fiscalYear} must be closed again.", userName);

            return true;
        }

        public async Task<int> CountApproversAsync(int companyId)
        {
            return await _db.AppUserPermissions.IgnoreQueryFilters()
                .Where(p => p.PermissionKey == AppPermissions.PeriodReopenApprove &&
                            _db.AppUserCompanies.IgnoreQueryFilters().Any(c => c.AppUserId == p.AppUserId && c.CompanyId == companyId))
                .Select(p => p.AppUserId).Distinct().CountAsync();
        }

        /// <summary>Requests of the current company waiting for a decision, for the menu badge.</summary>
        public Task<int> CountPendingReopenRequestsAsync() =>
            _db.PeriodReopenRequests.CountAsync(x => x.Status == PeriodReopenStatuses.Pending);

        public async Task<ReopenRequestsViewModel> GetReopenRequestsAsync(int companyId, int userId)
        {
            var all = await _db.PeriodReopenRequests.AsNoTracking()
                .Include(x => x.AccountingPeriod)
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.RequestedAt)
                .Take(200)
                .ToListAsync();

            return new ReopenRequestsViewModel
            {
                Pending = all.Where(x => x.Status == PeriodReopenStatuses.Pending).OrderBy(x => x.RequestedAt).ToList(),
                Recent = all.Where(x => x.Status != PeriodReopenStatuses.Pending).Take(50).ToList(),
                CurrentUserId = userId,
                OnlyApprover = await CountApproversAsync(companyId) <= 1
            };
        }

        private static void Decide(PeriodReopenRequest request, string status, int userId, string userName, string? comment)
        {
            request.Status = status;
            request.DecidedByAppUserId = userId;
            request.DecidedByName = Limit(userName, 200);
            request.DecidedAt = DateTime.UtcNow;
            request.DecisionComment = string.IsNullOrWhiteSpace(comment) ? null : Limit(comment.Trim(), 1000);
        }

        // =====================================================================
        //  YEAR-END
        // =====================================================================

        public async Task<FiscalYearClosePreview> GetYearEndPreviewAsync(int companyId, int fiscalYear)
        {
            var preview = new FiscalYearClosePreview { FiscalYear = fiscalYear };

            var periods = await _db.AccountingPeriods.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.FiscalYear == fiscalYear)
                .OrderBy(x => x.PeriodNumber)
                .ToListAsync();

            var closing = await _db.FiscalYearClosings.AsNoTracking()
                .Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.FiscalYear == fiscalYear);

            if (periods.Count == 0)
            {
                preview.Blockers.Add($"Fiscal year {fiscalYear} has no accounting periods.");
                return preview;
            }

            preview.StartDate = periods.Min(x => x.StartDate);
            preview.EndDate = periods.Max(x => x.EndDate);

            if (closing?.Status == FiscalYearClosingStatuses.Closed)
            {
                preview.IsClosed = true;
                preview.ClosedAt = closing.ClosedAt;
                preview.ClosedByName = closing.ClosedByName;
                preview.JournalEntryId = closing.JournalEntryId;
                preview.JournalEntryNumber = closing.JournalEntry?.EntryNumber;
                preview.TotalRevenue = closing.TotalRevenue;
                preview.TotalExpenses = closing.TotalExpenses;
                preview.CarryForwards = await _db.FiscalYearCarryForwards.AsNoTracking()
                    .Where(x => x.FiscalYearClosingId == closing.Id)
                    .OrderBy(x => x.AccountCode)
                    .ToListAsync();
                return preview;
            }

            preview.WasReopened = closing?.Status == FiscalYearClosingStatuses.Reopened;

            var open = periods.Where(x => !x.IsClosed).Select(x => x.Name).ToList();
            if (open.Count > 0)
                preview.Blockers.Add($"Close every period first. Still open: {Join(open)}.");

            var previousYear = await _db.AccountingPeriods.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.FiscalYear == fiscalYear - 1)
                .Select(x => new { x.StartDate, x.EndDate })
                .ToListAsync();

            if (previousYear.Count > 0)
            {
                var prevStart = previousYear.Min(x => x.StartDate);
                var prevEnd = previousYear.Max(x => x.EndDate);
                var previousClosed = await _db.FiscalYearClosings.AnyAsync(x =>
                    x.CompanyId == companyId && x.FiscalYear == fiscalYear - 1 && x.Status == FiscalYearClosingStatuses.Closed);
                var previousHadActivity = await _db.JournalEntries.AnyAsync(x =>
                    x.CompanyId == companyId && x.IsPosted && x.EntryDate >= prevStart && x.EntryDate <= prevEnd);

                if (!previousClosed && previousHadActivity)
                    preview.Blockers.Add($"Close fiscal year {fiscalYear - 1} first: its results must be in retained earnings before this year's.");
            }

            var settings = await _db.CompanyAccountSettings.AsNoTracking().Include(x => x.RetainedEarningsPriorYearsAccount)
                .FirstOrDefaultAsync(x => x.CompanyId == companyId);
            var retained = settings?.RetainedEarningsPriorYearsAccount;
            if (retained == null)
                preview.Blockers.Add("Map the Retained Earnings account under Account Mappings.");
            else if (!retained.IsPostable || !retained.IsActive)
                preview.Blockers.Add($"The retained earnings account {retained.Code} must be active and postable.");
            else
                preview.RetainedEarningsAccount = $"{retained.Code} - {retained.Name}";

            var balances = await GetResultBalancesAsync(companyId, preview.StartDate.Value, preview.EndDate.Value);
            preview.TotalRevenue = balances.Where(x => x.AccountType == "Revenue").Sum(x => x.Balance);
            preview.TotalExpenses = balances.Where(x => x.AccountType == "Expense").Sum(x => x.Balance);
            preview.RevenueAccounts = balances.Count(x => x.AccountType == "Revenue");
            preview.ExpenseAccounts = balances.Count(x => x.AccountType == "Expense");

            return preview;
        }

        private sealed record ResultBalance(int AccountId, string AccountType, decimal Balance);

        /// <summary>Each revenue and expense account's balance for the year, in its natural sign.</summary>
        private async Task<List<ResultBalance>> GetResultBalancesAsync(int companyId, DateTime start, DateTime end)
        {
            var rows = await _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.JournalEntry != null && x.JournalEntry.CompanyId == companyId && x.JournalEntry.IsPosted &&
                            x.JournalEntry.JournalType != ClosingJournalType &&
                            x.JournalEntry.EntryDate >= start && x.JournalEntry.EntryDate <= end)
                .Where(x => x.Account != null && (x.Account.AccountType == "Revenue" || x.Account.AccountType == "Expense"))
                .GroupBy(x => new { x.AccountId, x.Account!.AccountType })
                .Select(g => new { g.Key.AccountId, g.Key.AccountType, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
                .ToListAsync();

            return rows
                .Select(x => new ResultBalance(x.AccountId, x.AccountType,
                    x.AccountType == "Revenue" ? x.Credit - x.Debit : x.Debit - x.Credit))
                .Where(x => x.Balance != 0m)
                .ToList();
        }

        public async Task<(bool Success, string Message)> CloseFiscalYearAsync(int companyId, int fiscalYear, string userName)
        {
            var preview = await GetYearEndPreviewAsync(companyId, fiscalYear);
            if (preview.IsClosed)
                return (false, $"Fiscal year {fiscalYear} is already closed.");
            if (preview.Blockers.Count > 0)
                return (false, $"Fiscal year {fiscalYear} cannot be closed yet: " + string.Join(" ", preview.Blockers));

            var company = await _db.Companies.FirstAsync(x => x.Id == companyId);
            var settings = await _db.CompanyAccountSettings.FirstAsync(x => x.CompanyId == companyId);
            var retainedEarningsId = settings.RetainedEarningsPriorYearsAccountId!.Value;
            var start = preview.StartDate!.Value;
            var end = preview.EndDate!.Value;

            var lastPeriod = await _db.AccountingPeriods
                .Where(x => x.CompanyId == companyId && x.FiscalYear == fiscalYear)
                .OrderByDescending(x => x.PeriodNumber).FirstAsync();

            var balances = await GetResultBalancesAsync(companyId, start, end);
            var totalRevenue = balances.Where(x => x.AccountType == "Revenue").Sum(x => x.Balance);
            var totalExpenses = balances.Where(x => x.AccountType == "Expense").Sum(x => x.Balance);
            var net = totalRevenue - totalExpenses;

            await using var transaction = await _db.Database.BeginTransactionAsync();

            JournalEntry? journal = null;
            if (balances.Count > 0)
            {
                // Each account is cleared by its balance: revenue (credit nature) is debited,
                // expense (debit nature) credited, and the difference goes to retained earnings.
                var lines = balances.Select(x => new JournalEntryLine
                {
                    AccountId = x.AccountId,
                    Debit = x.AccountType == "Revenue" ? Math.Max(x.Balance, 0m) : Math.Max(-x.Balance, 0m),
                    Credit = x.AccountType == "Revenue" ? Math.Max(-x.Balance, 0m) : Math.Max(x.Balance, 0m),
                    Description = $"Year-end closing of {(x.AccountType == "Revenue" ? "revenue" : "expense")} for {fiscalYear}"
                }).ToList();

                if (net != 0m)
                {
                    lines.Add(new JournalEntryLine
                    {
                        AccountId = retainedEarningsId,
                        Debit = net < 0m ? -net : 0m,
                        Credit = net > 0m ? net : 0m,
                        Description = net > 0m
                            ? $"Net profit for {fiscalYear} transferred to retained earnings"
                            : $"Net loss for {fiscalYear} transferred to retained earnings"
                    });
                }

                journal = new JournalEntry
                {
                    CompanyId = companyId,
                    AccountingPeriodId = lastPeriod.Id,
                    EntryNumber = $"FYC-{fiscalYear}-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    EntryDate = end,
                    JournalType = ClosingJournalType,
                    // Posted at once: the last period is closed, so a draft could never be posted.
                    Status = "Posted",
                    IsPosted = true,
                    PostedAt = DateTime.UtcNow,
                    PostedBy = Limit(userName, 200),
                    Description = $"Fiscal year closing for {fiscalYear}: revenue and expenses to retained earnings",
                    DocumentNumber = $"FY-{fiscalYear}",
                    ReferenceNumber = $"FY-CLOSE-{fiscalYear}",
                    SourceType = ClosingSourceType,
                    SourceId = fiscalYear,
                    CreatedAt = DateTime.UtcNow,
                    Lines = lines
                };

                // The year's last period is closed by now; the closing entry is the one
                // entry allowed into it.
                using (_db.BypassPeriodLock())
                {
                    _db.JournalEntries.Add(journal);
                    await _db.SaveChangesAsync();
                }
            }

            var closing = await _db.FiscalYearClosings.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.FiscalYear == fiscalYear);
            if (closing == null)
            {
                closing = new FiscalYearClosing { CompanyId = companyId, FiscalYear = fiscalYear };
                _db.FiscalYearClosings.Add(closing);
            }

            closing.RetainedEarningsAccountId = retainedEarningsId;
            closing.JournalEntryId = journal?.Id;
            closing.TotalRevenue = totalRevenue;
            closing.TotalExpenses = totalExpenses;
            closing.ProfitOrLossAmount = net;
            closing.Status = FiscalYearClosingStatuses.Closed;
            closing.ClosedAt = DateTime.UtcNow;
            closing.ClosedByName = Limit(userName, 200);
            closing.Notes = $"Closed from the Closing Cockpit by {Limit(userName, 200)}";
            await _db.SaveChangesAsync();

            // The balance sheet at the year end, after the close, is what the next year opens with.
            var trialBalance = await _reports.GetTrialBalanceAsync(companyId, end);
            var old = await _db.FiscalYearCarryForwards.Where(x => x.FiscalYearClosingId == closing.Id).ToListAsync();
            _db.FiscalYearCarryForwards.RemoveRange(old);
            _db.FiscalYearCarryForwards.AddRange(trialBalance.Rows
                .Where(x => x.AccountType is "Asset" or "Liability" or "Equity")
                .Select(x => new FiscalYearCarryForward
                {
                    CompanyId = companyId,
                    FiscalYearClosingId = closing.Id,
                    OpeningFiscalYear = fiscalYear + 1,
                    AccountId = x.AccountId,
                    AccountCode = x.AccountCode,
                    AccountName = Limit(x.AccountName, 200),
                    AccountType = x.AccountType,
                    DebitBalance = x.DebitBalance,
                    CreditBalance = x.CreditBalance
                }));

            AddEvent(lastPeriod, AccountingPeriodActions.YearClosed,
                $"Revenue {totalRevenue:N2}, expenses {totalExpenses:N2}, {(net >= 0m ? "profit" : "loss")} {Math.Abs(net):N2} to retained earnings.",
                userName);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            // The next year must exist for its opening balances (and its postings) to have somewhere to go.
            await ChartOfAccountsSeeder.EnsureFiscalYearPeriodsAsync(_db, company, fiscalYear + 1);

            return (true, $"Fiscal year {fiscalYear} is closed. {(net >= 0m ? "Profit" : "Loss")} of {Math.Abs(net):N2} " +
                          $"is transferred to retained earnings{(journal != null ? $" by entry {journal.EntryNumber}" : string.Empty)}, " +
                          $"and the balance sheet is carried into {fiscalYear + 1}.");
        }

        // =====================================================================
        //  DASHBOARD
        // =====================================================================

        public async Task<UnclosedPeriodsSummary> GetUnclosedPeriodsAsync(int companyId, DateTime today)
        {
            today = today.Date;
            var periods = await _db.AccountingPeriods.AsNoTracking()
                .Where(x => x.CompanyId == companyId && !x.IsClosed && x.StartDate <= today)
                .OrderBy(x => x.StartDate)
                .ToListAsync();

            var ids = periods.Select(x => x.Id).ToList();
            var requests = await _db.PeriodReopenRequests.AsNoTracking()
                .Where(x => x.CompanyId == companyId &&
                            (x.Status == PeriodReopenStatuses.Pending || ids.Contains(x.AccountingPeriodId)))
                .Select(x => new { x.AccountingPeriodId, x.Status })
                .ToListAsync();

            var drafts = await _db.JournalEntries.AsNoTracking()
                .Where(x => x.CompanyId == companyId && !x.IsPosted && x.AccountingPeriodId != null && ids.Contains(x.AccountingPeriodId.Value))
                .GroupBy(x => x.AccountingPeriodId!.Value)
                .Select(g => new { PeriodId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.PeriodId, x => x.Count);

            var summary = new UnclosedPeriodsSummary
            {
                PendingReopenRequests = requests.Count(x => x.Status == PeriodReopenStatuses.Pending),
                Periods = periods.Select(x => new UnclosedPeriodItem
                {
                    FiscalYear = x.FiscalYear,
                    PeriodNumber = x.PeriodNumber,
                    Name = x.Name,
                    StartDate = x.StartDate,
                    EndDate = x.EndDate,
                    DaysOverdue = x.EndDate.Date < today ? (today - x.EndDate.Date).Days : 0,
                    IsReopened = requests.Any(r => r.AccountingPeriodId == x.Id && r.Status == PeriodReopenStatuses.Approved),
                    HasPendingRequest = requests.Any(r => r.AccountingPeriodId == x.Id && r.Status == PeriodReopenStatuses.Pending),
                    DraftJournals = drafts.TryGetValue(x.Id, out var count) ? count : 0
                }).ToList()
            };

            // Fiscal years that have ended but whose results are not yet in retained earnings.
            var endedYears = await _db.AccountingPeriods.AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .GroupBy(x => x.FiscalYear)
                .Select(g => new { FiscalYear = g.Key, End = g.Max(x => x.EndDate) })
                .Where(x => x.End < today)
                .Select(x => x.FiscalYear)
                .ToListAsync();

            var closedYears = await _db.FiscalYearClosings.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Status == FiscalYearClosingStatuses.Closed)
                .Select(x => x.FiscalYear)
                .ToListAsync();

            foreach (var year in endedYears.Except(closedYears).OrderBy(x => x))
            {
                var hasActivity = await _db.JournalEntries.AnyAsync(x =>
                    x.CompanyId == companyId && x.IsPosted && x.AccountingPeriod != null && x.AccountingPeriod.FiscalYear == year);
                if (hasActivity)
                    summary.UnclosedFiscalYears.Add(year);
            }

            return summary;
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        private void AddEvent(AccountingPeriod period, string action, string? note, string userName)
        {
            _db.AccountingPeriodEvents.Add(new AccountingPeriodEvent
            {
                CompanyId = period.CompanyId,
                AccountingPeriodId = period.Id,
                Action = action,
                Note = string.IsNullOrWhiteSpace(note) ? null : Limit(note, 1000),
                ByName = Limit(userName, 200),
                At = DateTime.UtcNow
            });
        }

        private static string DescribeSource(string? sourceType) => sourceType switch
        {
            "CustomerInvoice" => "rent invoice(s)",
            "CustomerReceipt" => "receipt(s)",
            "VendorBill" => "supplier bill(s)",
            "VendorPayment" => "supplier payment(s)",
            VendorClearingService.AdvanceSourceType => "advance application(s)",
            "RevenueRecognitionSchedule" => "revenue recognition",
            "PrepaymentRecognition" => "prepayment amortisation",
            "FixedAssetDepreciation" => "depreciation",
            "ManualJournal" => "manual journal(s)",
            null or "" => "other",
            _ => sourceType
        };

        private static string Join(IReadOnlyCollection<string> items) =>
            items.Count <= 6 ? string.Join(", ", items) : string.Join(", ", items.Take(6)) + $" and {items.Count - 6} more";

        private static string Limit(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
