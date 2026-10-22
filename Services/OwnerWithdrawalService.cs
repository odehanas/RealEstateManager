using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Money the owner takes out of the business for their own use, recorded on its own
    /// screen. The owner's spending is not a business expense, so it never touches profit
    /// and loss:
    ///
    ///   Dr  Owner's Current Account (equity)   fixed - with the withdrawal category
    ///       Cr  the account it was paid from   chosen: bank, cash, petty cash, a card...
    ///
    /// The category is what turns one equity account into a report of where the owner's
    /// money went (Reporting ▸ Owner Withdrawals).
    /// </summary>
    public class OwnerWithdrawalService
    {
        public const string SourceType = "OwnerWithdrawal";

        private readonly ApplicationDbContext _db;
        private readonly ICurrentCompanyAccessor _currentCompany;
        private readonly AccountingPostingService _posting;
        private readonly JournalWorkflowService _workflow;

        public OwnerWithdrawalService(
            ApplicationDbContext db, ICurrentCompanyAccessor currentCompany,
            AccountingPostingService posting, JournalWorkflowService workflow)
        {
            _db = db;
            _currentCompany = currentCompany;
            _posting = posting;
            _workflow = workflow;
        }

        private int CompanyId => _currentCompany.CompanyId
            ?? throw new InvalidOperationException("No company is selected for the current user.");

        /// <summary>The owner equity accounts a withdrawal is charged to - flagged as owner accounts in the chart.</summary>
        public async Task<List<Account>> GetOwnerAccountsAsync() =>
            await _db.Accounts.AsNoTracking()
                .Where(x => x.IsOwnerAccount && x.IsPostable && x.IsActive && !x.IsGroup)
                .OrderBy(x => x.Code)
                .ToListAsync();

        /// <summary>Where the money can come from: any postable asset or liability account that is not an owner account.</summary>
        public async Task<List<Account>> GetPaidFromAccountsAsync() =>
            await _db.Accounts.AsNoTracking()
                .Where(x => !x.IsOwnerAccount && x.IsPostable && x.IsActive && !x.IsGroup &&
                            (x.AccountType == "Asset" || x.AccountType == "Liability"))
                .OrderBy(x => x.Code)
                .ToListAsync();

        public async Task<List<OwnerWithdrawalCategory>> GetCategoriesAsync() =>
            await _db.OwnerWithdrawalCategories.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
                .ToListAsync();

        /// <summary>
        /// Records and posts a withdrawal. Returns the journal id and, when posting was not
        /// possible, the reason (the journal is then kept as a draft).
        /// </summary>
        public async Task<(int JournalId, string? Warning)> RecordAsync(
            DateTime date, int ownerAccountId, int categoryId, decimal amount, int paidFromAccountId,
            string? description, string? reference)
        {
            var companyId = CompanyId;

            if (amount <= 0m)
                throw new InvalidOperationException("Enter the amount taken out.");
            if (date.Date > DateTime.Today)
                throw new InvalidOperationException("A withdrawal cannot be dated in the future.");

            var owners = await GetOwnerAccountsAsync();
            if (owners.Count == 0)
                throw new InvalidOperationException(
                    "No owner's account to charge. Flag the owner's current account as an owner account in System Setup ▸ Chart of Accounts.");
            var owner = owners.FirstOrDefault(x => x.Id == ownerAccountId)
                        ?? throw new InvalidOperationException("A withdrawal can only be charged to an owner's account.");
            var paidFrom = (await GetPaidFromAccountsAsync()).FirstOrDefault(x => x.Id == paidFromAccountId)
                           ?? throw new InvalidOperationException("Choose the account the money was paid from.");
            var category = (await GetCategoriesAsync()).FirstOrDefault(x => x.Id == categoryId)
                           ?? throw new InvalidOperationException("Choose what the money was spent on.");

            var text = string.IsNullOrWhiteSpace(description) ? category.Name : description.Trim();
            var lines = new List<JournalEntryLine>
            {
                new()
                {
                    AccountId = owner.Id,
                    Debit = Math.Round(amount, 2),
                    Description = text,
                    OwnerWithdrawalCategoryId = category.Id
                },
                new()
                {
                    AccountId = paidFrom.Id,
                    Credit = Math.Round(amount, 2),
                    Description = $"Owner withdrawal - {category.Name}"
                }
            };

            var journal = await _posting.CreatePreparedJournalAsync(companyId, date.Date,
                $"Owner withdrawal - {category.Name}: {text}", SourceType, category.Id, lines,
                string.IsNullOrWhiteSpace(reference) ? null : reference.Trim());

            var warning = await _workflow.PostNewEntryAsync(journal.Id);
            return (journal.Id, warning);
        }

        /// <summary>
        /// Every posted movement on the owner accounts in the period - withdrawals from this
        /// screen and any made by manual journal - with the account on the other side.
        /// </summary>
        public async Task<List<OwnerWithdrawalRegisterRow>> GetRegisterAsync(DateTime from, DateTime to)
        {
            var ownerIds = (await GetOwnerAccountsAsync()).Select(x => x.Id).ToList();

            var lines = await _db.JournalEntryLines.AsNoTracking()
                .Where(x => ownerIds.Contains(x.AccountId) &&
                            x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.JournalEntry.EntryDate >= from && x.JournalEntry.EntryDate <= to)
                .Select(x => new
                {
                    x.JournalEntryId,
                    x.JournalEntry!.EntryDate,
                    x.JournalEntry.EntryNumber,
                    x.JournalEntry.DocumentNumber,
                    x.JournalEntry.SourceType,
                    x.Description,
                    Category = x.OwnerWithdrawalCategory != null ? x.OwnerWithdrawalCategory.Name : null,
                    OwnerAccount = x.Account!.Code + " " + x.Account.Name,
                    x.Debit,
                    x.Credit
                })
                .ToListAsync();

            var journalIds = lines.Select(x => x.JournalEntryId).Distinct().ToList();
            var otherSides = await _db.JournalEntryLines.AsNoTracking()
                .Where(x => journalIds.Contains(x.JournalEntryId) && !ownerIds.Contains(x.AccountId))
                .Select(x => new { x.JournalEntryId, Account = x.Account!.Code + " " + x.Account.Name })
                .ToListAsync();

            return lines
                .OrderByDescending(x => x.EntryDate).ThenByDescending(x => x.JournalEntryId)
                .Select(x => new OwnerWithdrawalRegisterRow
                {
                    JournalEntryId = x.JournalEntryId,
                    Date = x.EntryDate,
                    EntryNumber = x.EntryNumber,
                    Reference = x.DocumentNumber,
                    RecordedHere = x.SourceType == SourceType,
                    Category = x.Category,
                    Description = x.Description,
                    OwnerAccount = x.OwnerAccount,
                    OtherAccount = string.Join(", ", otherSides.Where(o => o.JournalEntryId == x.JournalEntryId).Select(o => o.Account).Distinct()),
                    Withdrawn = x.Debit,
                    PutIn = x.Credit
                })
                .ToList();
        }
    }

    public class OwnerWithdrawalRegisterRow
    {
        public int JournalEntryId { get; set; }
        public DateTime Date { get; set; }
        public string EntryNumber { get; set; } = string.Empty;
        public string? Reference { get; set; }
        public bool RecordedHere { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
        public string OwnerAccount { get; set; } = string.Empty;
        public string OtherAccount { get; set; } = string.Empty;
        public decimal Withdrawn { get; set; }
        public decimal PutIn { get; set; }
    }
}
