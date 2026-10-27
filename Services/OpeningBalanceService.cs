using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Resolves the effective opening balance for any GL account.
    ///
    /// For non-control accounts the source is the manual <see cref="Account.OpeningBalance"/>.
    ///
    /// For control accounts (Accounts Receivable, Accounts Payable, Fixed Asset Cost,
    /// Accumulated Depreciation) the opening balance is computed from the
    /// underlying sub-ledger entities (Tenants, Vendors, FixedAssets) so that the
    /// GL value is guaranteed to reconcile with sub-ledger detail at all times.
    ///
    /// All values returned are expressed in NATURAL balance form
    /// (positive = debit balance for Asset/Expense accounts;
    ///  positive = credit balance for Liability/Equity/Revenue accounts).
    /// </summary>
    public class OpeningBalanceService
    {
        public const string SourceManual = "Manual";
        public const string SourceAccountsReceivable = "AccountsReceivable";
        public const string SourceAccountsPayable = "AccountsPayable";
        public const string SourceFixedAssetCost = "FixedAssetCost";
        public const string SourceFixedAssetAccumDepreciation = "FixedAssetAccumDepreciation";

        /// <summary>Balance is the derived contra of every other opening balance.</summary>
        public const string SourceOpeningBalanceControl = "OpeningBalanceControl";

        /// <summary>
        /// The opening balance suspense account. Every opening balance is implicitly
        /// contra'd here, so the opening position always nets to zero.
        /// </summary>
        public const string OpeningBalanceControlAccountCode = "39999";

        private readonly ApplicationDbContext _db;

        public OpeningBalanceService(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Returns the effective natural opening balance for a single account.
        /// Use this from any reporting service (TB, BS, GL) so that all reports
        /// stay in sync with sub-ledger detail.
        /// </summary>
        public async Task<decimal> GetEffectiveOpeningBalanceAsync(int accountId)
        {
            var snapshot = await BuildSnapshotAsync();
            return snapshot.GetEffectiveOpeningBalance(accountId);
        }

        /// <summary>
        /// Returns a per-account snapshot of opening balances suitable for use
        /// inside a single report (built once, queried many times).
        /// </summary>
        public async Task<OpeningBalanceSnapshot> BuildSnapshotAsync()
        {
            var accounts = await _db.Accounts
                .AsNoTracking()
                .ToListAsync();

            var settings = await _db.CompanyAccountSettings
                .AsNoTracking()
                .ToListAsync();

            var arAccountIds = settings
                .Where(x => x.AccountsReceivableAccountId.HasValue)
                .Select(x => x.AccountsReceivableAccountId!.Value)
                .ToHashSet();

            var apAccountIds = settings
                .Where(x => x.AccountsPayableAccountId.HasValue)
                .Select(x => x.AccountsPayableAccountId!.Value)
                .ToHashSet();

            var arTotal = await _db.Tenants
                .AsNoTracking()
                .SumAsync(x => (decimal?)x.OpeningBalance) ?? 0m;

            var apTotal = await _db.Vendors
                .AsNoTracking()
                .SumAsync(x => (decimal?)x.OpeningBalance) ?? 0m;

            var faCostByAccount = await _db.FixedAssets
                .AsNoTracking()
                .GroupBy(x => x.AssetAccountId)
                .Select(g => new { AccountId = g.Key, Total = g.Sum(x => x.OpeningCost) })
                .ToDictionaryAsync(x => x.AccountId, x => x.Total);

            var faAccumDepByAccount = await _db.FixedAssets
                .AsNoTracking()
                .GroupBy(x => x.AccumulatedDepreciationAccountId)
                .Select(g => new { AccountId = g.Key, Total = g.Sum(x => x.OpeningAccumulatedDepreciation) })
                .ToDictionaryAsync(x => x.AccountId, x => x.Total);

            var resolved = new Dictionary<int, OpeningBalanceAccountInfo>();

            foreach (var account in accounts)
            {
                var info = new OpeningBalanceAccountInfo
                {
                    AccountId = account.Id,
                    AccountCode = account.Code,
                    AccountName = account.Name,
                    AccountType = account.AccountType,
                    ManualOpeningBalance = account.OpeningBalance,
                    Source = SourceManual,
                    SubLedgerTotal = 0m
                };

                if (arAccountIds.Contains(account.Id))
                {
                    info.Source = SourceAccountsReceivable;
                    info.SubLedgerTotal = arTotal;
                }
                else if (apAccountIds.Contains(account.Id))
                {
                    info.Source = SourceAccountsPayable;
                    info.SubLedgerTotal = apTotal;
                }
                else if (faCostByAccount.TryGetValue(account.Id, out var faCost))
                {
                    info.Source = SourceFixedAssetCost;
                    info.SubLedgerTotal = faCost;
                }
                else if (faAccumDepByAccount.TryGetValue(account.Id, out var faDep))
                {
                    // Accumulated Depreciation is presented in the chart as an Asset
                    // (contra-asset) in this codebase. The sub-ledger sum is a
                    // positive number representing the credit-balance against cost,
                    // so we negate it to express as a debit-natural value.
                    info.Source = SourceFixedAssetAccumDepreciation;
                    info.SubLedgerTotal = -faDep;
                }

                resolved[account.Id] = info;
            }

            ApplyOpeningBalanceControl(accounts, resolved);

            return new OpeningBalanceSnapshot(resolved);
        }

        /// <summary>
        /// Gives account 39999 the balancing figure for every other opening balance, so
        /// the opening position always sums to zero and the Trial Balance and Balance
        /// Sheet are usable even while the migration is half done.
        ///
        /// This is the standard "opening balance equity" suspense account. It does not
        /// make the opening balances CORRECT - it makes the error visible and named
        /// instead of showing up as an unexplained difference on a report total. The
        /// account reaches zero only when every debit opening balance has a matching
        /// credit one, which is the signal that the migration is complete.
        ///
        /// The balance is derived, never stored, so it cannot drift out of step with the
        /// accounts it balances. Anything typed into the account's own OpeningBalance
        /// field is deliberately ignored.
        /// </summary>
        private static void ApplyOpeningBalanceControl(
            List<Account> accounts,
            Dictionary<int, OpeningBalanceAccountInfo> resolved)
        {
            var control = accounts.FirstOrDefault(x => x.Code == OpeningBalanceControlAccountCode);
            if (control == null)
                return;

            // Everything else expressed debit-positive. A positive total means the
            // opening balances carry more debits than credits.
            var debitPositiveTotal = 0m;

            foreach (var account in accounts)
            {
                if (account.Id == control.Id)
                    continue;

                if (!resolved.TryGetValue(account.Id, out var info))
                    continue;

                var natural = info.Source == SourceManual ? info.ManualOpeningBalance : info.SubLedgerTotal;

                debitPositiveTotal += IsDebitNature(account.AccountType) ? natural : -natural;
            }

            // 39999 is equity, so credit-natural: a positive natural balance is a credit,
            // which is exactly what an excess of opening debits needs.
            resolved[control.Id] = new OpeningBalanceAccountInfo
            {
                AccountId = control.Id,
                AccountCode = control.Code,
                AccountName = control.Name,
                AccountType = control.AccountType,
                Source = SourceOpeningBalanceControl,
                ManualOpeningBalance = 0m,
                SubLedgerTotal = debitPositiveTotal
            };
        }

        /// <summary>
        /// Builds the full reconciliation report shown on the Opening Balances page.
        /// Includes per-account effective opening balances, sub-ledger drill-down,
        /// and a balance-sheet style sanity check (sum of debit OB = sum of credit OB).
        /// </summary>
        public async Task<OpeningBalanceReconciliationViewModel> GetReconciliationAsync()
        {
            var snapshot = await BuildSnapshotAsync();

            // The control account is intentionally not postable, but it carries an
            // opening balance and must appear on this reconciliation - it is the line
            // that tells you whether the opening position is complete.
            var accounts = await _db.Accounts
                .AsNoTracking()
                .Where(x => (x.IsPostable || x.Code == OpeningBalanceControlAccountCode) && x.IsActive)
                .OrderBy(x => x.Code)
                .ToListAsync();

            var rows = new List<OpeningBalanceAccountRow>();

            foreach (var account in accounts)
            {
                if (!snapshot.TryGet(account.Id, out var info) || info == null)
                    continue;

                var effective = snapshot.GetEffectiveOpeningBalance(account.Id);

                rows.Add(new OpeningBalanceAccountRow
                {
                    AccountId = account.Id,
                    AccountCode = account.Code,
                    AccountName = account.Name,
                    AccountType = account.AccountType,
                    ReportingGroup = account.ReportingGroup,
                    Source = info.Source,
                    IsControlAccount = info.Source != SourceManual,
                    IsEditable = info.Source == SourceManual && account.Code != OpeningBalanceControlAccountCode,
                    ManualOpeningBalance = info.ManualOpeningBalance,
                    SubLedgerTotal = info.SubLedgerTotal,
                    EffectiveOpeningBalance = effective,
                    DebitOpeningBalance = effective > 0m && IsDebitNature(account.AccountType) ? effective
                        : effective < 0m && !IsDebitNature(account.AccountType) ? -effective
                        : 0m,
                    CreditOpeningBalance = effective > 0m && !IsDebitNature(account.AccountType) ? effective
                        : effective < 0m && IsDebitNature(account.AccountType) ? -effective
                        : 0m
                });
            }

            var arDetails = await _db.Tenants
                .AsNoTracking()
                .Where(x => x.OpeningBalance != 0m || x.IsActive)
                .OrderBy(x => x.Code)
                .Select(x => new OpeningBalanceSubLedgerRow
                {
                    EntityId = x.Id,
                    EntityCode = x.Code,
                    EntityName = x.FullName,
                    OpeningBalance = x.OpeningBalance
                })
                .ToListAsync();

            var apDetails = await _db.Vendors
                .AsNoTracking()
                .Where(x => x.OpeningBalance != 0m || x.IsActive)
                .OrderBy(x => x.Code)
                .Select(x => new OpeningBalanceSubLedgerRow
                {
                    EntityId = x.Id,
                    EntityCode = x.Code,
                    EntityName = x.Name,
                    OpeningBalance = x.OpeningBalance
                })
                .ToListAsync();

            var faDetails = await _db.FixedAssets
                .AsNoTracking()
                .Include(x => x.AssetAccount)
                .Include(x => x.AccumulatedDepreciationAccount)
                .OrderBy(x => x.AssetCode)
                .Select(x => new OpeningBalanceFixedAssetRow
                {
                    AssetId = x.Id,
                    AssetCode = x.AssetCode,
                    AssetName = x.Name,
                    OpeningCost = x.OpeningCost,
                    OpeningAccumulatedDepreciation = x.OpeningAccumulatedDepreciation,
                    OpeningNetBookValue = x.OpeningCost - x.OpeningAccumulatedDepreciation,
                    AssetAccountDisplay = x.AssetAccount != null ? $"{x.AssetAccount.Code} - {x.AssetAccount.Name}" : "-",
                    AccumulatedDepreciationAccountDisplay = x.AccumulatedDepreciationAccount != null
                        ? $"{x.AccumulatedDepreciationAccount.Code} - {x.AccumulatedDepreciationAccount.Name}"
                        : "-"
                })
                .ToListAsync();

            return new OpeningBalanceReconciliationViewModel
            {
                Rows = rows.OrderBy(x => x.AccountCode).ToList(),
                TenantOpeningBalances = arDetails,
                VendorOpeningBalances = apDetails,
                FixedAssetOpeningBalances = faDetails,
                TotalDebitOpeningBalance = rows.Sum(x => x.DebitOpeningBalance),
                TotalCreditOpeningBalance = rows.Sum(x => x.CreditOpeningBalance),
                TenantOpeningTotal = arDetails.Sum(x => x.OpeningBalance),
                VendorOpeningTotal = apDetails.Sum(x => x.OpeningBalance),
                FixedAssetOpeningCostTotal = faDetails.Sum(x => x.OpeningCost),
                FixedAssetOpeningAccumDepTotal = faDetails.Sum(x => x.OpeningAccumulatedDepreciation)
            };
        }

        internal static bool IsDebitNature(string accountType)
        {
            return accountType == "Asset" || accountType == "Expense";
        }
    }

    /// <summary>
    /// Per-account opening balance metadata kept inside <see cref="OpeningBalanceSnapshot"/>.
    /// </summary>
    public class OpeningBalanceAccountInfo
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public string Source { get; set; } = OpeningBalanceService.SourceManual;
        public decimal ManualOpeningBalance { get; set; }
        public decimal SubLedgerTotal { get; set; }
    }

    /// <summary>
    /// Pre-computed snapshot of every account's effective opening balance.
    /// Build it once per report and re-use across all rows for performance.
    /// </summary>
    public class OpeningBalanceSnapshot
    {
        private readonly Dictionary<int, OpeningBalanceAccountInfo> _byAccountId;

        public OpeningBalanceSnapshot(Dictionary<int, OpeningBalanceAccountInfo> byAccountId)
        {
            _byAccountId = byAccountId;
        }

        public bool TryGet(int accountId, out OpeningBalanceAccountInfo? info)
        {
            return _byAccountId.TryGetValue(accountId, out info);
        }

        public decimal GetEffectiveOpeningBalance(int accountId)
        {
            if (!_byAccountId.TryGetValue(accountId, out var info))
                return 0m;

            return info.Source == OpeningBalanceService.SourceManual
                ? info.ManualOpeningBalance
                : info.SubLedgerTotal;
        }
    }
}
