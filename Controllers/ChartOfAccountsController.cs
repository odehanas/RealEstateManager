using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class ChartOfAccountsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly OpeningBalanceService _openingBalanceService;

        public ChartOfAccountsController(ApplicationDbContext db, OpeningBalanceService openingBalanceService)
        {
            _db = db;
            _openingBalanceService = openingBalanceService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var accounts = await _db.Accounts
                .OrderBy(x => x.SortOrder == 0 ? int.MaxValue : x.SortOrder)
                .ThenBy(x => x.Code)
                .ToListAsync();

            var snapshot = await _openingBalanceService.BuildSnapshotAsync();

            var rootAccounts = accounts
                .Where(x => x.ParentAccountId == null)
                .OrderBy(x => x.SortOrder == 0 ? int.MaxValue : x.SortOrder)
                .ThenBy(x => x.Code)
                .Select(x => BuildTree(x, accounts, snapshot))
                .ToList();

            var model = new ChartOfAccountsIndexViewModel
            {
                Accounts = rootAccounts,
                OpeningBalances = await BuildOpeningBalanceSummaryAsync()
            };

            return View(model);
        }

        /// <summary>
        /// Opening balance position for the banner at the top of the tree.
        ///
        /// Built from the same reconciliation the Opening Balances screen uses, rather
        /// than recomputed here - two screens disagreeing about whether the books
        /// balance would be worse than not showing it at all.
        /// </summary>
        private async Task<OpeningBalanceSummaryViewModel> BuildOpeningBalanceSummaryAsync()
        {
            var reconciliation = await _openingBalanceService.GetReconciliationAsync();

            const string controlCode = OpeningBalanceService.OpeningBalanceControlAccountCode;

            decimal NaturalTotalFor(string accountType) => reconciliation.Rows
                .Where(x => x.AccountType == accountType)
                .Sum(x => x.EffectiveOpeningBalance);

            var controlRow = reconciliation.Rows.FirstOrDefault(x => x.AccountCode == controlCode);

            return new OpeningBalanceSummaryViewModel
            {
                TotalDebit = reconciliation.TotalDebitOpeningBalance,
                TotalCredit = reconciliation.TotalCreditOpeningBalance,
                Assets = NaturalTotalFor("Asset"),
                Liabilities = NaturalTotalFor("Liability"),
                Equity = NaturalTotalFor("Equity"),
                Revenue = NaturalTotalFor("Revenue"),
                Expenses = NaturalTotalFor("Expense"),

                HasControlAccount = controlRow != null,
                ControlAccountBalance = controlRow?.EffectiveOpeningBalance ?? 0m,
                EquityExcludingControl = reconciliation.Rows
                    .Where(x => x.AccountType == "Equity" && x.AccountCode != controlCode)
                    .Sum(x => x.EffectiveOpeningBalance),

                // The control account is not something the user entered, so it does not
                // count towards "accounts with an opening balance".
                AccountsWithOpeningBalance = reconciliation.Rows
                    .Count(x => x.EffectiveOpeningBalance != 0m && x.AccountCode != controlCode)
            };
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await LoadParentAccountsAsync();
            return View(new AccountFormViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AccountFormViewModel model)
        {
            await LoadParentAccountsAsync();

            ValidateAccountForm(model);

            if (await _db.Accounts.AnyAsync(x => x.Code == model.Code))
            {
                ModelState.AddModelError(nameof(model.Code), "Account number already exists.");
            }

            if (model.ParentAccountId.HasValue)
            {
                var parent = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == model.ParentAccountId.Value);
                if (parent == null)
                {
                    ModelState.AddModelError(nameof(model.ParentAccountId), "Parent account was not found.");
                }
                else
                {
                    if (!string.Equals(parent.AccountType, model.AccountType, StringComparison.OrdinalIgnoreCase))
                    {
                        ModelState.AddModelError(nameof(model.ParentAccountId), "Parent account must be of the same account type.");
                    }

                    if (!string.Equals(parent.ReportingGroup, model.ReportingGroup, StringComparison.OrdinalIgnoreCase))
                    {
                        ModelState.AddModelError(nameof(model.ParentAccountId), "Parent account must be of the same reporting group.");
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var account = new Account
            {
                // Accounts are company-scoped. Without this the row is inserted with
                // CompanyId 0 and SQL Server rejects it on FK_Accounts_Companies_CompanyId.
                CompanyId = User.GetRequiredCurrentCompanyId(),
                Code = model.Code.Trim(),
                Name = model.Name.Trim(),
                AccountType = model.AccountType,
                ReportingGroup = model.ReportingGroup,
                StatementLine = CleanStatementLine(model),
                ParentAccountId = model.ParentAccountId,
                IsGroup = model.IsGroup,
                IsPostable = model.IsPostable,
                IsActive = model.IsActive,
                SortOrder = model.SortOrder,
                // Only a postable equity account can act as an owner's current account.
                IsOwnerAccount = model.IsOwnerAccount && model.AccountType == "Equity" && !model.IsGroup && model.IsPostable,
                // Only a postable asset account can hold a petty cash float.
                IsPettyCashAccount = model.IsPettyCashAccount && model.AccountType == "Asset" && !model.IsGroup && model.IsPostable,
                // Group accounts and not-postable accounts cannot carry an opening balance.
                OpeningBalance = (model.IsGroup || !model.IsPostable) ? 0m : model.OpeningBalance
            };

            _db.Accounts.Add(account);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var account = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == id);
            if (account == null)
            {
                return NotFound();
            }

            await LoadParentAccountsAsync(id);

            var snapshot = await _openingBalanceService.BuildSnapshotAsync();
            snapshot.TryGet(account.Id, out var openingInfo);

            var isControl = openingInfo != null && openingInfo.Source != OpeningBalanceService.SourceManual;

            var model = new AccountFormViewModel
            {
                Id = account.Id,
                Code = account.Code,
                Name = account.Name,
                AccountType = account.AccountType,
                ReportingGroup = account.ReportingGroup,
                StatementLine = account.StatementLine,
                InheritedStatementLine = await StatementLineResolver.InheritedAsync(_db, account),
                ParentAccountId = account.ParentAccountId,
                IsGroup = account.IsGroup,
                IsPostable = account.IsPostable,
                IsActive = account.IsActive,
                IsOwnerAccount = account.IsOwnerAccount,
                IsPettyCashAccount = account.IsPettyCashAccount,
                SortOrder = account.SortOrder,
                OpeningBalance = isControl
                    ? (openingInfo?.SubLedgerTotal ?? 0m)
                    : account.OpeningBalance,
                IsOpeningBalanceReadOnly = isControl,
                OpeningBalanceSource = openingInfo?.Source ?? OpeningBalanceService.SourceManual,
                OpeningBalanceFromSubLedger = openingInfo?.SubLedgerTotal ?? 0m
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(AccountFormViewModel model)
        {
            if (!model.Id.HasValue)
            {
                return BadRequest();
            }

            await LoadParentAccountsAsync(model.Id.Value);

            ValidateAccountForm(model);

            var account = await _db.Accounts
                .Include(x => x.Children)
                .FirstOrDefaultAsync(x => x.Id == model.Id.Value);

            if (account == null)
            {
                return NotFound();
            }

            if (await _db.Accounts.AnyAsync(x => x.Code == model.Code && x.Id != model.Id.Value))
            {
                ModelState.AddModelError(nameof(model.Code), "Account number already exists.");
            }

            if (model.ParentAccountId.HasValue)
            {
                if (model.ParentAccountId.Value == model.Id.Value)
                {
                    ModelState.AddModelError(nameof(model.ParentAccountId), "Account cannot be its own parent.");
                }
                else
                {
                    var parent = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == model.ParentAccountId.Value);
                    if (parent == null)
                    {
                        ModelState.AddModelError(nameof(model.ParentAccountId), "Parent account was not found.");
                    }
                    else
                    {
                        if (!string.Equals(parent.AccountType, model.AccountType, StringComparison.OrdinalIgnoreCase))
                        {
                            ModelState.AddModelError(nameof(model.ParentAccountId), "Parent account must be of the same account type.");
                        }

                        if (!string.Equals(parent.ReportingGroup, model.ReportingGroup, StringComparison.OrdinalIgnoreCase))
                        {
                            ModelState.AddModelError(nameof(model.ParentAccountId), "Parent account must be of the same reporting group.");
                        }
                    }
                }
            }

            if (account.Children.Any() && model.IsPostable)
            {
                ModelState.AddModelError(nameof(model.IsPostable), "An account with child accounts should not be postable.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            account.Code = model.Code.Trim();
            account.Name = model.Name.Trim();
            account.AccountType = model.AccountType;
            account.ReportingGroup = model.ReportingGroup;
            account.StatementLine = CleanStatementLine(model);
            account.ParentAccountId = model.ParentAccountId;
            account.IsGroup = model.IsGroup;
            account.IsPostable = model.IsPostable;
            account.IsActive = model.IsActive;
            account.IsOwnerAccount = model.IsOwnerAccount && model.AccountType == "Equity" && !model.IsGroup && model.IsPostable;
            account.IsPettyCashAccount = model.IsPettyCashAccount && model.AccountType == "Asset" && !model.IsGroup && model.IsPostable;
            account.SortOrder = model.SortOrder;

            // Sub-ledger control accounts (AR / AP / Fixed Asset) keep the
            // database value at zero so it can never drift from sub-ledger
            // detail. Group / not-postable accounts also cannot carry OB.
            var snapshot = await _openingBalanceService.BuildSnapshotAsync();
            var isControl = snapshot.TryGet(account.Id, out var info)
                && info != null
                && info.Source != OpeningBalanceService.SourceManual;

            // The opening balance control account's balance is derived from every other
            // account, so a figure typed here would be ignored by the reports. Zero it
            // rather than leave a misleading value on the record.
            var isOpeningBalanceControl = account.Code == OpeningBalanceService.OpeningBalanceControlAccountCode;

            account.OpeningBalance = (account.IsGroup || !account.IsPostable || isControl || isOpeningBalanceControl)
                ? 0m
                : model.OpeningBalance;

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        /// <summary>Only revenue and expense accounts carry an income statement line, and only a known one on the right side.</summary>
        private static string? CleanStatementLine(AccountFormViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.StatementLine))
                return null;
            return model.AccountType switch
            {
                "Revenue" when StatementLines.Revenue.Contains(model.StatementLine) => model.StatementLine,
                "Expense" when StatementLines.Costs.Contains(model.StatementLine) => model.StatementLine,
                _ => null
            };
        }

        private void ValidateAccountForm(AccountFormViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
            {
                ModelState.AddModelError(nameof(model.Code), "Account number is required.");
            }

            if (string.IsNullOrWhiteSpace(model.Name))
            {
                ModelState.AddModelError(nameof(model.Name), "Account name is required.");
            }

            if (string.IsNullOrWhiteSpace(model.AccountType))
            {
                ModelState.AddModelError(nameof(model.AccountType), "Account type is required.");
            }

            if (string.IsNullOrWhiteSpace(model.ReportingGroup))
            {
                ModelState.AddModelError(nameof(model.ReportingGroup), "Reporting group is required.");
            }

            if (model.IsGroup && model.IsPostable)
            {
                ModelState.AddModelError(nameof(model.IsPostable), "Group account cannot be postable.");
            }

            if (!model.IsGroup && !model.IsPostable)
            {
                ModelState.AddModelError(nameof(model.IsPostable), "Non-group account should normally be postable.");
            }
        }

        private async Task LoadParentAccountsAsync(int? currentAccountId = null)
        {
            var parents = await _db.Accounts
                .Where(x => !currentAccountId.HasValue || x.Id != currentAccountId.Value)
                .OrderBy(x => x.SortOrder == 0 ? int.MaxValue : x.SortOrder)
                .ThenBy(x => x.Code)
                .ToListAsync();

            ViewBag.ParentAccounts = parents;
        }

        private static AccountTreeViewModel BuildTree(Account account, List<Account> allAccounts, OpeningBalanceSnapshot snapshot)
        {
            var openingBalance = snapshot.GetEffectiveOpeningBalance(account.Id);
            snapshot.TryGet(account.Id, out var info);

            return new AccountTreeViewModel
            {
                Id = account.Id,
                Code = account.Code,
                Name = account.Name,
                AccountType = account.AccountType,
                ReportingGroup = account.ReportingGroup,
                IsGroup = account.IsGroup,
                IsPostable = account.IsPostable,
                IsActive = account.IsActive,
                SortOrder = account.SortOrder,
                OpeningBalance = openingBalance,
                OpeningBalanceSource = info?.Source ?? OpeningBalanceService.SourceManual,
                IsControlAccount = info != null && info.Source != OpeningBalanceService.SourceManual,
                Children = allAccounts
                    .Where(x => x.ParentAccountId == account.Id)
                    .OrderBy(x => x.SortOrder == 0 ? int.MaxValue : x.SortOrder)
                    .ThenBy(x => x.Code)
                    .Select(x => BuildTree(x, allAccounts, snapshot))
                    .ToList()
            };
        }
    }
}
