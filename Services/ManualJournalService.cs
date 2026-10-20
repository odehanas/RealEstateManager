using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    public class ManualJournalService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentCompanyAccessor _currentCompanyAccessor;

        public ManualJournalService(
            ApplicationDbContext db,
            ICurrentCompanyAccessor currentCompanyAccessor)
        {
            _db = db;
            _currentCompanyAccessor = currentCompanyAccessor;
        }

        public async Task<int> CreateManualJournalAsync(ManualJournalEntryViewModel model)
        {
            var companyId = _currentCompanyAccessor.CompanyId
                ?? throw new InvalidOperationException("No company is selected for the current user.");

            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                throw new InvalidOperationException("No company record found.");

            var entryDate = model.EntryDate.Date;

            var period = await _db.AccountingPeriods.FirstOrDefaultAsync(x =>
                x.CompanyId == company.Id &&
                x.StartDate <= entryDate &&
                x.EndDate >= entryDate);

            if (period == null)
                throw new InvalidOperationException("No accounting period found for the selected date.");

            if (period.IsClosed)
                throw new InvalidOperationException("The accounting period is closed. Manual journal cannot be posted.");

            var validLines = model.Lines
                .Where(x => x.AccountId.HasValue && (x.Debit != 0m || x.Credit != 0m))
                .ToList();

            if (validLines.Count < 2)
                throw new InvalidOperationException("At least two journal lines are required.");

            foreach (var line in validLines)
            {
                if (line.Debit < 0 || line.Credit < 0)
                    throw new InvalidOperationException("Debit and credit must not be negative.");

                if (line.Debit > 0 && line.Credit > 0)
                    throw new InvalidOperationException("A journal line cannot have both debit and credit.");
            }

            // The owner expenditure dimension only belongs on owner equity accounts.
            // Posting it anywhere else would put personal spend on a company account.
            var categorisedAccountIds = validLines
                .Where(x => x.OwnerWithdrawalCategoryId.HasValue)
                .Select(x => x.AccountId!.Value)
                .Distinct()
                .ToList();

            if (categorisedAccountIds.Count > 0)
            {
                var nonOwnerAccounts = await _db.Accounts
                    .Where(x => categorisedAccountIds.Contains(x.Id) && !x.IsOwnerAccount)
                    .Select(x => x.Code + " - " + x.Name)
                    .ToListAsync();

                if (nonOwnerAccounts.Count > 0)
                {
                    throw new InvalidOperationException(
                        "An owner withdrawal category can only be used on an owner's current account. " +
                        "Remove the category from: " + string.Join(", ", nonOwnerAccounts) + ".");
                }

                var categoryIds = validLines
                    .Where(x => x.OwnerWithdrawalCategoryId.HasValue)
                    .Select(x => x.OwnerWithdrawalCategoryId!.Value)
                    .Distinct()
                    .ToList();

                var validCategoryCount = await _db.OwnerWithdrawalCategories
                    .CountAsync(x => categoryIds.Contains(x.Id));

                if (validCategoryCount != categoryIds.Count)
                    throw new InvalidOperationException("One or more selected owner withdrawal categories were not found.");
            }

            // Development costs: the project must take costs, and the line must sit on the
            // project's own account, or the project's cost would disagree with the ledger.
            var projectTags = new Dictionary<ManualJournalLineViewModel, (DevelopmentProject Project, string Category)>();
            for (var i = 0; i < validLines.Count; i++)
            {
                var (project, category, tagError) = await DevelopmentProjectService.ParseCostTagAsync(_db, validLines[i].DevelopmentCost);
                if (tagError != null)
                    throw new InvalidOperationException($"Line {i + 1}: {tagError}.");
                if (project == null)
                    continue;

                if (validLines[i].AccountId != project.WipAccountId)
                {
                    var wipCode = await _db.Accounts.Where(x => x.Id == project.WipAccountId).Select(x => x.Code + " " + x.Name).FirstOrDefaultAsync();
                    throw new InvalidOperationException(
                        $"Line {i + 1}: a cost for {project.Code} must be posted to the project's account {wipCode}.");
                }

                projectTags[validLines[i]] = (project, category!);
            }

            var totalDebit = validLines.Sum(x => x.Debit);
            var totalCredit = validLines.Sum(x => x.Credit);

            if (Math.Abs(totalDebit - totalCredit) != 0m)
                throw new InvalidOperationException("Journal entry is not balanced.");

            var entry = new JournalEntry
            {
                CompanyId = company.Id,
                AccountingPeriodId = period.Id,
                EntryNumber = await GenerateEntryNumberAsync(),
                EntryDate = entryDate,
                JournalType = "Manual",
                Status = "Draft",
                Description = model.Description,
                DocumentNumber = model.DocumentNumber,
                ReferenceNumber = model.ReferenceNumber,
                Remarks = model.Remarks,
                SourceType = "ManualJournal",
                IsPosted = false,
                CreatedAt = DateTime.UtcNow,
                PostedAt = null
            };

            foreach (var line in validLines)
            {
                int? propertyId = projectTags.TryGetValue(line, out var projectTag) ? projectTag.Project.PropertyId : null;

                if (line.UnitId.HasValue && propertyId == null)
                {
                    propertyId = await _db.Units
                        .Where(x => x.Id == line.UnitId.Value)
                        .Select(x => (int?)x.PropertyId)
                        .FirstOrDefaultAsync();
                }

                entry.Lines.Add(new JournalEntryLine
                {
                    AccountId = line.AccountId!.Value,
                    Description = line.LineDescription,
                    Debit = line.Debit,
                    Credit = line.Credit,
                    PropertyId = propertyId,
                    UnitId = line.UnitId,
                    TenantId = line.TenantId,
                    VendorId = line.VendorId,
                    LeaseId = line.LeaseId,
                    OwnerWithdrawalCategoryId = line.OwnerWithdrawalCategoryId,
                    DevelopmentProjectId = projectTags.TryGetValue(line, out var tag) ? tag.Project.Id : null,
                    DevelopmentCostCategory = projectTags.TryGetValue(line, out var tag2) ? tag2.Category : null
                });
            }

            _db.JournalEntries.Add(entry);
            await _db.SaveChangesAsync();

            return entry.Id;
        }

        private async Task<string> GenerateEntryNumberAsync()
        {
            var count = await _db.JournalEntries.CountAsync() + 1;
            return $"MAN-{DateTime.UtcNow:yyyyMMdd}-{count:D6}";
        }
    }
}
