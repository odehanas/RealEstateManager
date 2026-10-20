using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Buildings under development: their budgets and the costs collected in them.
    ///
    /// A project's cost is never kept as a running total of its own. It is read from the
    /// ledger: every posted line on the project's work-in-progress account that carries
    /// the project. Bills, manual journals and direct cost entries all tag their lines
    /// the same way, so whatever route a cost takes, it lands in the project once and
    /// the project always agrees with the balance sheet.
    /// </summary>
    public class DevelopmentProjectService
    {
        public const string CostSourceType = "DevelopmentCost";

        private readonly ApplicationDbContext _db;
        private readonly ICurrentCompanyAccessor _currentCompany;
        private readonly AccountingPostingService _posting;
        private readonly JournalWorkflowService _workflow;

        public DevelopmentProjectService(
            ApplicationDbContext db,
            ICurrentCompanyAccessor currentCompany,
            AccountingPostingService posting,
            JournalWorkflowService workflow)
        {
            _db = db;
            _currentCompany = currentCompany;
            _posting = posting;
            _workflow = workflow;
        }

        private int CompanyId => _currentCompany.CompanyId
            ?? throw new InvalidOperationException("No company is selected for the current user.");

        // =====================================================================
        //  READ
        // =====================================================================

        public async Task<DevelopmentProjectIndexViewModel> GetIndexAsync()
        {
            var projects = await _db.DevelopmentProjects.AsNoTracking()
                .Include(x => x.Property)
                .Include(x => x.WipAccount)
                .Include(x => x.BudgetLines)
                .OrderBy(x => x.Code)
                .ToListAsync();

            var costs = await PostedCostLines()
                .GroupBy(x => x.DevelopmentProjectId!.Value)
                .Select(g => new { ProjectId = g.Key, Amount = g.Sum(x => x.Debit - x.Credit) })
                .ToListAsync();

            var transferred = await _db.DevelopmentTransfers.AsNoTracking()
                .GroupBy(x => x.DevelopmentProjectId)
                .Select(g => new { ProjectId = g.Key, Amount = g.Sum(x => x.LandCost + x.BuildingCost) })
                .ToListAsync();

            var model = new DevelopmentProjectIndexViewModel
            {
                Projects = projects.Select(p => new DevelopmentProjectListRow
                {
                    Id = p.Id,
                    Code = p.Code,
                    Name = p.Name,
                    PropertyName = p.Property?.Name,
                    IntendedUse = p.IntendedUse,
                    Status = p.Status,
                    WipAccount = p.WipAccount == null ? "" : $"{p.WipAccount.Code} {p.WipAccount.Name}",
                    Budget = p.TotalBudget,
                    CostToDate = costs.FirstOrDefault(c => c.ProjectId == p.Id)?.Amount ?? 0m,
                    Transferred = transferred.FirstOrDefault(t => t.ProjectId == p.Id)?.Amount ?? 0m,
                    ExpectedCompletionDate = p.ExpectedCompletionDate,
                    CompletionDate = p.CompletionDate
                }).ToList()
            };

            // Development accounts: the three standard ones and any a project uses.
            var codes = new[] { "13100", "15500", "15600" };
            var projectAccountIds = projects.Select(x => x.WipAccountId).Distinct().ToList();

            model.Untagged = await _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.DevelopmentProjectId == null &&
                            x.Account != null &&
                            (codes.Contains(x.Account.Code) || projectAccountIds.Contains(x.AccountId)))
                .GroupBy(x => new { x.Account!.Code, x.Account.Name })
                .Select(g => new UntaggedDevelopmentBalance
                {
                    AccountCode = g.Key.Code,
                    AccountName = g.Key.Name,
                    Amount = g.Sum(x => x.Debit - x.Credit)
                })
                .Where(x => x.Amount != 0m)
                .ToListAsync();

            return model;
        }

        public async Task<DevelopmentProjectDetailsViewModel?> GetDetailsAsync(int id)
        {
            var project = await _db.DevelopmentProjects.AsNoTracking()
                .Include(x => x.Property)
                .Include(x => x.WipAccount)
                .Include(x => x.MainContractorVendor)
                .Include(x => x.BudgetLines)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (project == null)
                return null;

            var ledger = await PostedCostLines()
                .Where(x => x.DevelopmentProjectId == id)
                .OrderBy(x => x.JournalEntry!.EntryDate).ThenBy(x => x.JournalEntryId).ThenBy(x => x.Id)
                .Select(x => new DevelopmentCostLine
                {
                    Date = x.JournalEntry!.EntryDate,
                    JournalEntryId = x.JournalEntryId,
                    EntryNumber = x.JournalEntry.EntryNumber,
                    Source = x.JournalEntry.SourceType ?? "",
                    DocumentNumber = x.JournalEntry.DocumentNumber,
                    VendorName = x.Vendor != null ? x.Vendor.Name : null,
                    Category = x.DevelopmentCostCategory,
                    Description = x.Description,
                    Amount = x.Debit - x.Credit
                })
                .ToListAsync();

            var drafts = await _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.DevelopmentProjectId == id && x.JournalEntry != null && !x.JournalEntry.IsPosted &&
                            x.JournalEntry.SourceType != DevelopmentTransferService.SourceType)
                .Select(x => new { x.JournalEntryId, Amount = x.Debit - x.Credit })
                .ToListAsync();

            var categories = DevelopmentCostCategories.All
                .Select(c => new DevelopmentCategoryRow
                {
                    Category = c,
                    Budget = project.BudgetLines.Where(b => b.CostCategory == c).Sum(b => b.Amount),
                    Actual = ledger.Where(l => l.Category == c).Sum(l => l.Amount)
                })
                .ToList();

            // A tagged line with an unknown or missing category still belongs to the project.
            var uncategorised = ledger.Where(l => !DevelopmentCostCategories.All.Contains(l.Category)).Sum(l => l.Amount);
            if (uncategorised != 0m)
                categories.Add(new DevelopmentCategoryRow { Category = "", Actual = uncategorised });

            return new DevelopmentProjectDetailsViewModel
            {
                Project = project,
                Ledger = ledger,
                Categories = categories,
                Transferred = await _db.DevelopmentTransfers.AsNoTracking()
                    .Where(x => x.DevelopmentProjectId == id)
                    .SumAsync(x => (decimal?)(x.LandCost + x.BuildingCost)) ?? 0m,
                DraftCosts = drafts.Sum(x => x.Amount),
                DraftCount = drafts.Select(x => x.JournalEntryId).Distinct().Count()
            };
        }

        /// <summary>
        /// Projects a cost can be tagged to, for the "development cost" choice on bill and
        /// journal lines. A completed project still takes costs - retention releases and late
        /// invoices arrive after the completion certificate.
        /// </summary>
        public async Task<List<DevelopmentCostOption>> GetCostOptionsAsync()
        {
            return await DevelopmentCostOptions(_db);
        }

        public static async Task<List<DevelopmentCostOption>> DevelopmentCostOptions(ApplicationDbContext db)
        {
            return await db.DevelopmentProjects.AsNoTracking()
                .Where(x => x.Status != DevelopmentProjectStatuses.Cancelled)
                .OrderBy(x => x.Code)
                .Select(x => new DevelopmentCostOption
                {
                    ProjectId = x.Id,
                    ProjectLabel = x.Code + " " + x.Name + (x.Status == DevelopmentProjectStatuses.Completed ? " (completed)" : ""),
                    WipAccountId = x.WipAccountId,
                    PropertyId = x.PropertyId
                })
                .ToListAsync();
        }

        /// <summary>
        /// Shared by the bill and manual journal screens: the project and category a line's
        /// "project|category" value names, or an error message.
        /// </summary>
        public static async Task<(DevelopmentProject? Project, string? Category, string? Error)> ParseCostTagAsync(
            ApplicationDbContext db, string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return (null, null, null);

            var parts = tag.Split('|');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var projectId) ||
                !DevelopmentCostCategories.All.Contains(parts[1]))
                return (null, null, "the development cost chosen is not valid");

            var project = await db.DevelopmentProjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == projectId);
            if (project == null)
                return (null, null, "the development project was not found");
            if (!project.TakesCosts)
                return (null, null, $"{project.Code} {project.Name} was cancelled and takes no costs");

            return (project, parts[1], null);
        }

        public static string CostTag(int? projectId, string? category) =>
            projectId.HasValue && !string.IsNullOrWhiteSpace(category) ? $"{projectId}|{category}" : string.Empty;

        /// <summary>Postable asset accounts a project may collect its cost in.</summary>
        public async Task<List<Account>> GetWipAccountsAsync()
        {
            return await _db.Accounts.AsNoTracking()
                .Where(x => x.AccountType == "Asset" && x.IsPostable && x.IsActive && !x.IsGroup &&
                            (x.Code.StartsWith("13") || x.Code.StartsWith("15")))
                .OrderBy(x => x.Code)
                .ToListAsync();
        }

        /// <summary>Accounts a direct cost entry may credit: anything postable except the project's own.</summary>
        public async Task<List<Account>> GetCreditAccountsAsync()
        {
            return await _db.Accounts.AsNoTracking()
                .Where(x => x.IsPostable && x.IsActive && !x.IsGroup)
                .OrderBy(x => x.Code)
                .ToListAsync();
        }

        // =====================================================================
        //  WRITE
        // =====================================================================

        public async Task<DevelopmentProjectFormViewModel> BuildFormAsync(int? id)
        {
            if (id == null)
            {
                var wipCode = DevelopmentIntendedUses.DefaultWipAccountCode(DevelopmentIntendedUses.Sale);
                return new DevelopmentProjectFormViewModel
                {
                    WipAccountId = await _db.Accounts.Where(x => x.Code == wipCode).Select(x => x.Id).FirstOrDefaultAsync(),
                    Budget = DevelopmentCostCategories.All.Select(c => new DevelopmentBudgetInput { Category = c }).ToList()
                };
            }

            var project = await _db.DevelopmentProjects.AsNoTracking()
                .Include(x => x.BudgetLines)
                .FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("The development project was not found.");

            return new DevelopmentProjectFormViewModel
            {
                Id = project.Id,
                Code = project.Code,
                Name = project.Name,
                PropertyId = project.PropertyId,
                IntendedUse = project.IntendedUse,
                WipAccountId = project.WipAccountId,
                StartDate = project.StartDate,
                ExpectedCompletionDate = project.ExpectedCompletionDate,
                MainContractorVendorId = project.MainContractorVendorId,
                Notes = project.Notes,
                HasCosts = await HasCostsAsync(project.Id),
                Budget = DevelopmentCostCategories.All
                    .Select(c => new DevelopmentBudgetInput
                    {
                        Category = c,
                        Amount = project.BudgetLines.Where(b => b.CostCategory == c).Sum(b => b.Amount)
                    })
                    .ToList()
            };
        }

        public async Task<int> SaveAsync(DevelopmentProjectFormViewModel model)
        {
            var companyId = CompanyId;

            if (string.IsNullOrWhiteSpace(model.Name))
                throw new InvalidOperationException("Enter the project name.");
            if (!DevelopmentIntendedUses.All.Contains(model.IntendedUse))
                throw new InvalidOperationException("Choose what the building is for: sale, rental or own use.");
            if (model.Budget.Any(x => x.Amount < 0m))
                throw new InvalidOperationException("Budget amounts cannot be negative.");
            if (model.StartDate.HasValue && model.ExpectedCompletionDate.HasValue && model.ExpectedCompletionDate < model.StartDate)
                throw new InvalidOperationException("The expected completion cannot be before the start date.");

            var wipAccounts = await GetWipAccountsAsync();
            if (wipAccounts.All(x => x.Id != model.WipAccountId))
                throw new InvalidOperationException("Choose the asset account the project's cost is collected in.");

            if (model.PropertyId.HasValue && !await _db.Properties.AnyAsync(x => x.Id == model.PropertyId.Value))
                throw new InvalidOperationException("The property chosen was not found.");

            var name = model.Name.Trim();
            if (await _db.DevelopmentProjects.AnyAsync(x => x.Name == name && x.Id != model.Id))
                throw new InvalidOperationException($"A project named \"{name}\" already exists.");

            DevelopmentProject project;
            if (model.Id == 0)
            {
                project = new DevelopmentProject
                {
                    CompanyId = companyId,
                    Code = await NextCodeAsync(companyId),
                    Status = DevelopmentProjectStatuses.Planning,
                    CreatedAt = DateTime.UtcNow
                };
                _db.DevelopmentProjects.Add(project);
            }
            else
            {
                project = await _db.DevelopmentProjects.Include(x => x.BudgetLines).FirstOrDefaultAsync(x => x.Id == model.Id)
                          ?? throw new InvalidOperationException("The development project was not found.");

                // Costs already sit in the old account; moving the project would strand them.
                if (project.WipAccountId != model.WipAccountId && await HasCostsAsync(project.Id))
                    throw new InvalidOperationException(
                        "Costs are already posted to this project, so the account they are collected in cannot be changed.");
            }

            project.Name = name;
            project.PropertyId = model.PropertyId;
            project.IntendedUse = model.IntendedUse;
            project.WipAccountId = model.WipAccountId;
            project.StartDate = model.StartDate?.Date;
            project.ExpectedCompletionDate = model.ExpectedCompletionDate?.Date;
            project.MainContractorVendorId = model.MainContractorVendorId;
            project.Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim();

            foreach (var input in model.Budget.Where(x => DevelopmentCostCategories.All.Contains(x.Category)))
            {
                var line = project.BudgetLines.FirstOrDefault(x => x.CostCategory == input.Category);
                if (line == null && input.Amount != 0m)
                    project.BudgetLines.Add(new DevelopmentBudgetLine { CostCategory = input.Category, Amount = input.Amount });
                else if (line != null && input.Amount == 0m)
                    _db.DevelopmentBudgetLines.Remove(line);
                else if (line != null)
                    line.Amount = input.Amount;
            }

            await _db.SaveChangesAsync();
            return project.Id;
        }

        /// <summary>
        /// Records a cost that has no supplier bill and posts it at once. Returns the
        /// posting warning when the journal could not be posted (it is kept as a draft).
        /// </summary>
        public async Task<string?> RecordCostAsync(
            int projectId, DateTime costDate, string category, string description,
            decimal amount, int creditAccountId, string? reference)
        {
            var project = await _db.DevelopmentProjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == projectId)
                          ?? throw new InvalidOperationException("The development project was not found.");

            if (!project.TakesCosts)
                throw new InvalidOperationException("This project was cancelled and takes no costs.");
            if (amount <= 0m)
                throw new InvalidOperationException("Enter an amount greater than zero.");
            if (!DevelopmentCostCategories.All.Contains(category))
                throw new InvalidOperationException("Choose what the cost was for.");
            if (string.IsNullOrWhiteSpace(description))
                throw new InvalidOperationException("Describe the cost.");
            if (creditAccountId == project.WipAccountId)
                throw new InvalidOperationException("Choose the account the cost is paid from or moved from - not the project's own account.");
            if (!await _db.Accounts.AnyAsync(x => x.Id == creditAccountId && x.IsPostable && x.IsActive && !x.IsGroup))
                throw new InvalidOperationException("Choose a postable account to credit.");

            var journal = await _posting.PostDevelopmentCostAsync(
                project, costDate.Date, category, description.Trim(), Math.Round(amount, 2), creditAccountId,
                string.IsNullOrWhiteSpace(reference) ? null : reference.Trim());

            if (project.Status == DevelopmentProjectStatuses.Planning)
                await SetStatusInternalAsync(projectId, DevelopmentProjectStatuses.UnderConstruction, costDate.Date);

            return await _workflow.PostNewEntryAsync(journal.Id);
        }

        public async Task StartConstructionAsync(int projectId)
        {
            var project = await _db.DevelopmentProjects.FirstOrDefaultAsync(x => x.Id == projectId)
                          ?? throw new InvalidOperationException("The development project was not found.");
            if (project.Status != DevelopmentProjectStatuses.Planning)
                throw new InvalidOperationException("Only a project in planning can be started.");

            project.Status = DevelopmentProjectStatuses.UnderConstruction;
            project.StartDate ??= DateTime.Today;
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Records the completion certificate. The certificate number and date are copied
        /// to the property, where they drive the VAT zero-rating window. Transferring the
        /// cost out of work in progress is the next step and is not done here.
        /// </summary>
        public async Task CompleteAsync(int projectId, DateTime completionDate, string? certificateNumber)
        {
            var project = await _db.DevelopmentProjects.Include(x => x.Property).FirstOrDefaultAsync(x => x.Id == projectId)
                          ?? throw new InvalidOperationException("The development project was not found.");
            if (!project.IsOpen)
                throw new InvalidOperationException("This project is already closed.");
            if (completionDate.Date > DateTime.Today)
                throw new InvalidOperationException("The completion date cannot be in the future.");
            if (project.StartDate.HasValue && completionDate.Date < project.StartDate.Value)
                throw new InvalidOperationException("The completion date cannot be before the project started.");

            project.Status = DevelopmentProjectStatuses.Completed;
            project.CompletionDate = completionDate.Date;
            project.CompletionCertificateNumber = string.IsNullOrWhiteSpace(certificateNumber) ? null : certificateNumber.Trim();

            if (project.Property != null)
            {
                project.Property.CompletionDate = project.CompletionDate;
                project.Property.CompletionCertificateNumber = project.CompletionCertificateNumber ?? project.Property.CompletionCertificateNumber;
            }

            await _db.SaveChangesAsync();
        }

        /// <summary>A project can be cancelled only while nothing has been spent on it.</summary>
        public async Task CancelAsync(int projectId)
        {
            var project = await _db.DevelopmentProjects.FirstOrDefaultAsync(x => x.Id == projectId)
                          ?? throw new InvalidOperationException("The development project was not found.");
            if (!project.IsOpen)
                throw new InvalidOperationException("This project is already closed.");
            if (await HasCostsAsync(projectId) ||
                await _db.JournalEntryLines.AnyAsync(x => x.DevelopmentProjectId == projectId))
                throw new InvalidOperationException(
                    "Costs are recorded against this project. An abandoned project's costs must be written off " +
                    "with a journal before it is cancelled.");

            project.Status = DevelopmentProjectStatuses.Cancelled;
            await _db.SaveChangesAsync();
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        /// <summary>
        /// Posted lines that make up project cost: tagged, and on the project's own account.
        /// The completion transfer that empties work in progress is not cost - it is kept apart.
        /// </summary>
        private IQueryable<JournalEntryLine> PostedCostLines()
        {
            return _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.DevelopmentProjectId != null &&
                            x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.JournalEntry.SourceType != DevelopmentTransferService.SourceType &&
                            x.DevelopmentProject != null && x.AccountId == x.DevelopmentProject.WipAccountId);
        }

        private Task<bool> HasCostsAsync(int projectId) =>
            PostedCostLines().AnyAsync(x => x.DevelopmentProjectId == projectId);

        private async Task SetStatusInternalAsync(int projectId, string status, DateTime startDate)
        {
            var project = await _db.DevelopmentProjects.FirstAsync(x => x.Id == projectId);
            project.Status = status;
            project.StartDate ??= startDate;
            await _db.SaveChangesAsync();
        }

        private async Task<string> NextCodeAsync(int companyId)
        {
            var codes = await _db.DevelopmentProjects.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Code.StartsWith("DEV-"))
                .Select(x => x.Code)
                .ToListAsync();

            var next = codes.Select(x => int.TryParse(x[4..], out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
            return $"DEV-{next:D4}";
        }
    }
}
