using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Brings an existing rental building - one carried in 15100 Investment Property from an
    /// opening balance - under the cost model:
    ///
    ///   1. its share of the unallocated 15100 balance is tagged to the property, land and
    ///      building on separate lines (Dr 15100 with the property / Cr 15100 without), so
    ///      the investment property note shows it against the right building;
    ///   2. the building part is entered in the fixed asset register and depreciated
    ///      monthly into 15150 over its remaining useful life. Land is never depreciated.
    /// </summary>
    public class InvestmentPropertySetupService
    {
        public const string SourceType = "InvestmentPropertySetup";

        private readonly ApplicationDbContext _db;
        private readonly ICurrentCompanyAccessor _currentCompany;
        private readonly AccountingPostingService _posting;
        private readonly JournalWorkflowService _workflow;
        private readonly FixedAssetService _fixedAssets;

        public InvestmentPropertySetupService(
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

        /// <summary>15100 not yet allocated to any property: opening balance plus untagged postings.</summary>
        public async Task<decimal> GetUnallocatedAsync()
        {
            var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Code == "15100");
            if (account == null)
                return 0m;

            var untagged = await _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.AccountId == account.Id && x.PropertyId == null &&
                            x.JournalEntry != null && x.JournalEntry.IsPosted)
                .SumAsync(x => (decimal?)(x.Debit - x.Credit)) ?? 0m;

            return account.OpeningBalance + untagged;
        }

        /// <summary>Owned properties whose building is not yet being depreciated as investment property.</summary>
        public async Task<List<Property>> GetCandidatesAsync()
        {
            var registered = await _db.FixedAssets.AsNoTracking()
                .Where(x => x.PropertyId != null && x.AssetAccount != null && x.AssetAccount.Code == "15100")
                .Select(x => x.PropertyId!.Value)
                .Distinct()
                .ToListAsync();

            // A building developed for sale is inventory, not investment property.
            var forSale = await _db.DevelopmentProjects.AsNoTracking()
                .Where(x => x.PropertyId != null && x.IntendedUse == DevelopmentIntendedUses.Sale)
                .Select(x => x.PropertyId!.Value)
                .ToListAsync();

            return await _db.Properties.AsNoTracking()
                .Where(x => x.OwnershipType == "Owned" && x.IsActive && !registered.Contains(x.Id) && !forSale.Contains(x.Id))
                .OrderBy(x => x.Code)
                .ToListAsync();
        }

        public async Task<FixedAsset> SetUpAsync(
            int propertyId, DateTime startDate, decimal landCost, decimal buildingCost,
            int remainingLifeYears, bool allocateFromOpeningBalance)
        {
            var companyId = _currentCompany.CompanyId
                ?? throw new InvalidOperationException("No company is selected for the current user.");

            var property = await _db.Properties.AsNoTracking().FirstOrDefaultAsync(x => x.Id == propertyId && x.OwnershipType == "Owned")
                           ?? throw new InvalidOperationException("Choose an owned property.");

            if (buildingCost <= 0m)
                throw new InvalidOperationException("Enter the building's carrying amount - the part to be depreciated.");
            if (landCost < 0m)
                throw new InvalidOperationException("The land amount cannot be negative.");
            if (remainingLifeYears < 1 || remainingLifeYears > 100)
                throw new InvalidOperationException("Enter the building's remaining useful life in years (1 to 100).");
            if ((await GetCandidatesAsync()).All(x => x.Id != propertyId))
                throw new InvalidOperationException($"{property.Code} is already being depreciated as investment property.");

            var accounts = await _db.Accounts.AsNoTracking()
                .Where(x => new[] { "15100", "15150", "62100" }.Contains(x.Code))
                .ToDictionaryAsync(x => x.Code, x => x.Id);
            if (accounts.Count < 3)
                throw new InvalidOperationException("Accounts 15100, 15150 and 62100 are needed. Restart the application once so they are added.");

            int? journalId = null;
            if (allocateFromOpeningBalance)
            {
                var unallocated = await GetUnallocatedAsync();
                if (landCost + buildingCost > unallocated)
                    throw new InvalidOperationException(
                        $"Only {unallocated:N2} of investment property is not yet allocated to a property; land plus building is {landCost + buildingCost:N2}.");

                var lines = new List<JournalEntryLine>();
                if (landCost > 0m)
                    lines.Add(new JournalEntryLine { AccountId = accounts["15100"], Debit = landCost, Description = $"{property.Code} land - not depreciated", PropertyId = property.Id });
                lines.Add(new JournalEntryLine { AccountId = accounts["15100"], Debit = buildingCost, Description = $"{property.Code} building", PropertyId = property.Id });
                lines.Add(new JournalEntryLine { AccountId = accounts["15100"], Credit = landCost + buildingCost, Description = "Opening investment property allocated to its building" });

                var journal = await _posting.CreateInvestmentPropertySetupJournalAsync(companyId, startDate.Date, property, lines);
                var warning = await _workflow.PostNewEntryAsync(journal.Id);
                if (warning != null)
                    throw new InvalidOperationException($"The allocation journal was saved as a draft and the building was not set up: {warning}");
                journalId = journal.Id;
            }

            var code = $"IP-{property.Code}";
            if (await _db.FixedAssets.AnyAsync(x => x.AssetCode == code))
                code += "-" + DateTime.UtcNow.ToString("yyMMddHHmm");

            var asset = new FixedAsset
            {
                CompanyId = companyId,
                PropertyId = property.Id,
                AssetCode = code,
                Name = $"{property.Name} - building (investment property)",
                AssetCategory = "Investment property - building",
                AcquisitionDate = startDate.Date,
                InServiceDate = startDate.Date,
                CapitalizationDate = startDate.Date,
                Cost = buildingCost,
                ResidualValue = 0m,
                UsefulLifeMonths = remainingLifeYears * 12,
                DepreciationMethod = "StraightLine",
                Status = "Active",
                AssetAccountId = accounts["15100"],
                AccumulatedDepreciationAccountId = accounts["15150"],
                DepreciationExpenseAccountId = accounts["62100"],
                CapitalizationJournalEntryId = journalId,
                Notes = $"Existing building brought under the cost model. Land of {landCost:N2} is not depreciated.",
                CreatedAt = DateTime.UtcNow
            };

            _db.FixedAssets.Add(asset);
            await _db.SaveChangesAsync();
            await _fixedAssets.GenerateDepreciationScheduleAsync(asset.Id);
            return asset;
        }
    }
}
