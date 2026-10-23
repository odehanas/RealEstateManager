using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    public class FixedAssetService
    {
        private readonly ApplicationDbContext _db;

        public FixedAssetService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<int> CreateDraftAssetAsync(int companyId, FixedAssetCreateViewModel model)
        {
            if (await _db.FixedAssets.AnyAsync(x => x.CompanyId == companyId && x.AssetCode == model.AssetCode.Trim()))
                throw new InvalidOperationException("Asset code already exists.");

            var asset = new FixedAsset
            {
                CompanyId = companyId,
                PropertyId = model.PropertyId,
                AssetCode = model.AssetCode.Trim(),
                Name = model.Name.Trim(),
                AssetCategory = string.IsNullOrWhiteSpace(model.AssetCategory) ? "General" : model.AssetCategory.Trim(),
                AcquisitionDate = model.AcquisitionDate.Date,
                InServiceDate = model.InServiceDate.Date,
                Cost = 0m,
                ResidualValue = model.ResidualValue,
                UsefulLifeMonths = model.UsefulLifeMonths,
                OpeningCost = model.OpeningCost,
                OpeningAccumulatedDepreciation = model.OpeningAccumulatedDepreciation,
                DepreciationMethod = "StraightLine",
                Status = "Draft",
                AssetAccountId = model.AssetAccountId!.Value,
                AccumulatedDepreciationAccountId = model.AccumulatedDepreciationAccountId!.Value,
                DepreciationExpenseAccountId = model.DepreciationExpenseAccountId!.Value,
                Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _db.FixedAssets.Add(asset);
            await _db.SaveChangesAsync();

            if (model.Cost > 0m)
            {
                if (!model.InitialCreditAccountId.HasValue)
                    throw new InvalidOperationException("Initial cost credit account is required when an initial acquisition cost is entered.");

                await AddAcquisitionCostAsync(new FixedAssetAcquisitionCostEntryViewModel
                {
                    FixedAssetId = asset.Id,
                    CostDate = model.AcquisitionDate.Date,
                    CostType = "Purchase",
                    Description = "Initial acquisition cost",
                    Amount = model.Cost,
                    IsCapitalizable = true,
                    CreditAccountId = model.InitialCreditAccountId,
                    VendorId = model.InitialVendorId
                });
            }

            return asset.Id;
        }

        public async Task GenerateDepreciationScheduleAsync(int fixedAssetId)
        {
            var asset = await _db.FixedAssets
                .Include(x => x.DepreciationSchedules)
                .FirstOrDefaultAsync(x => x.Id == fixedAssetId);

            if (asset == null)
                throw new InvalidOperationException("Fixed asset was not found.");

            ValidateAssetForSchedule(asset);

            var postedSchedules = asset.DepreciationSchedules
                .Where(x => x.Status == "Posted")
                .ToList();

            if (postedSchedules.Any())
                throw new InvalidOperationException("Depreciation schedule cannot be regenerated after posting has started.");

            var existingSchedules = asset.DepreciationSchedules.ToList();
            if (existingSchedules.Any())
            {
                _db.FixedAssetDepreciations.RemoveRange(existingSchedules);
            }

            var depreciableAmount = asset.Cost - asset.ResidualValue;
            var monthlyAmount = Math.Round(depreciableAmount / asset.UsefulLifeMonths, 2, MidpointRounding.AwayFromZero);
            var accumulated = 0m;

            for (var i = 0; i < asset.UsefulLifeMonths; i++)
            {
                var periodDate = asset.InServiceDate.Date.AddMonths(i);
                var depreciationDate = new DateTime(
                    periodDate.Year,
                    periodDate.Month,
                    DateTime.DaysInMonth(periodDate.Year, periodDate.Month));

                var amount = i == asset.UsefulLifeMonths - 1
                    ? depreciableAmount - accumulated
                    : monthlyAmount;

                accumulated += amount;

                _db.FixedAssetDepreciations.Add(new FixedAssetDepreciation
                {
                    FixedAssetId = asset.Id,
                    PeriodYear = depreciationDate.Year,
                    PeriodMonth = depreciationDate.Month,
                    DepreciationDate = depreciationDate,
                    Amount = amount,
                    Status = "Planned"
                });
            }

            await _db.SaveChangesAsync();
        }

        public async Task<FixedAssetAcquisitionViewModel> GetAcquisitionViewAsync(int fixedAssetId)
        {
            var asset = await _db.FixedAssets
                .AsNoTracking()
                .Include(x => x.Property)
                .Include(x => x.AcquisitionCosts)
                    .ThenInclude(x => x.OffsetAccount)
                .Include(x => x.AcquisitionCosts)
                    .ThenInclude(x => x.Vendor)
                .Include(x => x.AcquisitionCosts)
                    .ThenInclude(x => x.VendorBill)
                .Include(x => x.AcquisitionCosts)
                    .ThenInclude(x => x.ExpenseAccount)
                .Include(x => x.AcquisitionCosts)
                    .ThenInclude(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == fixedAssetId);

            if (asset == null)
                throw new InvalidOperationException("Fixed asset was not found.");

            var companySetting = await _db.CompanyAccountSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CompanyId == asset.CompanyId);

            return new FixedAssetAcquisitionViewModel
            {
                FixedAssetId = asset.Id,
                AssetCode = asset.AssetCode,
                AssetName = asset.Name,
                AssetCategory = asset.AssetCategory,
                Status = asset.Status,
                PropertyDisplay = asset.Property != null ? $"{asset.Property.Code} - {asset.Property.Name}" : "-",
                AcquisitionDate = asset.AcquisitionDate,
                InServiceDate = asset.InServiceDate,
                CapitalizationDate = asset.CapitalizationDate,
                ResidualValue = asset.ResidualValue,
                UsefulLifeMonths = asset.UsefulLifeMonths,
                CapitalizableCost = asset.AcquisitionCosts
                    .Where(x => x.IsCapitalizable)
                    .Sum(x => x.Amount),
                OpenCapitalizableCost = asset.AcquisitionCosts
                    .Where(x => x.IsCapitalizable && x.Status == "Open")
                    .Sum(x => x.Amount),
                DefaultClearingAccountId = companySetting?.FixedAssetClearingAccountId,
                DefaultOffsetAccountId = companySetting?.AccountsPayableAccountId ?? companySetting?.DefaultCashAccountId,
                AccountsPayableAccountId = companySetting?.AccountsPayableAccountId,
                DefaultExpenseAccountId = companySetting?.GeneralExpenseDefaultAccountId,
                Costs = asset.AcquisitionCosts
                    .OrderByDescending(x => x.CostDate)
                    .ThenByDescending(x => x.Id)
                    .Select(x => new FixedAssetAcquisitionCostItemViewModel
                    {
                        CostDate = x.CostDate,
                        CostType = x.CostType,
                        Description = x.Description,
                        Amount = x.Amount,
                        IsCapitalizable = x.IsCapitalizable,
                        Status = x.Status,
                        SourceDocumentNumber = x.SourceDocumentNumber,
                        ReferenceNumber = x.ReferenceNumber,
                        OffsetAccountDisplay = x.OffsetAccount != null ? $"{x.OffsetAccount.Code} - {x.OffsetAccount.Name}" : null,
                        VendorDisplay = x.Vendor != null ? $"{x.Vendor.Code} - {x.Vendor.Name}" : null,
                        VendorBillNumber = x.VendorBill?.BillNumber,
                        ExpenseAccountDisplay = x.ExpenseAccount != null ? $"{x.ExpenseAccount.Code} - {x.ExpenseAccount.Name}" : null,
                        JournalEntryNumber = x.JournalEntry != null ? x.JournalEntry.EntryNumber : null,
                        CreatedAt = x.CreatedAt,
                        CapitalizedAt = x.CapitalizedAt
                    })
                    .ToList()
            };
        }

        public async Task AddAcquisitionCostAsync(FixedAssetAcquisitionCostEntryViewModel model)
        {
            var asset = await _db.FixedAssets
                .Include(x => x.AcquisitionCosts)
                .FirstOrDefaultAsync(x => x.Id == model.FixedAssetId);

            if (asset == null)
                throw new InvalidOperationException("Fixed asset was not found.");

            if (asset.Status != "Draft")
                throw new InvalidOperationException("Additional acquisition costs can only be added while the asset is in Draft status.");

            if (model.Amount <= 0m)
                throw new InvalidOperationException("Cost amount must be greater than zero.");

            if (string.IsNullOrWhiteSpace(model.Description))
                throw new InvalidOperationException("Cost description is required.");

            if (!model.CreditAccountId.HasValue)
                throw new InvalidOperationException("Payment / payable account is required.");

            var companySetting = await _db.CompanyAccountSettings
                .FirstOrDefaultAsync(x => x.CompanyId == asset.CompanyId);

            var offsetAccount = await GetAccountAsync(model.CreditAccountId.Value, "Payment / Payable");
            var usesAccountsPayable = companySetting?.AccountsPayableAccountId == offsetAccount.Id;

            if (usesAccountsPayable && !model.VendorId.HasValue)
                throw new InvalidOperationException("Vendor is required when the payment / payable account is Accounts Payable.");

            Account? debitAccount = null;
            if (model.IsCapitalizable)
            {
                if (companySetting?.FixedAssetClearingAccountId == null)
                    throw new InvalidOperationException("Fixed asset clearing account is not configured in Account Mapping Setup.");

                debitAccount = await GetAccountAsync(companySetting.FixedAssetClearingAccountId.Value, "Fixed Asset Clearing");
            }
            else
            {
                var expenseAccountId = model.ExpenseAccountId ?? companySetting?.GeneralExpenseDefaultAccountId;
                if (!expenseAccountId.HasValue)
                    throw new InvalidOperationException("Expense account is required for non-capitalizable costs.");

                debitAccount = await GetAccountAsync(expenseAccountId.Value, "Expense");
            }

            VendorBill? vendorBill = null;
            if (usesAccountsPayable)
            {
                vendorBill = await CreateAutoVendorBillAsync(
                    asset.CompanyId,
                    model.VendorId!.Value,
                    asset.PropertyId,
                    model.CostDate.Date,
                    model.Amount,
                    "FixedAssetAcquisition",
                    asset.AssetCode,
                    model.ReferenceNumber);
            }

            var journalLines = new List<JournalEntryLine>
            {
                new()
                {
                    AccountId = debitAccount.Id,
                    Debit = model.Amount,
                    Credit = 0m,
                    Description = $"{(model.IsCapitalizable ? "Asset acquisition" : "Asset-related expense")} - {asset.AssetCode}",
                    PropertyId = asset.PropertyId,
                    VendorId = vendorBill?.VendorId,
                    VendorBillId = vendorBill?.Id
                },
                new()
                {
                    AccountId = offsetAccount.Id,
                    Debit = 0m,
                    Credit = model.Amount,
                    Description = $"Settlement for {asset.AssetCode}",
                    PropertyId = asset.PropertyId,
                    VendorId = vendorBill?.VendorId,
                    VendorBillId = vendorBill?.Id
                }
            };

            var journalEntry = await CreateJournalEntryAsync(
                asset.CompanyId,
                model.CostDate.Date,
                $"{(model.IsCapitalizable ? "Fixed asset acquisition" : "Fixed asset expense")} for {asset.AssetCode}",
                "FixedAssetAcquisition",
                asset.Id,
                journalLines,
                journalType: model.IsCapitalizable ? "FixedAsset" : "Expense",
                documentNumber: asset.AssetCode,
                referenceNumber: string.IsNullOrWhiteSpace(model.ReferenceNumber) ? null : model.ReferenceNumber.Trim(),
                remarks: string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim());

            var costLine = new FixedAssetAcquisitionCost
            {
                FixedAssetId = asset.Id,
                CostDate = model.CostDate.Date,
                CostType = string.IsNullOrWhiteSpace(model.CostType) ? "Other" : model.CostType.Trim(),
                Description = model.Description.Trim(),
                Amount = model.Amount,
                IsCapitalizable = model.IsCapitalizable,
                Status = "Open",
                SourceDocumentNumber = string.IsNullOrWhiteSpace(model.SourceDocumentNumber) ? null : model.SourceDocumentNumber.Trim(),
                ReferenceNumber = string.IsNullOrWhiteSpace(model.ReferenceNumber) ? null : model.ReferenceNumber.Trim(),
                Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
                OffsetAccountId = offsetAccount.Id,
                VendorId = vendorBill?.VendorId,
                VendorBillId = vendorBill?.Id,
                ExpenseAccountId = model.IsCapitalizable ? null : debitAccount.Id,
                JournalEntryId = journalEntry.Id,
                CreatedAt = DateTime.UtcNow
            };

            if (vendorBill != null)
            {
                vendorBill.JournalEntryId = journalEntry.Id;
            }

            _db.FixedAssetAcquisitionCosts.Add(costLine);

            await _db.SaveChangesAsync();
        }

        public async Task CapitalizeAsync(FixedAssetCapitalizeViewModel model)
        {
            var asset = await _db.FixedAssets
                .Include(x => x.AcquisitionCosts)
                .Include(x => x.DepreciationSchedules)
                .FirstOrDefaultAsync(x => x.Id == model.FixedAssetId);

            if (asset == null)
                throw new InvalidOperationException("Fixed asset was not found.");

            if (asset.Status != "Draft")
                throw new InvalidOperationException("Only draft assets can be capitalized.");

            if (!model.CreditAccountId.HasValue)
                throw new InvalidOperationException("Capitalization credit account is required.");

            if (model.InServiceDate.Date < asset.AcquisitionDate.Date)
                throw new InvalidOperationException("In-service date cannot be earlier than acquisition date.");

            var openCapitalizableLines = asset.AcquisitionCosts
                .Where(x => x.IsCapitalizable && x.Status == "Open")
                .OrderBy(x => x.CostDate)
                .ThenBy(x => x.Id)
                .ToList();

            if (openCapitalizableLines.Count == 0)
                throw new InvalidOperationException("There are no open capitalizable acquisition costs to capitalize.");

            var amountToCapitalize = openCapitalizableLines.Sum(x => x.Amount);
            if (amountToCapitalize <= 0m)
                throw new InvalidOperationException("Capitalization amount must be greater than zero.");

            var assetAccount = await GetAccountAsync(asset.AssetAccountId, "Fixed Asset");
            var creditAccount = await GetAccountAsync(model.CreditAccountId.Value, "Fixed Asset Clearing");

            var lines = new List<JournalEntryLine>
            {
                new()
                {
                    AccountId = assetAccount.Id,
                    Debit = amountToCapitalize,
                    Credit = 0m,
                    Description = $"Capitalize asset {asset.AssetCode}",
                    PropertyId = asset.PropertyId
                },
                new()
                {
                    AccountId = creditAccount.Id,
                    Debit = 0m,
                    Credit = amountToCapitalize,
                    Description = $"Clear acquisition cost for asset {asset.AssetCode}",
                    PropertyId = asset.PropertyId
                }
            };

            var journalEntry = await CreateJournalEntryAsync(
                asset.CompanyId,
                model.CapitalizationDate.Date,
                $"Fixed asset capitalization for {asset.AssetCode}",
                "FixedAssetCapitalization",
                asset.Id,
                lines,
                journalType: "FixedAsset",
                documentNumber: asset.AssetCode,
                referenceNumber: model.CapitalizationDate.ToString("yyyyMMdd"),
                remarks: string.IsNullOrWhiteSpace(model.Remarks) ? null : model.Remarks.Trim());

            asset.Cost = amountToCapitalize;
            asset.CapitalizationDate = model.CapitalizationDate.Date;
            asset.InServiceDate = model.InServiceDate.Date;
            asset.CapitalizationJournalEntryId = journalEntry.Id;
            asset.Status = "Active";

            foreach (var line in openCapitalizableLines)
            {
                line.Status = "Capitalized";
                line.CapitalizedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            await GenerateDepreciationScheduleAsync(asset.Id);
        }

        public async Task<FixedAssetRegisterViewModel> GetRegisterAsync(DateTime postingDate)
        {
            var assets = await _db.FixedAssets
                .AsNoTracking()
                .Include(x => x.Property)
                .Include(x => x.DepreciationSchedules)
                .OrderBy(x => x.AssetCode)
                .ToListAsync();

            var rows = assets.Select(asset =>
            {
                var postedAmount = asset.DepreciationSchedules
                    .Where(x => x.Status == "Posted")
                    .Sum(x => x.Amount);

                var pendingSchedules = asset.DepreciationSchedules
                    .Where(x => x.Status == "Planned" && x.DepreciationDate.Date <= postingDate.Date)
                    .ToList();

                var netBookValue = asset.Cost - postedAmount;
                var monthlyAmount = asset.UsefulLifeMonths > 0
                    ? Math.Round((asset.Cost - asset.ResidualValue) / asset.UsefulLifeMonths, 2, MidpointRounding.AwayFromZero)
                    : 0m;

                return new FixedAssetRegisterItemViewModel
                {
                    FixedAssetId = asset.Id,
                    AssetCode = asset.AssetCode,
                    Name = asset.Name,
                    AssetCategory = asset.AssetCategory,
                    PropertyDisplay = asset.Property != null ? $"{asset.Property.Code} - {asset.Property.Name}" : "-",
                    AcquisitionDate = asset.AcquisitionDate,
                    InServiceDate = asset.InServiceDate,
                    CapitalizationDate = asset.CapitalizationDate,
                    Cost = asset.Cost,
                    ResidualValue = asset.ResidualValue,
                    UsefulLifeMonths = asset.UsefulLifeMonths,
                    MonthlyDepreciation = monthlyAmount,
                    AccumulatedDepreciation = postedAmount,
                    NetBookValue = netBookValue,
                    Status = asset.Status,
                    PendingDepreciationCount = pendingSchedules.Count,
                    PendingDepreciationAmount = pendingSchedules.Sum(x => x.Amount)
                };
            }).ToList();

            return new FixedAssetRegisterViewModel
            {
                PostingDate = postingDate.Date,
                TotalCost = rows.Sum(x => x.Cost),
                TotalAccumulatedDepreciation = rows.Sum(x => x.AccumulatedDepreciation),
                TotalNetBookValue = rows.Sum(x => x.NetBookValue),
                PendingDepreciationCount = rows.Sum(x => x.PendingDepreciationCount),
                PendingDepreciationAmount = rows.Sum(x => x.PendingDepreciationAmount),
                Assets = rows
            };
        }

        public async Task<FixedAssetDepreciationViewModel> GetDepreciationViewAsync(int fixedAssetId)
        {
            var asset = await _db.FixedAssets
                .AsNoTracking()
                .Include(x => x.DepreciationSchedules)
                    .ThenInclude(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == fixedAssetId);

            if (asset == null)
                throw new InvalidOperationException("Fixed asset was not found.");

            var postedAmount = asset.DepreciationSchedules
                .Where(x => x.Status == "Posted")
                .Sum(x => x.Amount);

            return new FixedAssetDepreciationViewModel
            {
                FixedAssetId = asset.Id,
                AssetCode = asset.AssetCode,
                AssetName = asset.Name,
                Cost = asset.Cost,
                ResidualValue = asset.ResidualValue,
                MonthlyDepreciation = asset.UsefulLifeMonths > 0
                    ? Math.Round((asset.Cost - asset.ResidualValue) / asset.UsefulLifeMonths, 2, MidpointRounding.AwayFromZero)
                    : 0m,
                PostedDepreciation = postedAmount,
                NetBookValue = asset.Cost - postedAmount,
                Schedules = asset.DepreciationSchedules
                    .OrderBy(x => x.DepreciationDate)
                    .ThenBy(x => x.Id)
                    .Select(x => new FixedAssetDepreciationItemViewModel
                    {
                        DepreciationDate = x.DepreciationDate,
                        PeriodName = $"{x.PeriodYear}-{x.PeriodMonth:D2}",
                        Amount = x.Amount,
                        Status = x.Status,
                        JournalEntryNumber = x.JournalEntry?.EntryNumber,
                        PostedAt = x.PostedAt
                    })
                    .ToList()
            };
        }

        /// <param name="fromDate">
        /// Only lines on or after this date. The Closing Cockpit passes the period start so
        /// its run never reaches into an earlier month, which may be closed.
        /// </param>
        public async Task<FixedAssetDepreciationRunResult> PostDepreciationUpToAsync(DateTime postingDate, DateTime? fromDate = null)
        {
            var cutoffDate = new DateTime(
                postingDate.Year,
                postingDate.Month,
                DateTime.DaysInMonth(postingDate.Year, postingDate.Month));
            var startDate = fromDate?.Date ?? DateTime.MinValue;

            var schedules = await _db.FixedAssetDepreciations
                .Include(x => x.FixedAsset)
                    .ThenInclude(x => x!.Property)
                .Include(x => x.FixedAsset)
                    .ThenInclude(x => x!.DepreciationExpenseAccount)
                .Include(x => x.FixedAsset)
                    .ThenInclude(x => x!.AccumulatedDepreciationAccount)
                .Where(x =>
                    x.Status == "Planned" &&
                    x.DepreciationDate.Date >= startDate &&
                    x.DepreciationDate.Date <= cutoffDate.Date)
                .OrderBy(x => x.DepreciationDate)
                .ThenBy(x => x.FixedAssetId)
                .ThenBy(x => x.Id)
                .ToListAsync();

            var result = new FixedAssetDepreciationRunResult();

            foreach (var schedule in schedules)
            {
                var asset = schedule.FixedAsset!;
                var expenseAccount = await GetAccountAsync(asset.DepreciationExpenseAccountId, "Depreciation Expense");
                var accumulatedDepreciationAccount = await GetAccountAsync(asset.AccumulatedDepreciationAccountId, "Accumulated Depreciation");

                var lines = new List<JournalEntryLine>
                {
                    new()
                    {
                        AccountId = expenseAccount.Id,
                        Debit = schedule.Amount,
                        Credit = 0m,
                        Description = $"Depreciation expense for asset {asset.AssetCode}",
                        PropertyId = asset.PropertyId
                    },
                    new()
                    {
                        AccountId = accumulatedDepreciationAccount.Id,
                        Debit = 0m,
                        Credit = schedule.Amount,
                        Description = $"Accumulated depreciation for asset {asset.AssetCode}",
                        PropertyId = asset.PropertyId
                    }
                };

                var journalEntry = await CreateJournalEntryAsync(
                    asset.CompanyId,
                    schedule.DepreciationDate,
                    $"Monthly depreciation for asset {asset.AssetCode}",
                    "FixedAssetDepreciation",
                    schedule.Id,
                    lines,
                    journalType: "Depreciation",
                    documentNumber: asset.AssetCode,
                    referenceNumber: $"{schedule.PeriodYear}-{schedule.PeriodMonth:D2}");

                schedule.JournalEntryId = journalEntry.Id;
                schedule.Status = "Posted";
                schedule.PostedAt = DateTime.UtcNow;

                result.PostedCount++;
                result.PostedAmount += schedule.Amount;

                await _db.SaveChangesAsync();

                var remainingPlanned = await _db.FixedAssetDepreciations.AnyAsync(x =>
                    x.FixedAssetId == asset.Id &&
                    x.Status == "Planned");

                if (!remainingPlanned && asset.Status != "FullyDepreciated")
                {
                    asset.Status = "FullyDepreciated";
                    await _db.SaveChangesAsync();
                }
                else if (remainingPlanned && asset.Status == "Draft")
                {
                    asset.Status = "Active";
                    await _db.SaveChangesAsync();
                }
            }

            return result;
        }

        private static void ValidateAssetForSchedule(FixedAsset asset)
        {
            if (asset.Status != "Active")
                throw new InvalidOperationException("Depreciation schedule can only be generated for active fixed assets.");

            if (asset.Cost <= 0m)
                throw new InvalidOperationException("Fixed asset cost must be greater than zero.");

            if (asset.ResidualValue < 0m)
                throw new InvalidOperationException("Residual value cannot be negative.");

            if (asset.ResidualValue >= asset.Cost)
                throw new InvalidOperationException("Residual value must be less than cost.");

            if (asset.UsefulLifeMonths <= 0)
                throw new InvalidOperationException("Useful life in months must be greater than zero.");

            if (asset.DepreciationMethod != "StraightLine")
                throw new InvalidOperationException("Only straight-line depreciation is supported in the current batch.");
        }

        private async Task<Account> GetAccountAsync(int accountId, string mappingName)
        {
            var account = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == accountId);

            if (account == null)
                throw new InvalidOperationException($"Mapped account for '{mappingName}' was not found.");

            if (!account.IsActive)
                throw new InvalidOperationException($"Mapped account for '{mappingName}' is inactive.");

            if (!account.IsPostable)
                throw new InvalidOperationException($"Mapped account for '{mappingName}' must be postable.");

            return account;
        }

        private async Task<VendorBill> CreateAutoVendorBillAsync(
            int companyId,
            int vendorId,
            int? propertyId,
            DateTime billDate,
            decimal amount,
            string expenseCategory,
            string sourceCode,
            string? referenceNumber)
        {
            _ = await GetVendorAsync(companyId, vendorId);

            var bill = new VendorBill
            {
                CompanyId = companyId,
                VendorId = vendorId,
                PropertyId = propertyId,
                BillNumber = await GenerateAutoVendorBillNumberAsync(expenseCategory),
                VendorReference = referenceNumber,
                BillDate = billDate.Date,
                AccountingDate = billDate.Date,
                DueDate = billDate.Date,
                Subtotal = amount,
                VatAmount = 0m,
                TotalAmount = amount,
                AmountPaid = 0m,
                ExpenseCategory = expenseCategory,
                Status = "Posted"
            };

            // A single line so the document reads like any other bill on screen. Its
            // accounting is done by the fixed asset routine that raised it, not by
            // PostVendorBillAsync, so the line carries no account of its own.
            bill.Lines.Add(new VendorBillLine
            {
                LineNumber = 1,
                Description = $"{expenseCategory} - {sourceCode}",
                PropertyId = propertyId,
                Quantity = 1m,
                UnitPrice = amount,
                LineSubtotal = amount,
                IsTaxable = false,
                LineTotal = amount
            });

            _db.VendorBills.Add(bill);
            await _db.SaveChangesAsync();
            return bill;
        }

        private async Task<Vendor> GetVendorAsync(int companyId, int vendorId)
        {
            var vendor = await _db.Vendors.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == vendorId);

            if (vendor == null)
                throw new InvalidOperationException("Selected vendor was not found.");

            if (!vendor.IsActive)
                throw new InvalidOperationException("Selected vendor is inactive.");

            return vendor;
        }

        private async Task<AccountingPeriod?> ResolveAccountingPeriodAsync(int companyId, DateTime entryDate)
        {
            return await _db.AccountingPeriods.FirstOrDefaultAsync(x =>
                x.CompanyId == companyId &&
                x.StartDate <= entryDate.Date &&
                x.EndDate >= entryDate.Date);
        }

        private async Task<JournalEntry> CreateJournalEntryAsync(
            int companyId,
            DateTime entryDate,
            string description,
            string sourceType,
            int sourceId,
            List<JournalEntryLine> lines,
            string journalType = "Auto",
            string? documentNumber = null,
            string? referenceNumber = null,
            string? remarks = null)
        {
            if (lines.Count < 2)
                throw new InvalidOperationException("A journal entry must contain at least two lines.");

            var totalDebit = lines.Sum(x => x.Debit);
            var totalCredit = lines.Sum(x => x.Credit);

            if (Math.Abs(totalDebit - totalCredit) != 0m)
                throw new InvalidOperationException("Journal entry is not balanced.");

            var period = await ResolveAccountingPeriodAsync(companyId, entryDate);

            if (period == null)
                throw new InvalidOperationException("No accounting period found for the entry date.");

            if (period.IsClosed)
                throw new InvalidOperationException("The accounting period is closed. Posting is not allowed.");

            var journalEntry = new JournalEntry
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                EntryNumber = await GenerateEntryNumberAsync(sourceType),
                EntryDate = entryDate.Date,
                JournalType = journalType,
                Status = "Posted",
                Description = description,
                DocumentNumber = documentNumber,
                ReferenceNumber = referenceNumber,
                Remarks = remarks,
                SourceType = sourceType,
                SourceId = sourceId,
                IsPosted = true,
                CreatedAt = DateTime.UtcNow,
                PostedAt = DateTime.UtcNow,
                Lines = lines
            };

            _db.JournalEntries.Add(journalEntry);
            await _db.SaveChangesAsync();

            return journalEntry;
        }

        private async Task<string> GenerateAutoVendorBillNumberAsync(string category)
        {
            var prefix = category switch
            {
                "FixedAssetAcquisition" => "VFA",
                _ => "VAP"
            };

            var count = await _db.VendorBills.CountAsync() + 1;
            return $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{count:D5}";
        }

        private async Task<string> GenerateEntryNumberAsync(string sourceType)
        {
            var prefix = sourceType.Length >= 3
                ? sourceType[..3].ToUpperInvariant()
                : sourceType.ToUpperInvariant();

            var count = await _db.JournalEntries.CountAsync() + 1;
            return $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{count:D6}";
        }
    }
}
