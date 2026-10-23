using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Management reporting built on top of the general ledger and the operational
    /// sub-ledgers:
    ///
    ///  * Profit and Loss by Property  - the GL P&amp;L broken down over the PropertyId
    ///    dimension carried on every journal line.
    ///  * Property Utilization        - occupancy, area and rent realisation per property.
    ///  * Tenant Health               - a ranked scorecard per tenant built from
    ///    receivables, collections, PDC behaviour and contract tenure.
    ///
    /// All queries rely on the company query filters configured in ApplicationDbContext,
    /// so every figure is already scoped to the signed-in user's current company.
    /// </summary>
    public class ManagementReportService
    {
        private readonly ApplicationDbContext _db;

        public ManagementReportService(ApplicationDbContext db)
        {
            _db = db;
        }

        // =====================================================================
        //  PROFIT AND LOSS BY PROPERTY
        // =====================================================================

        /// <summary>
        /// Builds a revenue/expense matrix of accounts (rows) against properties (columns)
        /// for the requested period. Journal lines that carry no property dimension are
        /// reported in a dedicated "Unallocated" column so that the matrix always
        /// reconciles to the GL Profit and Loss for the same dates.
        /// </summary>
        public async Task<PropertyProfitAndLossReportDto> GetProfitAndLossByPropertyAsync(
            int companyId,
            DateTime startDate,
            DateTime endDate,
            int? propertyId = null)
        {
            var lines = await _db.JournalEntryLines
                .AsNoTracking()
                .Where(x =>
                    x.JournalEntry != null &&
                    x.JournalEntry.CompanyId == companyId &&
                    x.JournalEntry.IsPosted &&
                    x.JournalEntry.JournalType != PeriodClosingService.ClosingJournalType &&
                    x.JournalEntry.EntryDate >= startDate &&
                    x.JournalEntry.EntryDate <= endDate)
                .Where(x => x.Account != null && (x.Account.AccountType == "Revenue" || x.Account.AccountType == "Expense"))
                .Select(x => new PropertyPnlLine
                {
                    AccountId = x.AccountId,
                    AccountCode = x.Account!.Code,
                    AccountName = x.Account.Name,
                    AccountType = x.Account.AccountType,
                    ReportingGroup = x.Account.ReportingGroup,
                    PropertyId = x.PropertyId,
                    PropertyCode = x.Property != null ? x.Property.Code : null,
                    PropertyName = x.Property != null ? x.Property.Name : null,
                    Debit = x.Debit,
                    Credit = x.Credit
                })
                .ToListAsync();

            if (propertyId.HasValue)
                lines = lines.Where(x => x.PropertyId == propertyId.Value).ToList();

            // Column set: every property that actually has activity, plus Unallocated when needed.
            var columns = lines
                .Where(x => x.PropertyId.HasValue)
                .GroupBy(x => new { x.PropertyId, x.PropertyCode, x.PropertyName })
                .Select(g => new PropertyColumnDto
                {
                    PropertyId = g.Key.PropertyId!.Value,
                    PropertyCode = g.Key.PropertyCode ?? "-",
                    PropertyName = g.Key.PropertyName ?? "-"
                })
                .OrderBy(x => x.PropertyCode)
                .ToList();

            var hasUnallocated = lines.Any(x => !x.PropertyId.HasValue);
            if (hasUnallocated)
            {
                columns.Add(new PropertyColumnDto
                {
                    PropertyId = null,
                    PropertyCode = "—",
                    PropertyName = "Unallocated"
                });
            }

            PropertyProfitAndLossRowDto BuildRow(IGrouping<int, PropertyPnlLine> accountGroup, bool isRevenue)
            {
                var first = accountGroup.First();

                var row = new PropertyProfitAndLossRowDto
                {
                    AccountId = accountGroup.Key,
                    AccountCode = first.AccountCode,
                    AccountName = first.AccountName,
                    ReportingGroup = first.ReportingGroup
                };

                foreach (var column in columns)
                {
                    var amount = accountGroup
                        .Where(x => x.PropertyId == column.PropertyId)
                        .Sum(x => isRevenue
                            ? x.Credit - x.Debit
                            : x.Debit - x.Credit);

                    row.AmountsByProperty[column.Key] = amount;
                }

                row.Total = row.AmountsByProperty.Values.Sum();
                return row;
            }

            var revenueRows = lines
                .Where(x => x.AccountType == "Revenue")
                .GroupBy(x => x.AccountId)
                .Select(g => BuildRow(g, isRevenue: true))
                .Where(x => x.Total != 0m || x.AmountsByProperty.Values.Any(v => v != 0m))
                .OrderBy(x => x.AccountCode)
                .ToList();

            var expenseRows = lines
                .Where(x => x.AccountType == "Expense")
                .GroupBy(x => x.AccountId)
                .Select(g => BuildRow(g, isRevenue: false))
                .Where(x => x.Total != 0m || x.AmountsByProperty.Values.Any(v => v != 0m))
                .OrderBy(x => x.AccountCode)
                .ToList();

            var report = new PropertyProfitAndLossReportDto
            {
                StartDate = startDate,
                EndDate = endDate,
                FilteredPropertyId = propertyId,
                Columns = columns,
                Revenue = revenueRows,
                Expenses = expenseRows,
                HasUnallocatedActivity = hasUnallocated
            };

            foreach (var column in columns)
            {
                var revenue = revenueRows.Sum(r => r.AmountsByProperty.TryGetValue(column.Key, out var v) ? v : 0m);
                var expense = expenseRows.Sum(r => r.AmountsByProperty.TryGetValue(column.Key, out var v) ? v : 0m);

                report.TotalRevenueByProperty[column.Key] = revenue;
                report.TotalExpensesByProperty[column.Key] = expense;
                report.NetResultByProperty[column.Key] = revenue - expense;
            }

            report.TotalRevenue = revenueRows.Sum(x => x.Total);
            report.TotalExpenses = expenseRows.Sum(x => x.Total);
            report.NetProfitOrLoss = report.TotalRevenue - report.TotalExpenses;

            return report;
        }

        /// <summary>
        /// Properties available as a filter on the by-property Profit and Loss.
        /// </summary>
        public async Task<List<PropertyColumnDto>> GetPropertyOptionsAsync()
        {
            return await _db.Properties
                .AsNoTracking()
                .OrderBy(x => x.Code)
                .Select(x => new PropertyColumnDto
                {
                    PropertyId = x.Id,
                    PropertyCode = x.Code,
                    PropertyName = x.Name
                })
                .ToListAsync();
        }

        // =====================================================================
        //  PROPERTY UTILIZATION
        // =====================================================================

        public async Task<PropertyUtilizationReportDto> GetPropertyUtilizationAsync(int companyId, DateTime asOfDate)
        {
            var properties = await _db.Properties
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.Code)
                .Select(x => new
                {
                    x.Id,
                    x.Code,
                    x.Name,
                    x.PropertyType,
                    x.OwnershipType,
                    x.IsActive
                })
                .ToListAsync();

            var units = await _db.Units
                .AsNoTracking()
                .Select(x => new
                {
                    x.Id,
                    x.PropertyId,
                    x.Status,
                    x.AreaSqFt,
                    x.StandardAnnualRent,
                    x.IsActive
                })
                .ToListAsync();

            var activeLeases = await _db.Leases
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.Status == "Active" &&
                    x.StartDate <= asOfDate &&
                    x.EndDate >= asOfDate)
                .Select(x => new
                {
                    x.UnitId,
                    PropertyId = x.Unit!.PropertyId,
                    x.AnnualRent,
                    x.EndDate
                })
                .ToListAsync();

            var expiryHorizon = asOfDate.AddDays(90);

            var rows = new List<PropertyUtilizationRowDto>();

            foreach (var property in properties)
            {
                var propertyUnits = units.Where(u => u.PropertyId == property.Id).ToList();
                var propertyLeases = activeLeases.Where(l => l.PropertyId == property.Id).ToList();

                var leasedUnitIds = propertyLeases.Select(l => l.UnitId).ToHashSet();

                var totalUnits = propertyUnits.Count;
                var occupied = propertyUnits.Count(u => u.Status == "Occupied");
                var vacant = propertyUnits.Count(u => u.Status == "Vacant");
                var maintenance = propertyUnits.Count(u => u.Status == "UnderMaintenance");
                var reserved = propertyUnits.Count(u => u.Status == "Reserved");

                var totalArea = propertyUnits.Sum(u => u.AreaSqFt);
                var leasedArea = propertyUnits.Where(u => leasedUnitIds.Contains(u.Id)).Sum(u => u.AreaSqFt);

                var standardRent = propertyUnits.Sum(u => u.StandardAnnualRent);
                var contractedRent = propertyLeases.Sum(l => l.AnnualRent);

                rows.Add(new PropertyUtilizationRowDto
                {
                    PropertyId = property.Id,
                    PropertyCode = property.Code,
                    PropertyName = property.Name,
                    PropertyType = property.PropertyType,
                    OwnershipType = property.OwnershipType,
                    IsActive = property.IsActive,

                    TotalUnits = totalUnits,
                    OccupiedUnits = occupied,
                    VacantUnits = vacant,
                    UnderMaintenanceUnits = maintenance,
                    ReservedUnits = reserved,

                    TotalAreaSqFt = totalArea,
                    LeasedAreaSqFt = leasedArea,

                    StandardAnnualRent = standardRent,
                    ContractedAnnualRent = contractedRent,

                    ActiveLeases = propertyLeases.Count,
                    LeasesExpiringIn90Days = propertyLeases.Count(l => l.EndDate <= expiryHorizon)
                });
            }

            return new PropertyUtilizationReportDto
            {
                AsOfDate = asOfDate,
                Rows = rows
            };
        }

        // =====================================================================
        //  TENANT HEALTH / RANKING
        // =====================================================================

        /// <summary>
        /// Scores and ranks every tenant on four measured components. Components with no
        /// underlying data (e.g. a tenant who has never issued a PDC) are excluded and the
        /// remaining weights are re-normalised, so a tenant is never penalised for the
        /// absence of a behaviour rather than for the behaviour itself.
        /// </summary>
        public async Task<TenantHealthReportDto> GetTenantHealthAsync(int companyId, DateTime asOfDate)
        {
            var tenants = await _db.Tenants
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .Select(x => new
                {
                    x.Id,
                    x.Code,
                    x.FullName,
                    x.OpeningBalance,
                    x.IsActive
                })
                .ToListAsync();

            var invoices = await _db.CustomerInvoices
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.JournalEntry != null &&
                    x.JournalEntry.IsPosted &&
                    x.Status != "Cancelled" &&
                    x.InvoiceDate <= asOfDate)
                .Select(x => new
                {
                    TenantId = x.Lease!.TenantId,
                    x.InvoiceDate,
                    x.DueDate,
                    x.TotalAmount,
                    x.AmountPaid
                })
                .ToListAsync();

            var receipts = await _db.CustomerReceipts
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.ReceiptDate <= asOfDate)
                .Select(x => new
                {
                    TenantId = x.CustomerInvoice!.Lease!.TenantId,
                    x.ReceiptDate,
                    x.Amount,
                    DueDate = x.CustomerInvoice.DueDate
                })
                .ToListAsync();

            var pdcs = await _db.PdcReceipts
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.ReceivedDate <= asOfDate)
                .Select(x => new
                {
                    TenantId = x.TenantId ?? x.Lease!.TenantId,
                    x.Status,
                    x.Amount
                })
                .ToListAsync();

            var leases = await _db.Leases
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.StartDate <= asOfDate)
                .Select(x => new
                {
                    x.TenantId,
                    x.ContractNumber,
                    x.StartDate,
                    x.EndDate,
                    x.AnnualRent,
                    x.Status,
                    PropertyCode = x.Unit!.Property!.Code,
                    UnitNumber = x.Unit.UnitNumber
                })
                .ToListAsync();

            var deposits = await _db.SecurityDeposits
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Status == "Held" && x.ReceiptDate <= asOfDate)
                .Select(x => new
                {
                    TenantId = x.Lease!.TenantId,
                    x.Amount
                })
                .ToListAsync();

            var rows = new List<TenantHealthRowDto>();

            foreach (var tenant in tenants)
            {
                var tenantInvoices = invoices.Where(i => i.TenantId == tenant.Id).ToList();
                var tenantReceipts = receipts.Where(r => r.TenantId == tenant.Id).ToList();
                var tenantPdcs = pdcs.Where(p => p.TenantId == tenant.Id).ToList();
                var tenantLeases = leases.Where(l => l.TenantId == tenant.Id).ToList();

                var totalInvoiced = tenantInvoices.Sum(i => i.TotalAmount);
                var totalCollected = tenantInvoices.Sum(i => i.AmountPaid);
                var outstanding = tenant.OpeningBalance + totalInvoiced - totalCollected;

                var overdueInvoices = tenantInvoices
                    .Where(i => i.DueDate < asOfDate && i.AmountPaid < i.TotalAmount)
                    .ToList();

                var overdueAmount = overdueInvoices.Sum(i => i.TotalAmount - i.AmountPaid);
                var oldestOverdueDays = overdueInvoices.Count == 0
                    ? 0
                    : (int)(asOfDate - overdueInvoices.Min(i => i.DueDate)).TotalDays;

                var avgDaysToPay = tenantReceipts.Count == 0
                    ? (decimal?)null
                    : Math.Round((decimal)tenantReceipts.Average(r => (r.ReceiptDate - r.DueDate).TotalDays), 1);

                var pdcCleared = tenantPdcs.Where(p => p.Status == "Cleared").ToList();
                var pdcBounced = tenantPdcs.Where(p => p.Status == "Bounced").ToList();
                var pdcOnHand = tenantPdcs.Where(p => p.Status == "Received").ToList();

                var firstLeaseStart = tenantLeases.Count == 0
                    ? (DateTime?)null
                    : tenantLeases.Min(l => l.StartDate);

                var tenureYears = firstLeaseStart.HasValue
                    ? Math.Round((decimal)(asOfDate - firstLeaseStart.Value).TotalDays / 365.25m, 2)
                    : 0m;

                var currentLease = tenantLeases
                    .Where(l => l.Status == "Active" && l.StartDate <= asOfDate && l.EndDate >= asOfDate)
                    .OrderByDescending(l => l.StartDate)
                    .FirstOrDefault();

                var currentTermMonths = currentLease == null
                    ? 0
                    : (int)Math.Round((currentLease.EndDate - currentLease.StartDate).TotalDays / 30.4375);

                var row = new TenantHealthRowDto
                {
                    TenantId = tenant.Id,
                    TenantCode = tenant.Code,
                    TenantName = tenant.FullName,
                    IsActive = tenant.IsActive,

                    OpeningBalance = tenant.OpeningBalance,
                    TotalInvoiced = totalInvoiced,
                    TotalCollected = totalCollected,
                    OutstandingBalance = outstanding,
                    OverdueAmount = overdueAmount,
                    OverdueInvoiceCount = overdueInvoices.Count,
                    OldestOverdueDays = oldestOverdueDays,
                    AverageDaysToPay = avgDaysToPay,

                    PdcCount = tenantPdcs.Count,
                    PdcClearedCount = pdcCleared.Count,
                    PdcClearedAmount = pdcCleared.Sum(p => p.Amount),
                    PdcBouncedCount = pdcBounced.Count,
                    PdcBouncedAmount = pdcBounced.Sum(p => p.Amount),
                    PdcOnHandAmount = pdcOnHand.Sum(p => p.Amount),

                    SecurityDepositHeld = deposits.Where(d => d.TenantId == tenant.Id).Sum(d => d.Amount),

                    LeaseCount = tenantLeases.Count,
                    FirstLeaseStart = firstLeaseStart,
                    TenureYears = tenureYears,
                    CurrentContractNumber = currentLease?.ContractNumber,
                    CurrentContractEnd = currentLease?.EndDate,
                    CurrentContractTermMonths = currentTermMonths,
                    CurrentAnnualRent = currentLease?.AnnualRent ?? 0m,
                    CurrentUnitDisplay = currentLease == null
                        ? null
                        : $"{currentLease.PropertyCode}/{currentLease.UnitNumber}"
                };

                ScoreTenant(row);
                rows.Add(row);
            }

            var ranked = rows
                .OrderByDescending(x => x.HealthScore)
                .ThenBy(x => x.OverdueAmount)
                .ThenBy(x => x.TenantCode)
                .ToList();

            for (var i = 0; i < ranked.Count; i++)
                ranked[i].Rank = i + 1;

            return new TenantHealthReportDto
            {
                AsOfDate = asOfDate,
                Rows = ranked
            };
        }

        // =====================================================================
        //  OWNER WITHDRAWALS BY CATEGORY
        // =====================================================================

        /// <summary>
        /// Analyses movements on the owner equity accounts over the owner withdrawal
        /// category dimension. The ledger posting is a single equity account; this report
        /// is what turns that one account into a readable breakdown of owner spending
        /// without putting any of it into the company's own expense accounts.
        /// </summary>
        public async Task<OwnerWithdrawalReportDto> GetOwnerWithdrawalsAsync(
            int companyId,
            DateTime startDate,
            DateTime endDate)
        {
            var ownerAccounts = await _db.Accounts
                .AsNoTracking()
                .Where(x => x.IsOwnerAccount)
                .OrderBy(x => x.Code)
                .Select(x => new { x.Id, x.Code, x.Name, x.OpeningBalance })
                .ToListAsync();

            var report = new OwnerWithdrawalReportDto
            {
                StartDate = startDate,
                EndDate = endDate,
                HasOwnerAccounts = ownerAccounts.Count > 0
            };

            if (ownerAccounts.Count == 0)
                return report;

            var ownerAccountIds = ownerAccounts.Select(x => x.Id).ToList();

            // Everything posted to the owner accounts up to the end of the period.
            var allLines = await _db.JournalEntryLines
                .AsNoTracking()
                .Where(x =>
                    x.JournalEntry != null &&
                    x.JournalEntry.CompanyId == companyId &&
                    x.JournalEntry.IsPosted &&
                    x.JournalEntry.EntryDate <= endDate &&
                    ownerAccountIds.Contains(x.AccountId))
                .Select(x => new OwnerWithdrawalLine
                {
                    AccountId = x.AccountId,
                    EntryDate = x.JournalEntry!.EntryDate,
                    EntryNumber = x.JournalEntry.EntryNumber,
                    Description = x.Description,
                    CategoryId = x.OwnerWithdrawalCategoryId,
                    CategoryCode = x.OwnerWithdrawalCategory != null ? x.OwnerWithdrawalCategory.Code : null,
                    CategoryName = x.OwnerWithdrawalCategory != null ? x.OwnerWithdrawalCategory.Name : null,
                    Debit = x.Debit,
                    Credit = x.Credit
                })
                .ToListAsync();

            var periodLines = allLines.Where(x => x.EntryDate >= startDate).ToList();

            // Column set with equity-natural balances (credit positive; a withdrawal is a debit).
            foreach (var account in ownerAccounts)
            {
                var priorLines = allLines.Where(x => x.AccountId == account.Id && x.EntryDate < startDate);
                var opening = account.OpeningBalance + priorLines.Sum(x => x.Credit - x.Debit);

                var accountPeriod = periodLines.Where(x => x.AccountId == account.Id).ToList();
                var withdrawals = accountPeriod.Sum(x => x.Debit);
                var contributions = accountPeriod.Sum(x => x.Credit);

                report.Accounts.Add(new OwnerAccountColumnDto
                {
                    AccountId = account.Id,
                    AccountCode = account.Code,
                    AccountName = account.Name,
                    OpeningBalance = opening,
                    Withdrawals = withdrawals,
                    Contributions = contributions,
                    ClosingBalance = opening + contributions - withdrawals
                });
            }

            // Category rows. Withdrawals are debits, so the row value is debits less credits.
            var categories = await _db.OwnerWithdrawalCategories
                .AsNoTracking()
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Code)
                .Select(x => new { x.Id, x.Code, x.Name })
                .ToListAsync();

            foreach (var category in categories)
            {
                var categoryLines = periodLines.Where(x => x.CategoryId == category.Id).ToList();
                if (categoryLines.Count == 0)
                    continue;

                var row = new OwnerWithdrawalCategoryRowDto
                {
                    CategoryId = category.Id,
                    CategoryCode = category.Code,
                    CategoryName = category.Name
                };

                foreach (var account in report.Accounts)
                {
                    row.AmountsByAccount[account.AccountId] = categoryLines
                        .Where(x => x.AccountId == account.AccountId)
                        .Sum(x => x.Debit - x.Credit);
                }

                row.Total = row.AmountsByAccount.Values.Sum();
                report.Categories.Add(row);
            }

            // Anything on an owner account with no category - typically capital introduced
            // or a transfer of the owner's balance, but also genuine omissions.
            var uncategorised = periodLines.Where(x => !x.CategoryId.HasValue).ToList();
            if (uncategorised.Count > 0)
            {
                var row = new OwnerWithdrawalCategoryRowDto
                {
                    CategoryId = null,
                    CategoryCode = "—",
                    CategoryName = "Uncategorised"
                };

                foreach (var account in report.Accounts)
                {
                    row.AmountsByAccount[account.AccountId] = uncategorised
                        .Where(x => x.AccountId == account.AccountId)
                        .Sum(x => x.Debit - x.Credit);
                }

                row.Total = row.AmountsByAccount.Values.Sum();
                report.Categories.Add(row);
                report.HasUncategorisedActivity = true;
            }

            report.Details = periodLines
                .OrderBy(x => x.EntryDate)
                .ThenBy(x => x.EntryNumber)
                .ToList();

            return report;
        }

        /// <summary>
        /// Component weights. A component that cannot be measured for a tenant is dropped
        /// and its weight is redistributed across the components that can be measured.
        /// </summary>
        private const decimal WeightCollection = 40m;
        private const decimal WeightOverdue = 30m;
        private const decimal WeightPdc = 15m;
        private const decimal WeightTenure = 15m;

        private static void ScoreTenant(TenantHealthRowDto row)
        {
            var components = new List<(decimal Weight, decimal Score)>();

            // 1. Collection ratio - how much of what was billed has actually been collected.
            if (row.TotalInvoiced > 0m)
            {
                var ratio = row.TotalCollected / row.TotalInvoiced;
                row.CollectionRatio = Math.Round(ratio * 100m, 1);
                components.Add((WeightCollection, Clamp01(ratio) * 100m));
            }

            // 2. Overdue exposure - overdue balance relative to what was billed, inverted.
            if (row.TotalInvoiced > 0m)
            {
                var exposure = row.OverdueAmount / row.TotalInvoiced;
                var score = (1m - Clamp01(exposure)) * 100m;

                // Ageing penalty: lose up to 30 points as the oldest overdue item passes 120 days.
                if (row.OldestOverdueDays > 0)
                {
                    var agePenalty = Math.Min(30m, row.OldestOverdueDays / 4m);
                    score = Math.Max(0m, score - agePenalty);
                }

                row.OverdueExposureRatio = Math.Round(exposure * 100m, 1);
                components.Add((WeightOverdue, score));
            }

            // 3. PDC reliability - cleared value against cleared plus bounced value.
            var pdcSettled = row.PdcClearedAmount + row.PdcBouncedAmount;
            if (pdcSettled > 0m)
            {
                var reliability = row.PdcClearedAmount / pdcSettled;
                row.PdcReliabilityRatio = Math.Round(reliability * 100m, 1);
                components.Add((WeightPdc, Clamp01(reliability) * 100m));
            }

            // 4. Tenure and commitment - years occupied (capped at 5) plus current term length.
            if (row.LeaseCount > 0)
            {
                var tenureScore = Clamp01(row.TenureYears / 5m) * 70m;
                var termScore = Clamp01(row.CurrentContractTermMonths / 36m) * 30m;
                components.Add((WeightTenure, tenureScore + termScore));
            }

            if (components.Count == 0)
            {
                row.HealthScore = 0m;
                row.HealthGrade = "N/A";
                row.IsScored = false;
                return;
            }

            var totalWeight = components.Sum(c => c.Weight);
            var weighted = components.Sum(c => c.Weight * c.Score) / totalWeight;

            row.IsScored = true;
            row.HealthScore = Math.Round(weighted, 1);
            row.HealthGrade = row.HealthScore switch
            {
                >= 85m => "A",
                >= 70m => "B",
                >= 55m => "C",
                >= 40m => "D",
                _ => "E"
            };
        }

        private static decimal Clamp01(decimal value)
        {
            if (value < 0m) return 0m;
            if (value > 1m) return 1m;
            return value;
        }
    }

    // =========================================================================
    //  DTOs - Profit and Loss by Property
    // =========================================================================

    /// <summary>Flattened revenue/expense journal line used to build the property matrix.</summary>
    public class PropertyPnlLine
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public string ReportingGroup { get; set; } = string.Empty;
        public int? PropertyId { get; set; }
        public string? PropertyCode { get; set; }
        public string? PropertyName { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    public class PropertyColumnDto
    {
        public int? PropertyId { get; set; }
        public string PropertyCode { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;

        /// <summary>Dictionary key for this column. 0 represents the Unallocated column.</summary>
        public int Key => PropertyId ?? 0;

        public string Display => PropertyId.HasValue
            ? $"{PropertyCode} - {PropertyName}"
            : PropertyName;
    }

    public class PropertyProfitAndLossRowDto
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string ReportingGroup { get; set; } = string.Empty;

        public Dictionary<int, decimal> AmountsByProperty { get; set; } = new();
        public decimal Total { get; set; }

        public decimal AmountFor(int key) => AmountsByProperty.TryGetValue(key, out var value) ? value : 0m;
    }

    public class PropertyProfitAndLossReportDto
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int? FilteredPropertyId { get; set; }

        public List<PropertyColumnDto> Columns { get; set; } = new();
        public List<PropertyProfitAndLossRowDto> Revenue { get; set; } = new();
        public List<PropertyProfitAndLossRowDto> Expenses { get; set; } = new();

        public Dictionary<int, decimal> TotalRevenueByProperty { get; set; } = new();
        public Dictionary<int, decimal> TotalExpensesByProperty { get; set; } = new();
        public Dictionary<int, decimal> NetResultByProperty { get; set; } = new();

        public decimal TotalRevenue { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetProfitOrLoss { get; set; }

        public bool HasUnallocatedActivity { get; set; }

        public decimal RevenueFor(int key) => TotalRevenueByProperty.TryGetValue(key, out var v) ? v : 0m;
        public decimal ExpensesFor(int key) => TotalExpensesByProperty.TryGetValue(key, out var v) ? v : 0m;
        public decimal NetFor(int key) => NetResultByProperty.TryGetValue(key, out var v) ? v : 0m;

        public decimal MarginFor(int key)
        {
            var revenue = RevenueFor(key);
            return revenue == 0m ? 0m : Math.Round(NetFor(key) / revenue * 100m, 1);
        }
    }

    // =========================================================================
    //  DTOs - Property Utilization
    // =========================================================================

    public class PropertyUtilizationRowDto
    {
        public int PropertyId { get; set; }
        public string PropertyCode { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string PropertyType { get; set; } = string.Empty;
        public string OwnershipType { get; set; } = string.Empty;
        public bool IsActive { get; set; }

        public int TotalUnits { get; set; }
        public int OccupiedUnits { get; set; }
        public int VacantUnits { get; set; }
        public int UnderMaintenanceUnits { get; set; }
        public int ReservedUnits { get; set; }

        public decimal TotalAreaSqFt { get; set; }
        public decimal LeasedAreaSqFt { get; set; }

        public decimal StandardAnnualRent { get; set; }
        public decimal ContractedAnnualRent { get; set; }

        public int ActiveLeases { get; set; }
        public int LeasesExpiringIn90Days { get; set; }

        public decimal OccupancyRate => TotalUnits == 0
            ? 0m
            : Math.Round((decimal)OccupiedUnits / TotalUnits * 100m, 1);

        public decimal AreaUtilizationRate => TotalAreaSqFt == 0m
            ? 0m
            : Math.Round(LeasedAreaSqFt / TotalAreaSqFt * 100m, 1);

        public decimal RentRealisationRate => StandardAnnualRent == 0m
            ? 0m
            : Math.Round(ContractedAnnualRent / StandardAnnualRent * 100m, 1);

        /// <summary>Annual rent forgone on units that are not currently earning.</summary>
        public decimal RentGap => StandardAnnualRent - ContractedAnnualRent;

        public decimal RentPerSqFt => LeasedAreaSqFt == 0m
            ? 0m
            : Math.Round(ContractedAnnualRent / LeasedAreaSqFt, 2);
    }

    public class PropertyUtilizationReportDto
    {
        public DateTime AsOfDate { get; set; }
        public List<PropertyUtilizationRowDto> Rows { get; set; } = new();

        public int TotalUnits => Rows.Sum(x => x.TotalUnits);
        public int OccupiedUnits => Rows.Sum(x => x.OccupiedUnits);
        public int VacantUnits => Rows.Sum(x => x.VacantUnits);
        public int UnderMaintenanceUnits => Rows.Sum(x => x.UnderMaintenanceUnits);
        public int ReservedUnits => Rows.Sum(x => x.ReservedUnits);

        public decimal TotalAreaSqFt => Rows.Sum(x => x.TotalAreaSqFt);
        public decimal LeasedAreaSqFt => Rows.Sum(x => x.LeasedAreaSqFt);

        public decimal StandardAnnualRent => Rows.Sum(x => x.StandardAnnualRent);
        public decimal ContractedAnnualRent => Rows.Sum(x => x.ContractedAnnualRent);
        public decimal RentGap => StandardAnnualRent - ContractedAnnualRent;

        public int ActiveLeases => Rows.Sum(x => x.ActiveLeases);
        public int LeasesExpiringIn90Days => Rows.Sum(x => x.LeasesExpiringIn90Days);

        public decimal OccupancyRate => TotalUnits == 0
            ? 0m
            : Math.Round((decimal)OccupiedUnits / TotalUnits * 100m, 1);

        public decimal AreaUtilizationRate => TotalAreaSqFt == 0m
            ? 0m
            : Math.Round(LeasedAreaSqFt / TotalAreaSqFt * 100m, 1);

        public decimal RentRealisationRate => StandardAnnualRent == 0m
            ? 0m
            : Math.Round(ContractedAnnualRent / StandardAnnualRent * 100m, 1);
    }

    // =========================================================================
    //  DTOs - Tenant Health
    // =========================================================================

    public class TenantHealthRowDto
    {
        public int Rank { get; set; }

        public int TenantId { get; set; }
        public string TenantCode { get; set; } = string.Empty;
        public string TenantName { get; set; } = string.Empty;
        public bool IsActive { get; set; }

        // Receivables
        public decimal OpeningBalance { get; set; }
        public decimal TotalInvoiced { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal OutstandingBalance { get; set; }
        public decimal OverdueAmount { get; set; }
        public int OverdueInvoiceCount { get; set; }
        public int OldestOverdueDays { get; set; }
        public decimal? AverageDaysToPay { get; set; }

        // PDC behaviour
        public int PdcCount { get; set; }
        public int PdcClearedCount { get; set; }
        public decimal PdcClearedAmount { get; set; }
        public int PdcBouncedCount { get; set; }
        public decimal PdcBouncedAmount { get; set; }
        public decimal PdcOnHandAmount { get; set; }

        public decimal SecurityDepositHeld { get; set; }

        // Tenure
        public int LeaseCount { get; set; }
        public DateTime? FirstLeaseStart { get; set; }
        public decimal TenureYears { get; set; }
        public string? CurrentContractNumber { get; set; }
        public DateTime? CurrentContractEnd { get; set; }
        public int CurrentContractTermMonths { get; set; }
        public decimal CurrentAnnualRent { get; set; }
        public string? CurrentUnitDisplay { get; set; }

        // Score components (percentages, for transparency)
        public decimal CollectionRatio { get; set; }
        public decimal OverdueExposureRatio { get; set; }
        public decimal PdcReliabilityRatio { get; set; }

        public bool IsScored { get; set; }
        public decimal HealthScore { get; set; }
        public string HealthGrade { get; set; } = "N/A";

        /// <summary>Security deposit cover over the overdue balance.</summary>
        public decimal DepositCoverRatio => OverdueAmount <= 0m
            ? 0m
            : Math.Round(SecurityDepositHeld / OverdueAmount * 100m, 1);
    }

    // =========================================================================
    //  DTOs - Owner Withdrawals
    // =========================================================================

    public class OwnerWithdrawalLine
    {
        public int AccountId { get; set; }
        public DateTime EntryDate { get; set; }
        public string EntryNumber { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? CategoryId { get; set; }
        public string? CategoryCode { get; set; }
        public string? CategoryName { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    public class OwnerAccountColumnDto
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;

        /// <summary>Equity-natural balance at the start of the period (credit positive).</summary>
        public decimal OpeningBalance { get; set; }

        /// <summary>Debits in the period - money taken out by the owner.</summary>
        public decimal Withdrawals { get; set; }

        /// <summary>Credits in the period - capital or funds put in by the owner.</summary>
        public decimal Contributions { get; set; }

        public decimal ClosingBalance { get; set; }
    }

    public class OwnerWithdrawalCategoryRowDto
    {
        public int? CategoryId { get; set; }
        public string CategoryCode { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;

        public Dictionary<int, decimal> AmountsByAccount { get; set; } = new();
        public decimal Total { get; set; }

        public decimal AmountFor(int accountId) =>
            AmountsByAccount.TryGetValue(accountId, out var value) ? value : 0m;
    }

    public class OwnerWithdrawalReportDto
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool HasOwnerAccounts { get; set; }
        public bool HasUncategorisedActivity { get; set; }

        public List<OwnerAccountColumnDto> Accounts { get; set; } = new();
        public List<OwnerWithdrawalCategoryRowDto> Categories { get; set; } = new();
        public List<OwnerWithdrawalLine> Details { get; set; } = new();

        public decimal TotalOpeningBalance => Accounts.Sum(x => x.OpeningBalance);
        public decimal TotalWithdrawals => Accounts.Sum(x => x.Withdrawals);
        public decimal TotalContributions => Accounts.Sum(x => x.Contributions);
        public decimal TotalClosingBalance => Accounts.Sum(x => x.ClosingBalance);

        public decimal NetMovement => TotalWithdrawals - TotalContributions;

        public decimal CategorisedTotal => Categories
            .Where(x => x.CategoryId.HasValue)
            .Sum(x => x.Total);

        public decimal ColumnTotal(int accountId) => Categories.Sum(x => x.AmountFor(accountId));

        /// <summary>Share of a category in the total categorised spend, for the mix bar.</summary>
        public decimal ShareOfCategorised(decimal amount)
        {
            var total = Categories.Where(x => x.CategoryId.HasValue).Sum(x => Math.Max(0m, x.Total));
            return total <= 0m ? 0m : Math.Round(Math.Max(0m, amount) / total * 100m, 1);
        }
    }

    public class TenantHealthReportDto
    {
        public DateTime AsOfDate { get; set; }
        public List<TenantHealthRowDto> Rows { get; set; } = new();

        public decimal TotalOutstanding => Rows.Sum(x => x.OutstandingBalance);
        public decimal TotalOverdue => Rows.Sum(x => x.OverdueAmount);
        public decimal TotalInvoiced => Rows.Sum(x => x.TotalInvoiced);
        public decimal TotalCollected => Rows.Sum(x => x.TotalCollected);
        public decimal TotalDepositHeld => Rows.Sum(x => x.SecurityDepositHeld);

        public decimal PortfolioCollectionRatio => TotalInvoiced == 0m
            ? 0m
            : Math.Round(TotalCollected / TotalInvoiced * 100m, 1);

        public int ScoredTenants => Rows.Count(x => x.IsScored);

        public decimal AverageHealthScore => ScoredTenants == 0
            ? 0m
            : Math.Round(Rows.Where(x => x.IsScored).Average(x => x.HealthScore), 1);

        public int GradeCount(string grade) => Rows.Count(x => x.HealthGrade == grade);
    }
}
