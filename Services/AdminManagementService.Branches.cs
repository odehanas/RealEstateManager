using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Branches. A company's administrator opens a branch under the mother company: a
    /// company of its own, with its own books, that works on the mother company's
    /// subscription and is added to its group reports. A branch has no branches.
    /// </summary>
    public partial class AdminManagementService
    {
        /// <summary>The mother company of the group this company belongs to (itself, if it is not a branch).</summary>
        public async Task<int> GetMotherCompanyIdAsync(int companyId)
        {
            var parentId = await _db.Companies
                .IgnoreQueryFilters()
                .Where(x => x.Id == companyId)
                .Select(x => x.ParentCompanyId)
                .FirstOrDefaultAsync();

            return parentId ?? companyId;
        }

        /// <summary>
        /// Opening a branch is for whoever administers the mother company (or a System
        /// Admin): administering one branch does not let someone add to the group.
        /// </summary>
        public async Task<bool> CanOpenBranchAsync(System.Security.Claims.ClaimsPrincipal principal, int companyId)
        {
            if (principal.IsInRole("SystemAdmin"))
                return true;

            var userId = principal.GetAppUserId();
            if (!userId.HasValue)
                return false;

            var motherId = await GetMotherCompanyIdAsync(companyId);
            return await UserManagerAuthorizationHandler.IsCompanyAdminAsync(_db, userId.Value, motherId);
        }

        public async Task<BranchListViewModel> GetBranchesAsync(System.Security.Claims.ClaimsPrincipal principal, int companyId)
        {
            var motherId = await GetMotherCompanyIdAsync(companyId);
            var userId = principal.GetAppUserId() ?? 0;

            var companies = await _db.Companies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.Id == motherId || x.ParentCompanyId == motherId)
                .Select(x => new BranchListItemViewModel
                {
                    CompanyId = x.Id,
                    Name = x.Name,
                    IsMother = x.Id == motherId,
                    CreatedAt = x.CreatedAt,
                    UsersCount = _db.AppUserCompanies.IgnoreQueryFilters()
                        .Count(link => link.CompanyId == x.Id && !link.AppUser!.IsSystemAdmin),
                    HasAccess = _db.AppUserCompanies.IgnoreQueryFilters()
                        .Any(link => link.CompanyId == x.Id && link.AppUserId == userId)
                })
                .ToListAsync();

            return new BranchListViewModel
            {
                CurrentCompanyId = companyId,
                MotherCompanyName = companies.First(x => x.IsMother).Name,
                Companies = companies.OrderByDescending(x => x.IsMother).ThenBy(x => x.Name).ToList(),
                CanOpenBranch = await CanOpenBranchAsync(principal, companyId)
            };
        }

        public async Task<BranchFormViewModel> BuildBranchFormAsync(int companyId)
        {
            var motherId = await GetMotherCompanyIdAsync(companyId);
            var motherName = await _db.Companies.IgnoreQueryFilters()
                .Where(x => x.Id == motherId).Select(x => x.Name).FirstAsync();

            return new BranchFormViewModel { MotherCompanyName = motherName, Name = motherName + " - " };
        }

        /// <summary>
        /// Opens a branch of the group the acting user is working in. The branch takes the
        /// mother company's currency and financial year (the group reports add the
        /// companies together, so these must agree) and its identity and control settings.
        /// </summary>
        public async Task<int> OpenBranchAsync(System.Security.Claims.ClaimsPrincipal principal, int companyId, BranchFormViewModel model)
        {
            if (!await CanOpenBranchAsync(principal, companyId))
                throw new InvalidOperationException("Only an administrator of the mother company can open a branch.");

            var motherId = await GetMotherCompanyIdAsync(companyId);
            var mother = await _db.Companies.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == motherId);

            var name = (model.Name ?? string.Empty).Trim();
            if (name.Length == 0 || string.Equals(name, mother.Name + " -", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Give the branch a name.");

            if (await _db.Companies.IgnoreQueryFilters().AnyAsync(x => x.Name == name))
                throw new InvalidOperationException("A company with this name already exists. Give the branch a different name.");

            await using var transaction = await _db.Database.BeginTransactionAsync();

            var branch = new Company
            {
                ParentCompanyId = mother.Id,
                Name = name,
                BaseCurrency = mother.BaseCurrency,
                FiscalYearStartMonth = mother.FiscalYearStartMonth,
                CreatedAt = DateTime.UtcNow,

                // One legal entity: the branch is invoiced and taxed as the mother company is.
                LegalName = mother.LegalName ?? mother.Name,
                TrnNumber = mother.TrnNumber,
                TaxIdentificationNumber = mother.TaxIdentificationNumber,
                TradeLicenseNumber = mother.TradeLicenseNumber,
                LegalRegistrationType = mother.LegalRegistrationType,
                LicensingAuthority = mother.LicensingAuthority,
                AddressLine1 = mother.AddressLine1,
                AddressLine2 = mother.AddressLine2,
                City = mother.City,
                Country = mother.Country,
                CountryCode = mother.CountryCode,
                Emirate = mother.Emirate,
                Phone = mother.Phone,
                Email = mother.Email,
                ProvisionalVatRecoveryRate = mother.ProvisionalVatRecoveryRate,

                // The same controls as the mother company; each can be changed on the branch afterwards.
                RequireContractApproval = mother.RequireContractApproval,
                RequirePaymentApproval = mother.RequirePaymentApproval,
                RequireTwoFactor = mother.RequireTwoFactor,
                AllowSelfReleaseWithinLimit = mother.AllowSelfReleaseWithinLimit,
                ApplyLimitsToContracts = mother.ApplyLimitsToContracts,
                OwnerCostTreatment = mother.OwnerCostTreatment,
                PdcWithdrawalCharge = mother.PdcWithdrawalCharge,
                PdcBounceCharge = mother.PdcBounceCharge
            };

            _db.Companies.Add(branch);
            await _db.SaveChangesAsync();

            await EnsureCompanySetupAsync(branch.Id);

            // Every company of the group gets the accounts that carry what they do with one another.
            var groupIds = await _db.Companies.IgnoreQueryFilters()
                .Where(x => x.Id == mother.Id || x.ParentCompanyId == mother.Id)
                .Select(x => x.Id)
                .ToListAsync();
            foreach (var groupCompanyId in groupIds)
                await EnsureIntraGroupAccountsAsync(groupCompanyId);

            // Who works in the branch: its opener administers it; the mother company's people
            // follow with the rights they hold there, when asked.
            var actingUserId = principal.GetAppUserId();
            var links = new Dictionary<int, bool>();
            if (model.GiveMotherCompanyUsersAccess)
            {
                var motherLinks = await _db.AppUserCompanies.IgnoreQueryFilters()
                    .Where(x => x.CompanyId == mother.Id && !x.AppUser!.IsSystemAdmin)
                    .Select(x => new { x.AppUserId, x.IsCompanyAdmin })
                    .ToListAsync();
                foreach (var link in motherLinks)
                    links[link.AppUserId] = link.IsCompanyAdmin;
            }

            if (actingUserId.HasValue && !principal.IsInRole("SystemAdmin"))
                links[actingUserId.Value] = true;

            foreach (var (userId, isAdmin) in links)
            {
                _db.AppUserCompanies.Add(new AppUserCompany
                {
                    AppUserId = userId,
                    CompanyId = branch.Id,
                    IsDefault = false,
                    IsCompanyAdmin = isAdmin
                });
            }

            await _db.SaveChangesAsync();
            await GrantSystemAdminsAccessAsync(branch.Id);

            await transaction.CommitAsync();
            return branch.Id;
        }

        /// <summary>
        /// The standard intra-group accounts, added to a company that lacks them. They are
        /// marked intra-group, which is what the consolidated report removes; a company may
        /// mark further accounts of its own in the chart of accounts.
        /// </summary>
        private async Task EnsureIntraGroupAccountsAsync(int companyId)
        {
            var existing = await _db.Accounts.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId)
                .Select(x => new { x.Id, x.Code })
                .ToListAsync();
            var idByCode = existing.GroupBy(x => x.Code).ToDictionary(g => g.Key, g => g.Min(x => x.Id));

            foreach (var (code, name, type, reportingGroup, parentCode) in IntraGroupAccounts)
            {
                if (idByCode.ContainsKey(code))
                    continue;

                _db.Accounts.Add(new Account
                {
                    CompanyId = companyId,
                    Code = code,
                    Name = name,
                    AccountType = type,
                    ReportingGroup = reportingGroup,
                    ParentAccountId = idByCode.TryGetValue(parentCode, out var parentId) ? parentId : null,
                    IsGroup = false,
                    IsPostable = true,
                    IsActive = true,
                    IsIntraGroup = true,
                    SortOrder = int.Parse(code)
                });
            }

            await _db.SaveChangesAsync();
        }

        private static readonly (string Code, string Name, string Type, string ReportingGroup, string ParentCode)[] IntraGroupAccounts =
        {
            ("11700", "Due from Group Companies", "Asset", "Assets", "10000"),
            ("21900", "Due to Group Companies", "Liability", "Liabilities", "20000"),
            ("45100", "Intra-Group Revenue", "Revenue", "Revenue", "40000"),
            ("66100", "Intra-Group Charges", "Expense", "GeneralExpense", "60000")
        };
    }
}
