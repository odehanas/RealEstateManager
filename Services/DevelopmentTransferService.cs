using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The completion transfer: a finished project's cost leaves work in progress and is
    /// shared between the building's units by area. Each unit goes where its use says -
    /// completed units held for sale (inventory), investment property, or fixed assets.
    ///
    /// Land is kept apart from the building throughout. It is never depreciated; the
    /// building part of rental and own-use units is entered in the fixed asset register
    /// with the useful life given, and depreciated by the monthly depreciation run - the
    /// cost model for investment property (IAS 40) and IAS 16 for own use.
    /// </summary>
    public class DevelopmentTransferService
    {
        public const string SourceType = "DevelopmentTransfer";

        private readonly ApplicationDbContext _db;
        private readonly ICurrentCompanyAccessor _currentCompany;
        private readonly AccountingPostingService _posting;
        private readonly JournalWorkflowService _workflow;
        private readonly FixedAssetService _fixedAssets;

        public DevelopmentTransferService(
            ApplicationDbContext db,
            ICurrentCompanyAccessor currentCompany,
            AccountingPostingService posting,
            JournalWorkflowService workflow,
            FixedAssetService fixedAssets)
        {
            _db = db;
            _currentCompany = currentCompany;
            _posting = posting;
            _workflow = workflow;
            _fixedAssets = fixedAssets;
        }

        private int CompanyId => _currentCompany.CompanyId
            ?? throw new InvalidOperationException("No company is selected for the current user.");

        /// <summary>What is still in work in progress for the project, split into land and building.</summary>
        public async Task<(decimal Land, decimal Building)> GetRemainingAsync(int projectId)
        {
            var project = await _db.DevelopmentProjects.AsNoTracking().FirstAsync(x => x.Id == projectId);

            var lines = await _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.DevelopmentProjectId == projectId &&
                            x.AccountId == project.WipAccountId &&
                            x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.JournalEntry.SourceType != SourceType)
                .Select(x => new { x.DevelopmentCostCategory, Amount = x.Debit - x.Credit })
                .ToListAsync();

            var transferred = await _db.DevelopmentTransfers.AsNoTracking()
                .Where(x => x.DevelopmentProjectId == projectId)
                .Select(x => new { x.LandCost, x.BuildingCost })
                .ToListAsync();

            var land = lines.Where(x => x.DevelopmentCostCategory == DevelopmentCostCategories.Land).Sum(x => x.Amount)
                       - transferred.Sum(x => x.LandCost);
            var total = lines.Sum(x => x.Amount) - transferred.Sum(x => x.LandCost + x.BuildingCost);

            return (land, total - land);
        }

        /// <summary>The units the cost is shared between: the property's active units with an area.</summary>
        public async Task<List<Unit>> GetUnitsAsync(int projectId)
        {
            var propertyId = await _db.DevelopmentProjects.AsNoTracking()
                .Where(x => x.Id == projectId).Select(x => x.PropertyId).FirstOrDefaultAsync();
            if (propertyId == null)
                return new List<Unit>();

            return await _db.Units.AsNoTracking()
                .Where(x => x.PropertyId == propertyId.Value && x.IsActive)
                .OrderBy(x => x.UnitNumber)
                .ToListAsync();
        }

        public async Task<List<DevelopmentTransfer>> GetTransfersAsync(int projectId)
        {
            return await _db.DevelopmentTransfers.AsNoTracking()
                .Include(x => x.Lines).ThenInclude(x => x.Unit)
                .Include(x => x.JournalEntry)
                .Include(x => x.RentalFixedAsset)
                .Include(x => x.OwnUseFixedAsset)
                .Where(x => x.DevelopmentProjectId == projectId)
                .OrderBy(x => x.TransferDate).ThenBy(x => x.Id)
                .ToListAsync();
        }

        /// <summary>Shares an amount by area; the last unit takes the rounding so the parts add up.</summary>
        public static List<decimal> ShareByArea(decimal amount, IReadOnlyList<decimal> areas)
        {
            var total = areas.Sum();
            var shares = new List<decimal>();
            var allocated = 0m;
            for (var i = 0; i < areas.Count; i++)
            {
                var share = i == areas.Count - 1
                    ? amount - allocated
                    : Math.Round(amount * areas[i] / total, 2, MidpointRounding.AwayFromZero);
                allocated += share;
                shares.Add(share);
            }
            return shares;
        }

        /// <summary>
        /// Moves what is left in work in progress and posts it. Uses maps unit id to Sale,
        /// Rental or OwnUse. Returns the transfer.
        /// </summary>
        public async Task<DevelopmentTransfer> TransferAsync(
            int projectId, DateTime transferDate, int buildingUsefulLifeYears,
            IDictionary<int, string> uses, string? userName)
        {
            var companyId = CompanyId;
            var project = await _db.DevelopmentProjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == projectId)
                          ?? throw new InvalidOperationException("The development project was not found.");

            if (project.Status != DevelopmentProjectStatuses.Completed || project.CompletionDate == null)
                throw new InvalidOperationException("Record the completion certificate first - only a completed project is transferred.");
            if (transferDate.Date < project.CompletionDate.Value)
                throw new InvalidOperationException("The transfer cannot be dated before the completion date.");
            if (transferDate.Date > DateTime.Today)
                throw new InvalidOperationException("The transfer cannot be dated in the future.");

            var drafts = await _db.JournalEntryLines.CountAsync(x => x.DevelopmentProjectId == projectId &&
                                                                     x.JournalEntry != null && !x.JournalEntry.IsPosted);
            if (drafts > 0)
                throw new InvalidOperationException(
                    "Some costs tagged to this project are still drafts. Post them (or delete them) first, so the whole cost is transferred.");

            var (land, building) = await GetRemainingAsync(projectId);
            if (land + building <= 0m)
                throw new InvalidOperationException("Nothing is left in work in progress for this project.");

            var units = (await GetUnitsAsync(projectId)).Where(x => x.AreaSqFt > 0m).ToList();
            if (units.Count == 0)
                throw new InvalidOperationException(
                    "The cost is shared between the building's units by area. Add the units, with their areas, to the project's property first.");

            foreach (var unit in units)
            {
                if (!uses.TryGetValue(unit.Id, out var use) || !DevelopmentIntendedUses.All.Contains(use))
                    throw new InvalidOperationException($"Choose what unit {unit.UnitNumber} is for.");
            }

            var needsDepreciation = units.Any(x => uses[x.Id] != DevelopmentIntendedUses.Sale);
            if (needsDepreciation && (buildingUsefulLifeYears < 1 || buildingUsefulLifeYears > 100))
                throw new InvalidOperationException("Enter the building's useful life in years (1 to 100) for the rental or own-use units.");

            var accounts = await _db.Accounts.AsNoTracking()
                .Where(x => new[] { "13200", "15100", "15150", "15200", "15300", "62100" }.Contains(x.Code))
                .ToDictionaryAsync(x => x.Code, x => x.Id);
            foreach (var code in new[] { "13200", "15100", "15150", "15200", "15300", "62100" })
            {
                if (!accounts.ContainsKey(code))
                    throw new InvalidOperationException($"Account {code} is missing from the chart of accounts. Restart the application once so it is added.");
            }

            // ---- Allocation -------------------------------------------------------
            var areas = units.Select(x => x.AreaSqFt).ToList();
            var landShares = ShareByArea(land, areas);
            var buildingShares = ShareByArea(building, areas);

            var transfer = new DevelopmentTransfer
            {
                CompanyId = companyId,
                DevelopmentProjectId = projectId,
                TransferDate = transferDate.Date,
                LandCost = land,
                BuildingCost = building,
                BuildingUsefulLifeYears = needsDepreciation ? buildingUsefulLifeYears : 0,
                CreatedAt = DateTime.UtcNow,
                CreatedByName = userName
            };
            for (var i = 0; i < units.Count; i++)
            {
                transfer.Lines.Add(new DevelopmentTransferLine
                {
                    UnitId = units[i].Id,
                    IntendedUse = uses[units[i].Id],
                    AreaSqFt = units[i].AreaSqFt,
                    LandCost = landShares[i],
                    BuildingCost = buildingShares[i]
                });
            }

            _db.DevelopmentTransfers.Add(transfer);
            await _db.SaveChangesAsync();

            // ---- Journal ----------------------------------------------------------
            var journalLines = new List<JournalEntryLine>();

            foreach (var line in transfer.Lines.Where(x => x.IntendedUse == DevelopmentIntendedUses.Sale))
            {
                var unit = units.First(u => u.Id == line.UnitId);
                journalLines.Add(new JournalEntryLine
                {
                    AccountId = accounts["13200"],
                    Debit = line.TotalCost,
                    Description = $"Unit {unit.UnitNumber} held for sale - cost on completion",
                    PropertyId = project.PropertyId,
                    UnitId = unit.Id
                });
            }

            void AddLandAndBuilding(string use, string accountCode, string label)
            {
                var lines = transfer.Lines.Where(x => x.IntendedUse == use).ToList();
                if (lines.Sum(x => x.LandCost) != 0m)
                    journalLines.Add(new JournalEntryLine
                    {
                        AccountId = accounts[accountCode],
                        Debit = lines.Sum(x => x.LandCost),
                        Description = $"{label} - land ({lines.Count} unit(s)), not depreciated",
                        PropertyId = project.PropertyId
                    });
                if (lines.Sum(x => x.BuildingCost) != 0m)
                    journalLines.Add(new JournalEntryLine
                    {
                        AccountId = accounts[accountCode],
                        Debit = lines.Sum(x => x.BuildingCost),
                        Description = $"{label} - building ({lines.Count} unit(s))",
                        PropertyId = project.PropertyId
                    });
            }

            AddLandAndBuilding(DevelopmentIntendedUses.Rental, "15100", "Investment property");
            AddLandAndBuilding(DevelopmentIntendedUses.OwnUse, "15200", "Own-use property");

            journalLines.Add(new JournalEntryLine
            {
                AccountId = project.WipAccountId,
                Credit = land + building,
                Description = $"{project.Code} cost transferred out of work in progress on completion",
                PropertyId = project.PropertyId,
                DevelopmentProjectId = project.Id
            });

            // A negative share (a credit-heavy category) cannot be a debit line; keep the journal clean.
            foreach (var line in journalLines.Where(x => x.Debit < 0m))
            {
                line.Credit = -line.Debit;
                line.Debit = 0m;
            }

            journalLines.RemoveAll(x => x.Debit == 0m && x.Credit == 0m);

            var journal = await _posting.CreateDevelopmentTransferJournalAsync(transfer, project, journalLines);
            transfer.JournalEntryId = journal.Id;
            await _db.SaveChangesAsync();

            var warning = await _workflow.PostNewEntryAsync(journal.Id);
            if (warning != null)
            {
                // Nothing may be left half done: no transfer without its posted journal.
                var draft = await _db.JournalEntries.Include(x => x.Lines).FirstAsync(x => x.Id == journal.Id);
                transfer.JournalEntryId = null;
                await _db.SaveChangesAsync();
                _db.JournalEntryLines.RemoveRange(draft.Lines);
                _db.JournalEntries.Remove(draft);
                _db.DevelopmentTransfers.Remove(transfer);
                await _db.SaveChangesAsync();
                throw new InvalidOperationException($"The transfer could not be posted: {warning}");
            }

            // ---- Buildings into the fixed asset register, to be depreciated --------
            var sequence = await _db.DevelopmentTransfers.CountAsync(x => x.DevelopmentProjectId == projectId);

            async Task<int?> RegisterBuildingAsync(string use, string assetCode, string assetAccount, string accumulatedAccount, string category)
            {
                var cost = transfer.Lines.Where(x => x.IntendedUse == use).Sum(x => x.BuildingCost);
                if (cost <= 0m)
                    return null;

                var asset = new FixedAsset
                {
                    CompanyId = companyId,
                    PropertyId = project.PropertyId,
                    AssetCode = $"{project.Code}-{assetCode}{sequence}",
                    Name = $"{project.Name} - building ({DevelopmentIntendedUses.Describe(use)})",
                    AssetCategory = category,
                    AcquisitionDate = transfer.TransferDate,
                    InServiceDate = transfer.TransferDate,
                    CapitalizationDate = transfer.TransferDate,
                    Cost = cost,
                    ResidualValue = 0m,
                    UsefulLifeMonths = buildingUsefulLifeYears * 12,
                    DepreciationMethod = "StraightLine",
                    Status = "Active",
                    AssetAccountId = accounts[assetAccount],
                    AccumulatedDepreciationAccountId = accounts[accumulatedAccount],
                    DepreciationExpenseAccountId = accounts["62100"],
                    CapitalizationJournalEntryId = journal.Id,
                    Notes = $"Building part of {project.Code}, transferred on completion. The land part is not depreciated.",
                    CreatedAt = DateTime.UtcNow
                };
                _db.FixedAssets.Add(asset);
                await _db.SaveChangesAsync();
                await _fixedAssets.GenerateDepreciationScheduleAsync(asset.Id);
                return asset.Id;
            }

            transfer.RentalFixedAssetId = await RegisterBuildingAsync(
                DevelopmentIntendedUses.Rental, "IP", "15100", "15150", "Investment property - building");
            transfer.OwnUseFixedAssetId = await RegisterBuildingAsync(
                DevelopmentIntendedUses.OwnUse, "PPE", "15200", "15300", "Buildings - own use");
            await _db.SaveChangesAsync();

            return transfer;
        }
    }
}
