using System.Data;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    public static class ChartOfAccountsSeeder
    {
        /// <summary>
        /// Schema objects this build depends on. If any are missing the database is
        /// behind the deployed code, so seeding is skipped rather than allowed to
        /// throw - an unhandled exception here happens before the request pipeline
        /// is built and takes the whole site down with HTTP 500.30, leaving no UI
        /// from which to fix anything.
        /// </summary>
        private static readonly (string Table, string? Column)[] RequiredSchema =
        {
            ("PdcReceipts", null),
            ("OwnerWithdrawalCategories", null),
            ("Accounts", "IsOwnerAccount"),
            ("Accounts", "IsPettyCashAccount"),
            ("JournalEntryLines", "OwnerWithdrawalCategoryId"),
            ("Companies", "TrnNumber"),
            ("Properties", "VatCategory"),
            ("Tenants", "TrnNumber"),
            ("CustomerInvoices", "TaxInvoiceNumber"),
            ("VendorBills", "InputTaxAttribution"),
            ("Properties", "TitleDeedNumber"),
            ("PropertyDocuments", null),
            ("DevelopmentProjects", null),
            ("JournalEntryLines", "DevelopmentProjectId"),
            ("DevelopmentTransfers", null),
            ("Properties", "FairValue"),
            ("UnitSales", null),
            ("CapitalAssetVatRecords", null),
            ("Units", "FirstSupplyDate"),
            ("Units", "FloorNumber")
        };

        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var logger = scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(ChartOfAccountsSeeder));

            try
            {
                var missing = await GetMissingSchemaObjectsAsync(db);
                if (missing.Count > 0)
                {
                    logger.LogError(
                        "Seeding skipped - the database is behind the deployed code. Missing schema object(s): {Missing}. " +
                        "Apply SQL/02_AddOwnerWithdrawalCategories.sql (and SQL/01_Restore_PdcReceipts.sql if PdcReceipts is listed), " +
                        "then restart the application. The site will start, but any screen that touches these objects will fail until then.",
                        string.Join(", ", missing));
                    return;
                }

                await SeedCoreAsync(db);
            }
            catch (Exception ex)
            {
                // Never let seeding stop the host from starting.
                logger.LogError(ex,
                    "Chart of accounts seeding failed. The application will still start, but company setup may be incomplete.");
            }
        }

        private static async Task SeedCoreAsync(ApplicationDbContext db)
        {
            var companies = await db.Companies
                .IgnoreQueryFilters()
                .OrderBy(x => x.Id)
                .ToListAsync();

            if (companies.Count == 0)
            {
                var company = new Company
                {
                    Name = "Real Estate Manager",
                    BaseCurrency = "AED",
                    FiscalYearStartMonth = 1
                };

                db.Companies.Add(company);
                await db.SaveChangesAsync();
                companies.Add(company);
            }

            foreach (var company in companies)
            {
                await EnsureCompanySetupAsync(db, company);
            }
        }

        /// <summary>
        /// Returns the tables and columns from <see cref="RequiredSchema"/> that are
        /// not present in the database, described in a form that names the fix.
        /// </summary>
        private static async Task<List<string>> GetMissingSchemaObjectsAsync(ApplicationDbContext db)
        {
            var missing = new List<string>();

            var connection = db.Database.GetDbConnection();
            var shouldCloseConnection = connection.State != ConnectionState.Open;

            if (shouldCloseConnection)
            {
                await connection.OpenAsync();
            }

            try
            {
                foreach (var (table, column) in RequiredSchema)
                {
                    await using var command = connection.CreateCommand();

                    if (column == null)
                    {
                        command.CommandText = "SELECT COUNT(1) FROM sys.tables WHERE name = @table;";
                    }
                    else
                    {
                        command.CommandText = """
                            SELECT COUNT(1)
                            FROM sys.columns
                            WHERE object_id = OBJECT_ID(@table) AND name = @column;
                            """;

                        var columnParameter = command.CreateParameter();
                        columnParameter.ParameterName = "@column";
                        columnParameter.Value = column;
                        command.Parameters.Add(columnParameter);
                    }

                    var tableParameter = command.CreateParameter();
                    tableParameter.ParameterName = "@table";
                    tableParameter.Value = table;
                    command.Parameters.Add(tableParameter);

                    var result = await command.ExecuteScalarAsync();

                    if (Convert.ToInt32(result) == 0)
                    {
                        missing.Add(column == null ? $"table {table}" : $"column {table}.{column}");
                    }
                }
            }
            finally
            {
                if (shouldCloseConnection)
                {
                    await connection.CloseAsync();
                }
            }

            return missing;
        }

        public static async Task EnsureCompanySetupAsync(ApplicationDbContext db, Company company)
        {
            await EnsureAccountsAsync(db, company);
            await EnsureAccountingPeriodsAsync(db, company);
            await EnsureCompanyAccountSettingsAsync(db, company);
            await EnsureOwnerWithdrawalCategoriesAsync(db, company);
        }

        /// <summary>
        /// Seeds a starting set of owner expenditure categories. These are only a
        /// convenience so the dimension is usable immediately - every one of them can be
        /// renamed, deactivated or replaced from Setup, and companies are free to build a
        /// completely different list.
        /// </summary>
        private static async Task EnsureOwnerWithdrawalCategoriesAsync(ApplicationDbContext db, Company company)
        {
            var existingCodes = await db.OwnerWithdrawalCategories
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == company.Id)
                .Select(x => x.Code)
                .ToListAsync();

            if (existingCodes.Count > 0)
                return;

            var defaults = new (string Code, string Name, string Description, int SortOrder)[]
            {
                ("OW-10", "Cash Drawings", "Cash taken by the owner for personal use.", 10),
                ("OW-20", "Household & Living", "Groceries, utilities and household running costs.", 20),
                ("OW-30", "Education", "School and university fees for the owner's family.", 30),
                ("OW-40", "Travel", "Personal travel, tickets and accommodation.", 40),
                ("OW-50", "Vehicles", "Personal vehicle purchase, servicing and fuel.", 50),
                ("OW-60", "Medical & Insurance", "Personal medical costs and personal insurance premiums.", 60),
                ("OW-70", "Charity & Donations", "Personal charitable giving.", 70),
                ("OW-80", "Personal Investments", "Funds moved into the owner's private investments.", 80),
                ("OW-99", "Other Personal", "Personal expenditure that does not fit another category.", 99)
            };

            db.OwnerWithdrawalCategories.AddRange(defaults.Select(x => new OwnerWithdrawalCategory
            {
                CompanyId = company.Id,
                Code = x.Code,
                Name = x.Name,
                Description = x.Description,
                SortOrder = x.SortOrder,
                IsActive = true
            }));

            await db.SaveChangesAsync();
        }

        private static async Task EnsureAccountsAsync(ApplicationDbContext db, Company company)
        {
            var accountsByCode = await db.Accounts
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == company.Id)
                .ToDictionaryAsync(x => x.Code);

            var rootAccounts = new List<Account>
            {
                new() { CompanyId = company.Id, Code = "10000", Name = "Assets", AccountType = "Asset", ReportingGroup = "Assets", IsGroup = true, IsPostable = false, IsActive = true, SortOrder = 10000 },
                new() { CompanyId = company.Id, Code = "20000", Name = "Liabilities", AccountType = "Liability", ReportingGroup = "Liabilities", IsGroup = true, IsPostable = false, IsActive = true, SortOrder = 20000 },
                new() { CompanyId = company.Id, Code = "30000", Name = "Equity", AccountType = "Equity", ReportingGroup = "Equity", IsGroup = true, IsPostable = false, IsActive = true, SortOrder = 30000 },
                new() { CompanyId = company.Id, Code = "40000", Name = "Revenue", AccountType = "Revenue", ReportingGroup = "Revenue", IsGroup = true, IsPostable = false, IsActive = true, SortOrder = 40000 },
                new() { CompanyId = company.Id, Code = "50000", Name = "Direct Expenses", AccountType = "Expense", ReportingGroup = "DirectExpense", IsGroup = true, IsPostable = false, IsActive = true, SortOrder = 50000 },
                new() { CompanyId = company.Id, Code = "60000", Name = "General Expenses", AccountType = "Expense", ReportingGroup = "GeneralExpense", IsGroup = true, IsPostable = false, IsActive = true, SortOrder = 60000 }
            };

            var missingRootAccounts = rootAccounts
                .Where(x => !accountsByCode.ContainsKey(x.Code))
                .ToList();

            if (missingRootAccounts.Count > 0)
            {
                db.Accounts.AddRange(missingRootAccounts);
                await db.SaveChangesAsync();

                accountsByCode = await db.Accounts
                    .IgnoreQueryFilters()
                    .Where(x => x.CompanyId == company.Id)
                    .ToDictionaryAsync(x => x.Code);
            }

            var childAccounts = new List<Account>
            {
                new() { CompanyId = company.Id, Code = "11100", Name = "Cash and Cash Equivalents", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsControlAccount = true, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 11100 },
                new() { CompanyId = company.Id, Code = "11200", Name = "Accounts Receivable", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsControlAccount = true, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 11200 },
                new() { CompanyId = company.Id, Code = "11150", Name = "Petty Cash", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsGroup = false, IsPostable = true, IsActive = true, IsPettyCashAccount = true, SortOrder = 11150 },
                new() { CompanyId = company.Id, Code = "11250", Name = "Receivable from Unit Buyers", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 11250 },
                new() { CompanyId = company.Id, Code = "11300", Name = "VAT Recoverable", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 11300 },
                new() { CompanyId = company.Id, Code = "11400", Name = "PDC On Hand", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 11400 },
                new() { CompanyId = company.Id, Code = "11500", Name = "Prepaid Expenses", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 11500 },
                new() { CompanyId = company.Id, Code = "15100", Name = "Investment Property", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 15100 },
                new() { CompanyId = company.Id, Code = "15200", Name = "Fixed Assets", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 15200 },
                new() { CompanyId = company.Id, Code = "15300", Name = "Accumulated Depreciation", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 15300 },
                new() { CompanyId = company.Id, Code = "13100", Name = "Properties Under Development - For Sale", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 13100 },
                new() { CompanyId = company.Id, Code = "13200", Name = "Completed Properties Held for Sale", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 13200 },
                new() { CompanyId = company.Id, Code = "15150", Name = "Accumulated Depreciation - Investment Property", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 15150 },
                new() { CompanyId = company.Id, Code = "15500", Name = "Investment Property Under Construction", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 15500 },
                new() { CompanyId = company.Id, Code = "15600", Name = "Capital Work in Progress", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 15600 },
                new() { CompanyId = company.Id, Code = "15400", Name = "Fixed Asset Clearing", AccountType = "Asset", ReportingGroup = "Assets", ParentAccountId = accountsByCode["10000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 15400 },

                new() { CompanyId = company.Id, Code = "21100", Name = "Accounts Payable", AccountType = "Liability", ReportingGroup = "Liabilities", ParentAccountId = accountsByCode["20000"].Id, IsControlAccount = true, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 21100 },
                new() { CompanyId = company.Id, Code = "21200", Name = "Advances from Customers", AccountType = "Liability", ReportingGroup = "Liabilities", ParentAccountId = accountsByCode["20000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 21200 },
                new() { CompanyId = company.Id, Code = "21250", Name = "Advances from Unit Buyers", AccountType = "Liability", ReportingGroup = "Liabilities", ParentAccountId = accountsByCode["20000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 21250 },
                new() { CompanyId = company.Id, Code = "21300", Name = "Tenant Security Deposits", AccountType = "Liability", ReportingGroup = "Liabilities", ParentAccountId = accountsByCode["20000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 21300 },
                new() { CompanyId = company.Id, Code = "21400", Name = "VAT Payable", AccountType = "Liability", ReportingGroup = "Liabilities", ParentAccountId = accountsByCode["20000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 21400 },
                new() { CompanyId = company.Id, Code = "21500", Name = "Due to Landlords", AccountType = "Liability", ReportingGroup = "Liabilities", ParentAccountId = accountsByCode["20000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 21500 },
                new() { CompanyId = company.Id, Code = "21600", Name = "PDC Contra", AccountType = "Liability", ReportingGroup = "Liabilities", ParentAccountId = accountsByCode["20000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 21600 },

                new() { CompanyId = company.Id, Code = "31100", Name = "Share Capital", AccountType = "Equity", ReportingGroup = "Equity", ParentAccountId = accountsByCode["30000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 31100 },
                new() { CompanyId = company.Id, Code = "32100", Name = "Retained Earnings - Prior Years", AccountType = "Equity", ReportingGroup = "Equity", ParentAccountId = accountsByCode["30000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 32100 },
                new() { CompanyId = company.Id, Code = "33100", Name = "Current Year Profit or Loss", AccountType = "Equity", ReportingGroup = "Equity", ParentAccountId = accountsByCode["30000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 33100 },
                new() { CompanyId = company.Id, Code = "34100", Name = "Owner's Current Account", AccountType = "Equity", ReportingGroup = "Equity", ParentAccountId = accountsByCode["30000"].Id, IsGroup = false, IsPostable = true, IsActive = true, IsOwnerAccount = true, SortOrder = 34100 },
                // Opening balance suspense. Its balance is DERIVED as the contra of every
                // other opening balance, so it is deliberately not postable: journals must
                // never touch it, and its own OpeningBalance field is ignored.
                new() { CompanyId = company.Id, Code = "39999", Name = "Opening Balance Control Account", AccountType = "Equity", ReportingGroup = "Equity", ParentAccountId = accountsByCode["30000"].Id, IsGroup = false, IsPostable = false, IsActive = true, SortOrder = 39999 },

                new() { CompanyId = company.Id, Code = "41100", Name = "Rental Revenue - Owned", AccountType = "Revenue", ReportingGroup = "Revenue", ParentAccountId = accountsByCode["40000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 41100 },
                new() { CompanyId = company.Id, Code = "41200", Name = "Revenue from Sale of Properties", AccountType = "Revenue", ReportingGroup = "Revenue", ParentAccountId = accountsByCode["40000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 41200 },
                new() { CompanyId = company.Id, Code = "42100", Name = "Management Fee Revenue", AccountType = "Revenue", ReportingGroup = "Revenue", ParentAccountId = accountsByCode["40000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 42100 },
                new() { CompanyId = company.Id, Code = "43100", Name = "Other Income", AccountType = "Revenue", ReportingGroup = "Revenue", ParentAccountId = accountsByCode["40000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 43100 },

                new() { CompanyId = company.Id, Code = "51100", Name = "Property Operating Expenses", AccountType = "Expense", ReportingGroup = "DirectExpense", ParentAccountId = accountsByCode["50000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 51100 },
                new() { CompanyId = company.Id, Code = "51200", Name = "Cost of Properties Sold", AccountType = "Expense", ReportingGroup = "DirectExpense", ParentAccountId = accountsByCode["50000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 51200 },
                new() { CompanyId = company.Id, Code = "52100", Name = "Property Maintenance", AccountType = "Expense", ReportingGroup = "DirectExpense", ParentAccountId = accountsByCode["50000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 52100 },

                new() { CompanyId = company.Id, Code = "61100", Name = "Administrative Expenses", AccountType = "Expense", ReportingGroup = "GeneralExpense", ParentAccountId = accountsByCode["60000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 61100 },
                new() { CompanyId = company.Id, Code = "61200", Name = "Irrecoverable VAT - Capital Assets Scheme", AccountType = "Expense", ReportingGroup = "GeneralExpense", ParentAccountId = accountsByCode["60000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 61200 },
                new() { CompanyId = company.Id, Code = "62100", Name = "Depreciation Expense", AccountType = "Expense", ReportingGroup = "GeneralExpense", ParentAccountId = accountsByCode["60000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 62100 },
                new() { CompanyId = company.Id, Code = "63100", Name = "Finance Costs", AccountType = "Expense", ReportingGroup = "GeneralExpense", ParentAccountId = accountsByCode["60000"].Id, IsGroup = false, IsPostable = true, IsActive = true, SortOrder = 63100 }
            };

            var missingChildAccounts = childAccounts
                .Where(x => !accountsByCode.ContainsKey(x.Code))
                .ToList();

            if (missingChildAccounts.Count > 0)
            {
                db.Accounts.AddRange(missingChildAccounts);
                await db.SaveChangesAsync();
            }
        }

        private static async Task EnsureAccountingPeriodsAsync(ApplicationDbContext db, Company company)
        {
            var currentFiscalYear = GetCurrentFiscalYear(company.FiscalYearStartMonth);
            var yearsToCreate = new[] { currentFiscalYear, currentFiscalYear + 1 };

            foreach (var year in yearsToCreate)
            {
                var exists = await db.AccountingPeriods
                    .IgnoreQueryFilters()
                    .AnyAsync(x => x.CompanyId == company.Id && x.FiscalYear == year);

                if (!exists)
                {
                    db.AccountingPeriods.AddRange(BuildMonthlyPeriods(company.Id, year, company.FiscalYearStartMonth));
                }
            }

            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Opens the twelve monthly periods of one fiscal year if the company does not
        /// have them yet. Setup only creates the current and next year; this is for work
        /// that needs an earlier one, such as sample data that starts last January.
        /// </summary>
        public static async Task EnsureFiscalYearPeriodsAsync(ApplicationDbContext db, Company company, int fiscalYear)
        {
            var exists = await db.AccountingPeriods
                .IgnoreQueryFilters()
                .AnyAsync(x => x.CompanyId == company.Id && x.FiscalYear == fiscalYear);

            if (exists)
                return;

            db.AccountingPeriods.AddRange(BuildMonthlyPeriods(company.Id, fiscalYear, company.FiscalYearStartMonth));
            await db.SaveChangesAsync();
        }

        private static async Task EnsureCompanyAccountSettingsAsync(ApplicationDbContext db, Company company)
        {
            var accountsByCode = await db.Accounts
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == company.Id)
                .ToDictionaryAsync(x => x.Code, x => x.Id);

            var setting = await db.CompanyAccountSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.CompanyId == company.Id);

            if (setting == null)
            {
                setting = new CompanyAccountSetting
                {
                    CompanyId = company.Id
                };

                db.CompanyAccountSettings.Add(setting);
            }

            setting.DefaultCashAccountId ??= FindAccountId(accountsByCode, "11100");
            setting.AccountsReceivableAccountId ??= FindAccountId(accountsByCode, "11200");
            setting.VatRecoverableAccountId ??= FindAccountId(accountsByCode, "11300");
            setting.PdcOnHandAccountId ??= FindAccountId(accountsByCode, "11400");
            setting.PrepaidExpenseDefaultAccountId ??= FindAccountId(accountsByCode, "11500");
            setting.InvestmentPropertyAccountId ??= FindAccountId(accountsByCode, "15100");
            setting.FixedAssetDefaultAccountId ??= FindAccountId(accountsByCode, "15200");
            setting.AccumulatedDepreciationDefaultAccountId ??= FindAccountId(accountsByCode, "15300");
            setting.FixedAssetClearingAccountId ??= FindAccountId(accountsByCode, "15400");

            setting.AccountsPayableAccountId ??= FindAccountId(accountsByCode, "21100");
            setting.AdvancesFromCustomersAccountId ??= FindAccountId(accountsByCode, "21200");
            setting.UnearnedRevenueAccountId ??= FindAccountId(accountsByCode, "21200");
            setting.TenantSecurityDepositLiabilityAccountId ??= FindAccountId(accountsByCode, "21300");
            setting.VatPayableAccountId ??= FindAccountId(accountsByCode, "21400");
            setting.DueToLandlordsAccountId ??= FindAccountId(accountsByCode, "21500");
            setting.PdcContraAccountId ??= FindAccountId(accountsByCode, "21600");

            setting.RetainedEarningsPriorYearsAccountId ??= FindAccountId(accountsByCode, "32100");
            setting.CurrentYearProfitOrLossAccountId ??= FindAccountId(accountsByCode, "33100");

            setting.RentalRevenueOwnedAccountId ??= FindAccountId(accountsByCode, "41100");
            setting.ManagementFeeRevenueAccountId ??= FindAccountId(accountsByCode, "42100");
            setting.OtherIncomeAccountId ??= FindAccountId(accountsByCode, "43100");

            setting.DirectExpenseDefaultAccountId ??= FindAccountId(accountsByCode, "51100");
            setting.GeneralExpenseDefaultAccountId ??= FindAccountId(accountsByCode, "61100");
            setting.DepreciationExpenseAccountId ??= FindAccountId(accountsByCode, "62100");
            setting.FinanceCostAccountId ??= FindAccountId(accountsByCode, "63100");

            await db.SaveChangesAsync();
        }

        private static int? FindAccountId(IReadOnlyDictionary<string, int> accountsByCode, string code)
        {
            return accountsByCode.TryGetValue(code, out var id)
                ? id
                : null;
        }

        private static int GetCurrentFiscalYear(int fiscalYearStartMonth)
        {
            var today = DateTime.UtcNow.Date;
            return today.Month >= fiscalYearStartMonth
                ? today.Year
                : today.Year - 1;
        }

        private static IEnumerable<AccountingPeriod> BuildMonthlyPeriods(int companyId, int fiscalYear, int fiscalYearStartMonth)
        {
            var periods = new List<AccountingPeriod>();
            var fiscalStartDate = new DateTime(fiscalYear, fiscalYearStartMonth, 1);

            for (var i = 0; i < 12; i++)
            {
                var startDate = fiscalStartDate.AddMonths(i);
                var endDate = startDate.AddMonths(1).AddDays(-1);

                periods.Add(new AccountingPeriod
                {
                    CompanyId = companyId,
                    FiscalYear = fiscalYear,
                    PeriodNumber = i + 1,
                    Name = startDate.ToString("MMM yyyy"),
                    StartDate = startDate,
                    EndDate = endDate,
                    IsClosed = false
                });
            }

            return periods;
        }
    }
}
