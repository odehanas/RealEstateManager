using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Revaluation of a fixed asset to fair value.
    ///
    ///   Dr Accumulated depreciation   (all that has accumulated - eliminated)
    ///       Cr Asset
    ///   then the asset is moved to its fair value:
    ///   increase   Dr Asset / Cr 35100 Revaluation Surplus, or Cr 44100 gain
    ///   decrease   Cr Asset / Dr 35100 as far as the asset has a surplus, Dr 65100 loss
    ///
    /// The months not yet posted are then re-scheduled from the fair value over the
    /// remaining useful life.
    /// </summary>
    public partial class FixedAssetService
    {
        public const string RevaluationSourceType = "FixedAssetRevaluation";

        public const string RevaluationSurplusAccountCode = "35100";
        public const string RevaluationGainAccountCode = "44100";
        public const string RevaluationLossAccountCode = "65100";

        public async Task<FixedAssetRevaluationViewModel> GetRevaluationViewAsync(int fixedAssetId)
        {
            var asset = await _db.FixedAssets.AsNoTracking()
                .Include(x => x.Property)
                .Include(x => x.DepreciationSchedules)
                .FirstOrDefaultAsync(x => x.Id == fixedAssetId)
                ?? throw new InvalidOperationException("Fixed asset was not found.");

            var history = await _db.FixedAssetRevaluations.AsNoTracking()
                .Include(x => x.JournalEntry)
                .Where(x => x.FixedAssetId == asset.Id)
                .OrderByDescending(x => x.RevaluationDate).ThenByDescending(x => x.Id)
                .ToListAsync();

            var accumulated = AccumulatedDepreciation(asset);
            var planned = asset.DepreciationSchedules.Where(x => x.Status == "Planned").ToList();
            var lastPosted = asset.DepreciationSchedules.Where(x => x.Status == "Posted")
                .OrderByDescending(x => x.DepreciationDate).Select(x => (DateTime?)x.DepreciationDate).FirstOrDefault();

            return new FixedAssetRevaluationViewModel
            {
                FixedAssetId = asset.Id,
                AssetCode = asset.AssetCode,
                AssetName = asset.Name,
                AssetCategory = asset.AssetCategory,
                Status = asset.Status,
                PropertyDisplay = asset.Property != null ? $"{asset.Property.Code} - {asset.Property.Name}" : null,
                IsDepreciated = asset.DepreciationMethod != DepreciationMethods.None,
                Cost = asset.Cost,
                AccumulatedDepreciation = accumulated,
                CarryingAmount = asset.Cost - accumulated,
                SurplusBalance = history.Sum(x => x.SurplusAmount),
                LastPostedDate = lastPosted,
                CanRevalue = asset.Status is "Active" or "FullyDepreciated",
                // What the last revaluation used is what this asset is on.
                Treatment = history.FirstOrDefault()?.Treatment
                            ?? (asset.DepreciationMethod == DepreciationMethods.None && asset.PropertyId.HasValue
                                ? RevaluationTreatments.ProfitOrLoss
                                : RevaluationTreatments.Surplus),
                RevaluationDate = lastPosted ?? DateTime.Today,
                FairValue = 0m,
                ValuerName = asset.Property?.ValuerName,
                RemainingLifeMonths = planned.Count,
                ResidualValue = asset.ResidualValue,
                History = history.Select(x => new FixedAssetRevaluationItemViewModel
                {
                    RevaluationDate = x.RevaluationDate,
                    CarryingAmountBefore = x.CarryingAmountBefore,
                    FairValue = x.FairValue,
                    Difference = x.Difference,
                    Treatment = x.Treatment,
                    SurplusAmount = x.SurplusAmount,
                    ProfitOrLossAmount = x.ProfitOrLossAmount,
                    RemainingLifeMonths = x.RemainingLifeMonths,
                    ValuerName = x.ValuerName,
                    JournalEntryNumber = x.JournalEntry?.EntryNumber,
                    CreatedByName = x.CreatedByName
                }).ToList()
            };
        }

        public async Task<FixedAssetRevaluation> RevalueAsync(FixedAssetRevaluationViewModel model, string? byName)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();

            var asset = await _db.FixedAssets
                .Include(x => x.Property)
                .Include(x => x.DepreciationSchedules)
                .FirstOrDefaultAsync(x => x.Id == model.FixedAssetId)
                ?? throw new InvalidOperationException("Fixed asset was not found.");

            if (asset.Status is not ("Active" or "FullyDepreciated"))
                throw new InvalidOperationException("Only an asset in use can be revalued: capitalize it first.");
            if (!RevaluationTreatments.All.Contains(model.Treatment))
                throw new InvalidOperationException("Choose where the change in value is recognised.");
            if (model.FairValue <= 0m)
                throw new InvalidOperationException("Enter the fair value from the valuation.");

            var date = model.RevaluationDate.Date;
            if (date < asset.InServiceDate.Date)
                throw new InvalidOperationException("The revaluation date cannot be before the asset was put in service.");

            var previous = await _db.FixedAssetRevaluations
                .Where(x => x.FixedAssetId == asset.Id)
                .ToListAsync();
            if (previous.Any(x => x.RevaluationDate.Date >= date))
                throw new InvalidOperationException("The asset already has a revaluation on or after that date.");

            // The carrying amount must be the one at the date: everything up to it posted, nothing after it.
            if (asset.DepreciationSchedules.Any(x => x.Status == "Planned" && x.DepreciationDate.Date <= date))
                throw new InvalidOperationException($"Depreciation up to {date:dd/MM/yyyy} is not posted yet. Run the monthly depreciation up to that date first.");
            if (asset.DepreciationSchedules.Any(x => x.Status == "Posted" && x.DepreciationDate.Date > date))
                throw new InvalidOperationException($"Depreciation is already posted after {date:dd/MM/yyyy}. Choose a date on or after the last posted month.");

            var depreciated = asset.DepreciationMethod != DepreciationMethods.None;
            var remainingLife = depreciated ? model.RemainingLifeMonths : 0;
            var residual = depreciated ? model.ResidualValue : 0m;
            if (depreciated)
            {
                var programmeError = FixedAssetCategoryService.ValidateProgramme(asset.DepreciationMethod, remainingLife, asset.DecliningRatePercent);
                if (programmeError != null)
                    throw new InvalidOperationException("Remaining useful life: " + programmeError);
                if (residual < 0m || residual >= model.FairValue)
                    throw new InvalidOperationException("Residual value must be zero or more, and less than the fair value.");
            }

            var costBefore = asset.Cost;
            var accumulatedBefore = AccumulatedDepreciation(asset);
            var difference = model.FairValue - (costBefore - accumulatedBefore);

            var (surplusAmount, profitOrLossAmount) = SplitRevaluation(
                model.Treatment,
                difference,
                surplusBalance: previous.Sum(x => x.SurplusAmount),
                // Earlier decreases of this asset charged to profit or loss, net of what later increases reversed.
                lossInProfitOrLoss: Math.Max(0m, -previous.Where(x => x.Treatment == RevaluationTreatments.Surplus).Sum(x => x.ProfitOrLossAmount)));

            var assetAccount = await GetAccountAsync(asset.AssetAccountId, "Fixed Asset");
            var lines = new List<JournalEntryLine>();

            if (accumulatedBefore != 0m)
            {
                var accumulatedAccount = await GetAccountAsync(asset.AccumulatedDepreciationAccountId, "Accumulated Depreciation");
                lines.Add(new JournalEntryLine { AccountId = accumulatedAccount.Id, Debit = accumulatedBefore, Description = $"Accumulated depreciation eliminated on revaluation of {asset.AssetCode}", PropertyId = asset.PropertyId });
                lines.Add(new JournalEntryLine { AccountId = assetAccount.Id, Credit = accumulatedBefore, Description = $"Accumulated depreciation eliminated on revaluation of {asset.AssetCode}", PropertyId = asset.PropertyId });
            }

            if (difference > 0m)
                lines.Add(new JournalEntryLine { AccountId = assetAccount.Id, Debit = difference, Description = $"Revaluation increase of {asset.AssetCode} to fair value", PropertyId = asset.PropertyId });
            else if (difference < 0m)
                lines.Add(new JournalEntryLine { AccountId = assetAccount.Id, Credit = -difference, Description = $"Revaluation decrease of {asset.AssetCode} to fair value", PropertyId = asset.PropertyId });

            if (surplusAmount != 0m)
            {
                var surplusAccount = await GetAccountByCodeAsync(asset.CompanyId, RevaluationSurplusAccountCode, "Revaluation Surplus");
                lines.Add(new JournalEntryLine
                {
                    AccountId = surplusAccount.Id,
                    Debit = surplusAmount < 0m ? -surplusAmount : 0m,
                    Credit = surplusAmount > 0m ? surplusAmount : 0m,
                    Description = $"Revaluation surplus - {asset.AssetCode}",
                    PropertyId = asset.PropertyId
                });
            }

            if (profitOrLossAmount > 0m)
            {
                var gainAccount = await GetAccountByCodeAsync(asset.CompanyId, RevaluationGainAccountCode, "Gain on Revaluation of Property and Assets");
                lines.Add(new JournalEntryLine { AccountId = gainAccount.Id, Credit = profitOrLossAmount, Description = $"Fair value gain - {asset.AssetCode}", PropertyId = asset.PropertyId });
            }
            else if (profitOrLossAmount < 0m)
            {
                var lossAccount = await GetAccountByCodeAsync(asset.CompanyId, RevaluationLossAccountCode, "Loss on Revaluation of Property and Assets");
                lines.Add(new JournalEntryLine { AccountId = lossAccount.Id, Debit = -profitOrLossAmount, Description = $"Fair value loss - {asset.AssetCode}", PropertyId = asset.PropertyId });
            }

            var revaluation = new FixedAssetRevaluation
            {
                CompanyId = asset.CompanyId,
                FixedAssetId = asset.Id,
                RevaluationDate = date,
                CostBefore = costBefore,
                AccumulatedDepreciationBefore = accumulatedBefore,
                FairValue = model.FairValue,
                Difference = difference,
                Treatment = model.Treatment,
                SurplusAmount = surplusAmount,
                ProfitOrLossAmount = profitOrLossAmount,
                RemainingLifeMonths = remainingLife,
                ResidualValue = residual,
                ValuerName = string.IsNullOrWhiteSpace(model.ValuerName) ? null : model.ValuerName.Trim(),
                Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
                CreatedByName = byName
            };
            _db.FixedAssetRevaluations.Add(revaluation);
            await _db.SaveChangesAsync();

            // A valuation that confirms the carrying amount of an asset with nothing accumulated moves nothing.
            if (lines.Count >= 2)
            {
                var journal = await CreateJournalEntryAsync(
                    asset.CompanyId,
                    date,
                    $"Revaluation of {asset.AssetCode} to fair value {model.FairValue:N2}",
                    RevaluationSourceType,
                    revaluation.Id,
                    lines,
                    journalType: "FixedAsset",
                    documentNumber: asset.AssetCode,
                    referenceNumber: date.ToString("yyyyMMdd"),
                    remarks: revaluation.ValuerName != null ? $"Valuer: {revaluation.ValuerName}" : null);
                revaluation.JournalEntryId = journal.Id;
            }

            // The asset now stands at fair value with nothing accumulated.
            asset.Cost = model.FairValue;
            asset.RevaluationEliminatedDepreciation += accumulatedBefore;
            asset.ResidualValue = residual;

            // Clear what was still to come before scheduling it again: a month holds one line.
            var stale = asset.DepreciationSchedules.Where(x => x.Status == "Planned").ToList();
            _db.FixedAssetDepreciations.RemoveRange(stale);
            await _db.SaveChangesAsync();

            if (depreciated)
            {
                var monthEnd = new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
                var first = monthEnd > date ? new DateTime(date.Year, date.Month, 1) : new DateTime(date.Year, date.Month, 1).AddMonths(1);
                var amounts = BuildMonthlyAmounts(asset.DepreciationMethod, model.FairValue, residual, remainingLife, asset.DecliningRatePercent);

                for (var i = 0; i < amounts.Count; i++)
                {
                    if (amounts[i] <= 0m)
                        continue;

                    var month = first.AddMonths(i);
                    _db.FixedAssetDepreciations.Add(new FixedAssetDepreciation
                    {
                        FixedAssetId = asset.Id,
                        PeriodYear = month.Year,
                        PeriodMonth = month.Month,
                        DepreciationDate = new DateTime(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month)),
                        Amount = amounts[i],
                        Status = "Planned"
                    });
                }

                asset.UsefulLifeMonths = asset.DepreciationSchedules.Count(x => x.Status == "Posted") + remainingLife;
                asset.Status = "Active";
            }

            // The valuation is also the fair value disclosed for the building - the company's own, not one it manages.
            if (asset.Property != null && asset.Property.OwnershipType == "Owned")
            {
                asset.Property.FairValue = model.FairValue;
                asset.Property.FairValueDate = date;
                if (revaluation.ValuerName != null)
                    asset.Property.ValuerName = revaluation.ValuerName;
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return revaluation;
        }

        /// <summary>
        /// How a change in value is split between the revaluation surplus (equity) and
        /// profit or loss. Returned amounts are signed: positive adds to the surplus / is a gain.
        /// </summary>
        public static (decimal Surplus, decimal ProfitOrLoss) SplitRevaluation(
            string treatment, decimal difference, decimal surplusBalance, decimal lossInProfitOrLoss)
        {
            if (treatment == RevaluationTreatments.ProfitOrLoss)
                return (0m, difference);

            if (difference >= 0m)
            {
                // An increase first reverses an earlier decrease that went through profit or loss.
                var gain = Math.Min(difference, Math.Max(0m, lossInProfitOrLoss));
                return (difference - gain, gain);
            }

            // A decrease first uses up the surplus this asset has; the rest is a loss.
            var fromSurplus = Math.Min(-difference, Math.Max(0m, surplusBalance));
            return (-fromSurplus, difference + fromSurplus);
        }

        /// <summary>Depreciation accumulated on the asset as it stands: posted, less what revaluations eliminated.</summary>
        public static decimal AccumulatedDepreciation(FixedAsset asset) =>
            asset.DepreciationSchedules.Where(x => x.Status == "Posted").Sum(x => x.Amount) - asset.RevaluationEliminatedDepreciation;

        private async Task<Account> GetAccountByCodeAsync(int companyId, string code, string name)
        {
            var account = await _db.Accounts.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Code == code);
            if (account == null)
                throw new InvalidOperationException($"Account {code} {name} is needed for a revaluation. Restart the application once so it is added, or add it in the Chart of Accounts.");
            return await GetAccountAsync(account.Id, name);
        }
    }
}
