using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class AccountMappingsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public AccountMappingsController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies
                .Include(x => x.AccountSetting)
                .FirstOrDefaultAsync(x => x.Id == companyId);

            if (company == null)
                return BadRequest("No company record found.");

            await LoadAccountsAsync();

            var setting = company.AccountSetting;

            var model = new AccountMappingViewModel
            {
                DefaultCashAccountId = setting?.DefaultCashAccountId,
                AccountsReceivableAccountId = setting?.AccountsReceivableAccountId,
                AccountsPayableAccountId = setting?.AccountsPayableAccountId,
                AdvancesFromCustomersAccountId = setting?.AdvancesFromCustomersAccountId,
                UnearnedRevenueAccountId = setting?.UnearnedRevenueAccountId,
                TenantSecurityDepositLiabilityAccountId = setting?.TenantSecurityDepositLiabilityAccountId,
                VatRecoverableAccountId = setting?.VatRecoverableAccountId,
                VatPayableAccountId = setting?.VatPayableAccountId,
                DueToLandlordsAccountId = setting?.DueToLandlordsAccountId,
                RentalRevenueOwnedAccountId = setting?.RentalRevenueOwnedAccountId,
                ManagementFeeRevenueAccountId = setting?.ManagementFeeRevenueAccountId,
                OtherIncomeAccountId = setting?.OtherIncomeAccountId,
                DirectExpenseDefaultAccountId = setting?.DirectExpenseDefaultAccountId,
                GeneralExpenseDefaultAccountId = setting?.GeneralExpenseDefaultAccountId,
                DepreciationExpenseAccountId = setting?.DepreciationExpenseAccountId,
                FinanceCostAccountId = setting?.FinanceCostAccountId,
                RetainedEarningsPriorYearsAccountId = setting?.RetainedEarningsPriorYearsAccountId,
                CurrentYearProfitOrLossAccountId = setting?.CurrentYearProfitOrLossAccountId,
                InvestmentPropertyAccountId = setting?.InvestmentPropertyAccountId,
                PdcOnHandAccountId = setting?.PdcOnHandAccountId,
                PrepaidExpenseDefaultAccountId = setting?.PrepaidExpenseDefaultAccountId,
                PdcContraAccountId = setting?.PdcContraAccountId,
                FixedAssetDefaultAccountId = setting?.FixedAssetDefaultAccountId,
                FixedAssetClearingAccountId = setting?.FixedAssetClearingAccountId,
                AccumulatedDepreciationDefaultAccountId = setting?.AccumulatedDepreciationDefaultAccountId
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(AccountMappingViewModel model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies
                .Include(x => x.AccountSetting)
                .FirstOrDefaultAsync(x => x.Id == companyId);

            if (company == null)
                return BadRequest("No company record found.");

            await LoadAccountsAsync();

            if (!ModelState.IsValid)
                return View(model);

            var setting = company.AccountSetting;

            if (setting == null)
            {
                setting = new CompanyAccountSetting
                {
                    CompanyId = company.Id
                };

                _db.CompanyAccountSettings.Add(setting);
            }

            setting.DefaultCashAccountId = model.DefaultCashAccountId;
            setting.AccountsReceivableAccountId = model.AccountsReceivableAccountId;
            setting.AccountsPayableAccountId = model.AccountsPayableAccountId;
            setting.AdvancesFromCustomersAccountId = model.AdvancesFromCustomersAccountId;
            setting.UnearnedRevenueAccountId = model.UnearnedRevenueAccountId;
            setting.TenantSecurityDepositLiabilityAccountId = model.TenantSecurityDepositLiabilityAccountId;
            setting.VatRecoverableAccountId = model.VatRecoverableAccountId;
            setting.VatPayableAccountId = model.VatPayableAccountId;
            setting.DueToLandlordsAccountId = model.DueToLandlordsAccountId;
            setting.RentalRevenueOwnedAccountId = model.RentalRevenueOwnedAccountId;
            setting.ManagementFeeRevenueAccountId = model.ManagementFeeRevenueAccountId;
            setting.OtherIncomeAccountId = model.OtherIncomeAccountId;
            setting.DirectExpenseDefaultAccountId = model.DirectExpenseDefaultAccountId;
            setting.GeneralExpenseDefaultAccountId = model.GeneralExpenseDefaultAccountId;
            setting.DepreciationExpenseAccountId = model.DepreciationExpenseAccountId;
            setting.FinanceCostAccountId = model.FinanceCostAccountId;
            setting.RetainedEarningsPriorYearsAccountId = model.RetainedEarningsPriorYearsAccountId;
            setting.CurrentYearProfitOrLossAccountId = model.CurrentYearProfitOrLossAccountId;
            setting.InvestmentPropertyAccountId = model.InvestmentPropertyAccountId;
            setting.PdcOnHandAccountId = model.PdcOnHandAccountId;
            setting.PrepaidExpenseDefaultAccountId = model.PrepaidExpenseDefaultAccountId;
            setting.PdcContraAccountId = model.PdcContraAccountId;
            setting.FixedAssetDefaultAccountId = model.FixedAssetDefaultAccountId;
            setting.FixedAssetClearingAccountId = model.FixedAssetClearingAccountId;
            setting.AccumulatedDepreciationDefaultAccountId = model.AccumulatedDepreciationDefaultAccountId;

            await _db.SaveChangesAsync();

            ViewBag.SuccessMessage = "Account mappings saved successfully.";
            return View(model);
        }

        private async Task LoadAccountsAsync()
        {
            ViewBag.Accounts = await _db.Accounts
                .Where(x => x.IsActive && x.IsPostable)
                .OrderBy(x => x.Code)
                .ToListAsync();
        }
    }
}
