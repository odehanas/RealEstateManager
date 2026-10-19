using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    public class AdminManagementService
    {
        private readonly ApplicationDbContext _db;
        private readonly PasswordHashingService _passwordHashingService;
        private readonly SubscriptionService _subscriptionService;
        private readonly PlatformOwnerService _platformOwnerService;

        public AdminManagementService(
            ApplicationDbContext db,
            PasswordHashingService passwordHashingService,
            SubscriptionService subscriptionService,
            PlatformOwnerService platformOwnerService)
        {
            _db = db;
            _passwordHashingService = passwordHashingService;
            _subscriptionService = subscriptionService;
            _platformOwnerService = platformOwnerService;
        }

        public async Task<CompanyListPageViewModel> GetCompaniesAsync()
        {
            var companies = await _db.Companies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new CompanyListItemViewModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    BaseCurrency = x.BaseCurrency,
                    FiscalYearStartMonth = x.FiscalYearStartMonth,
                    CreatedAt = x.CreatedAt,
                    UsersCount = _db.AppUserCompanies
                        .IgnoreQueryFilters()
                        .Count(link => link.CompanyId == x.Id)
                })
                .ToListAsync();

            foreach (var company in companies)
            {
                var status = await _subscriptionService.GetStatusAsync(company.Id);
                company.SubscriptionStatus = status.StatusLabel;
                company.AccessEndDate = status.AccessEndDate;
                company.DaysRemaining = status.DaysRemaining;
                company.ActivationCodesIssued = await _db.SubscriptionActivationCodes
                    .IgnoreQueryFilters()
                    .CountAsync(x => x.CompanyId == company.Id);
                company.ActivationCodesUsed = await _db.SubscriptionActivationCodes
                    .IgnoreQueryFilters()
                    .CountAsync(x => x.CompanyId == company.Id && x.ActivatedAt.HasValue);
            }

            return new CompanyListPageViewModel
            {
                Items = companies,
                ExpiredCompanies = await _subscriptionService.GetExpiredCompaniesAsync(),
                ExpiringSoonCompanies = await _subscriptionService.GetExpiringCompaniesAsync()
            };
        }

        public async Task<CompanyFormViewModel?> GetCompanyFormAsync(int id)
        {
            var model = await _db.Companies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new CompanyFormViewModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    BaseCurrency = x.BaseCurrency,
                    FiscalYearStartMonth = x.FiscalYearStartMonth
                })
                .FirstOrDefaultAsync();

            if (model == null)
                return null;

            var status = await _subscriptionService.GetStatusAsync(id);
            model.SubscriptionStatus = status.StatusLabel;
            model.TrialStartDate = status.TrialStartDate;
            model.TrialEndDate = status.TrialEndDate;
            model.SubscriptionEndDate = status.SubscriptionEndDate;
            model.AccessEndDate = status.AccessEndDate;
            model.DaysRemaining = status.DaysRemaining;
            model.ActivationCodes = (await _subscriptionService.GetActivationCodesAsync(id, 50)).ToList();
            return model;
        }

        public async Task<int> CreateCompanyAsync(CompanyFormViewModel model)
        {
            var normalizedName = model.Name.Trim();

            if (await _db.Companies.IgnoreQueryFilters().AnyAsync(x => x.Name == normalizedName))
                throw new InvalidOperationException("Company name already exists.");

            var company = new Company
            {
                Name = normalizedName,
                BaseCurrency = model.BaseCurrency.Trim().ToUpperInvariant(),
                FiscalYearStartMonth = model.FiscalYearStartMonth,
                CreatedAt = DateTime.UtcNow
            };

            _db.Companies.Add(company);
            await _db.SaveChangesAsync();

            await _subscriptionService.EnsureTrialSubscriptionAsync(company.Id);
            await EnsureCompanySetupAsync(company.Id);
            await GrantSystemAdminsAccessAsync(company.Id);

            return company.Id;
        }

        public async Task<int> RegisterCompanyAsync(RegisterCompanyViewModel model)
        {
            var normalizedCompanyName = model.CompanyName.Trim();
            var normalizedUsername = model.Username.Trim().ToUpperInvariant();

            if (await _db.Companies.IgnoreQueryFilters().AnyAsync(x => x.Name == normalizedCompanyName))
                throw new InvalidOperationException("Company name already exists.");

            if (await _db.AppUsers.IgnoreQueryFilters().AnyAsync(x => x.Username == normalizedUsername))
                throw new InvalidOperationException("Username already exists.");

            var company = new Company
            {
                Name = normalizedCompanyName,
                BaseCurrency = model.BaseCurrency.Trim().ToUpperInvariant(),
                FiscalYearStartMonth = model.FiscalYearStartMonth,
                CreatedAt = DateTime.UtcNow
            };

            _db.Companies.Add(company);
            await _db.SaveChangesAsync();

            var user = new AppUser
            {
                Username = normalizedUsername,
                DisplayName = model.DisplayName.Trim(),
                PasswordHash = _passwordHashingService.HashPassword(model.Password),
                IsActive = true,
                IsSystemAdmin = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.AppUsers.Add(user);
            await _db.SaveChangesAsync();

            _db.AppUserCompanies.Add(new AppUserCompany
            {
                AppUserId = user.Id,
                CompanyId = company.Id,
                IsDefault = true
            });

            _db.AppUserPermissions.AddRange(
                PermissionCatalog.All.Select(permission => new AppUserPermission
                {
                    AppUserId = user.Id,
                    PermissionKey = permission.Key,
                    GrantedAt = DateTime.UtcNow
                }));

            await _db.SaveChangesAsync();

            await _subscriptionService.EnsureTrialSubscriptionAsync(company.Id);
            await EnsureCompanySetupAsync(company.Id);
            await GrantSystemAdminsAccessAsync(company.Id);

            return user.Id;
        }

        public async Task UpdateCompanyAsync(CompanyFormViewModel model)
        {
            var company = await _db.Companies
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == model.Id);

            if (company == null)
                throw new InvalidOperationException("Company was not found.");

            var normalizedName = model.Name.Trim();

            if (await _db.Companies
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Name == normalizedName && x.Id != company.Id))
            {
                throw new InvalidOperationException("Company name already exists.");
            }

            company.Name = normalizedName;
            company.BaseCurrency = model.BaseCurrency.Trim().ToUpperInvariant();
            company.FiscalYearStartMonth = model.FiscalYearStartMonth;

            await _db.SaveChangesAsync();
            await _subscriptionService.EnsureTrialSubscriptionAsync(company.Id);
            await EnsureCompanySetupAsync(company.Id);
        }

        public async Task<List<AppUserListItemViewModel>> GetUsersAsync()
        {
            var users = await _db.AppUsers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(x => x.CompanyAccesses)
                    .ThenInclude(x => x.Company)
                .Include(x => x.Permissions)
                .OrderBy(x => x.Username)
                .ToListAsync();

            return users.Select(x => new AppUserListItemViewModel
            {
                Id = x.Id,
                Username = x.Username,
                DisplayName = x.DisplayName,
                IsActive = x.IsActive,
                IsSystemAdmin = x.IsSystemAdmin,
                CreatedAt = x.CreatedAt,
                LastLoginAt = x.LastLoginAt,
                CompaniesDisplay = string.Join(", ",
                    x.CompanyAccesses
                        .OrderByDescending(link => link.IsDefault)
                        .ThenBy(link => link.Company!.Name)
                        .Select(link => $"{link.Company!.Name}{(link.IsDefault ? " (Default)" : string.Empty)}")),
                PermissionsDisplay = x.IsSystemAdmin
                    ? "Full Access"
                    : string.Join(", ",
                        x.Permissions
                            .OrderBy(p => p.PermissionKey)
                            .Select(p => PermissionCatalog.All.FirstOrDefault(def => def.Key == p.PermissionKey)?.Name ?? p.PermissionKey))
            }).ToList();
        }

        public async Task<AppUserFormViewModel> BuildUserFormAsync(int? id = null)
        {
            var companies = await _db.Companies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new SelectableCompanyViewModel
                {
                    CompanyId = x.Id,
                    CompanyName = x.Name
                })
                .ToListAsync();

            if (!id.HasValue)
            {
                return new AppUserFormViewModel
                {
                    IsActive = true,
                    Companies = companies,
                    PermissionGroups = BuildPermissionGroups(Array.Empty<string>())
                };
            }

            var user = await _db.AppUsers
                .IgnoreQueryFilters()
                .Include(x => x.CompanyAccesses)
                .Include(x => x.Permissions)
                .FirstOrDefaultAsync(x => x.Id == id.Value);

            if (user == null)
                throw new InvalidOperationException("User was not found.");

            foreach (var company in companies)
            {
                company.IsSelected = user.CompanyAccesses.Any(x => x.CompanyId == company.CompanyId);
            }

            return new AppUserFormViewModel
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                IsActive = user.IsActive,
                IsSystemAdmin = user.IsSystemAdmin,
                DefaultCompanyId = user.CompanyAccesses.FirstOrDefault(x => x.IsDefault)?.CompanyId,
                Companies = companies,
                PermissionGroups = BuildPermissionGroups(user.Permissions.Select(x => x.PermissionKey))
            };
        }

        /// <param name="actingUserIsOwner">
        /// Whether the person saving the form is the platform owner. Only the owner may
        /// make someone a System Admin or create an owner account.
        /// </param>
        public async Task<int> CreateUserAsync(AppUserFormViewModel model, bool actingUserIsOwner)
        {
            var normalizedUsername = model.Username.Trim().ToUpperInvariant();

            if (await _db.AppUsers.IgnoreQueryFilters().AnyAsync(x => x.Username == normalizedUsername))
                throw new InvalidOperationException("Username already exists.");

            if (!actingUserIsOwner)
            {
                if (_platformOwnerService.IsOwnerUsername(normalizedUsername))
                    throw new InvalidOperationException("That username is reserved for the platform owner.");

                if (model.IsSystemAdmin)
                    throw new InvalidOperationException("Only the platform owner can make a user a System Admin.");
            }

            if (string.IsNullOrWhiteSpace(model.Password))
                throw new InvalidOperationException("Password is required for a new user.");

            var selectedCompanyIds = GetSelectedCompanyIds(model);
            ValidateSelectedCompanies(model, selectedCompanyIds);

            var user = new AppUser
            {
                Username = normalizedUsername,
                DisplayName = model.DisplayName.Trim(),
                PasswordHash = _passwordHashingService.HashPassword(model.Password),
                IsActive = model.IsActive,
                IsSystemAdmin = model.IsSystemAdmin,
                CreatedAt = DateTime.UtcNow
            };

            _db.AppUsers.Add(user);
            await _db.SaveChangesAsync();

            await SaveUserCompanyAccessesAsync(user, selectedCompanyIds, model.DefaultCompanyId!.Value);
            await SaveUserPermissionsAsync(user, model);

            return user.Id;
        }

        /// <param name="actingUserIsOwner">
        /// Whether the person saving the form is the platform owner. A System Admin who
        /// is not the owner may not change anyone's System Admin flag, nor touch an owner
        /// account at all - otherwise they could reset its password, or rename it and take
        /// its name, and become the owner.
        /// </param>
        public async Task UpdateUserAsync(AppUserFormViewModel model, bool actingUserIsOwner)
        {
            if (!model.Id.HasValue)
                throw new InvalidOperationException("User id is required.");

            var user = await _db.AppUsers
                .IgnoreQueryFilters()
                .Include(x => x.CompanyAccesses)
                .FirstOrDefaultAsync(x => x.Id == model.Id.Value);

            if (user == null)
                throw new InvalidOperationException("User was not found.");

            var normalizedUsername = model.Username.Trim().ToUpperInvariant();

            if (await _db.AppUsers
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Username == normalizedUsername && x.Id != user.Id))
            {
                throw new InvalidOperationException("Username already exists.");
            }

            var targetIsOwner = _platformOwnerService.IsOwnerUsername(user.Username);

            if (!actingUserIsOwner)
            {
                if (targetIsOwner)
                    throw new InvalidOperationException("Only the platform owner can change the owner account.");

                if (_platformOwnerService.IsOwnerUsername(normalizedUsername))
                    throw new InvalidOperationException("That username is reserved for the platform owner.");

                if (model.IsSystemAdmin != user.IsSystemAdmin)
                    throw new InvalidOperationException("Only the platform owner can grant or remove System Admin.");
            }

            // The owner is recognised by username, and must be an active System Admin.
            // Renaming, deactivating or demoting the account here would leave the system
            // without an owner; that is done by changing the setting on the server.
            if (targetIsOwner &&
                (!string.Equals(normalizedUsername, user.Username, StringComparison.OrdinalIgnoreCase) ||
                 !model.IsActive ||
                 !model.IsSystemAdmin))
            {
                throw new InvalidOperationException(
                    "The owner account must keep its username and stay an active System Admin. " +
                    "To change the owner, edit PlatformOwner:Usernames in appsettings.json on the server.");
            }

            var selectedCompanyIds = GetSelectedCompanyIds(model);
            ValidateSelectedCompanies(model, selectedCompanyIds);

            user.Username = normalizedUsername;
            user.DisplayName = model.DisplayName.Trim();
            user.IsActive = model.IsActive;
            user.IsSystemAdmin = model.IsSystemAdmin;

            if (!string.IsNullOrWhiteSpace(model.Password))
            {
                user.PasswordHash = _passwordHashingService.HashPassword(model.Password);
            }

            await SaveUserCompanyAccessesAsync(user, selectedCompanyIds, model.DefaultCompanyId!.Value);
            await SaveUserPermissionsAsync(user, model);
        }

        private static List<int> GetSelectedCompanyIds(AppUserFormViewModel model)
        {
            return model.Companies
                .Where(x => x.IsSelected)
                .Select(x => x.CompanyId)
                .Distinct()
                .ToList();
        }

        private static void ValidateSelectedCompanies(AppUserFormViewModel model, List<int> selectedCompanyIds)
        {
            if (selectedCompanyIds.Count == 0)
                throw new InvalidOperationException("Select at least one company for the user.");

            if (!model.DefaultCompanyId.HasValue || !selectedCompanyIds.Contains(model.DefaultCompanyId.Value))
                throw new InvalidOperationException("Default company must be one of the selected companies.");
        }

        private async Task SaveUserCompanyAccessesAsync(AppUser user, List<int> selectedCompanyIds, int defaultCompanyId)
        {
            var existingLinks = await _db.AppUserCompanies
                .IgnoreQueryFilters()
                .Where(x => x.AppUserId == user.Id)
                .ToListAsync();

            var toRemove = existingLinks.Where(x => !selectedCompanyIds.Contains(x.CompanyId)).ToList();
            if (toRemove.Count > 0)
            {
                _db.AppUserCompanies.RemoveRange(toRemove);
            }

            foreach (var companyId in selectedCompanyIds)
            {
                var link = existingLinks.FirstOrDefault(x => x.CompanyId == companyId);
                if (link == null)
                {
                    link = new AppUserCompany
                    {
                        AppUserId = user.Id,
                        CompanyId = companyId
                    };

                    _db.AppUserCompanies.Add(link);
                }

                link.IsDefault = companyId == defaultCompanyId;
            }

            await _db.SaveChangesAsync();
        }

        private async Task SaveUserPermissionsAsync(AppUser user, AppUserFormViewModel model)
        {
            var selectedPermissionKeys = model.PermissionGroups
                .SelectMany(x => x.Permissions)
                .Where(x => x.IsSelected)
                .Select(x => x.PermissionKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var existingPermissions = await _db.AppUserPermissions
                .IgnoreQueryFilters()
                .Where(x => x.AppUserId == user.Id)
                .ToListAsync();

            var toRemove = existingPermissions
                .Where(x => !selectedPermissionKeys.Contains(x.PermissionKey, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (toRemove.Count > 0)
            {
                _db.AppUserPermissions.RemoveRange(toRemove);
            }

            foreach (var permissionKey in selectedPermissionKeys)
            {
                if (existingPermissions.Any(x => x.PermissionKey.Equals(permissionKey, StringComparison.OrdinalIgnoreCase)))
                    continue;

                _db.AppUserPermissions.Add(new AppUserPermission
                {
                    AppUserId = user.Id,
                    PermissionKey = permissionKey,
                    GrantedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();
        }

        private static List<SelectablePermissionGroupViewModel> BuildPermissionGroups(IEnumerable<string> selectedPermissionKeys)
        {
            var selected = new HashSet<string>(selectedPermissionKeys, StringComparer.OrdinalIgnoreCase);

            return PermissionCatalog.All
                .GroupBy(x => x.Group)
                .OrderBy(x => x.Key)
                .Select(group => new SelectablePermissionGroupViewModel
                {
                    GroupName = group.Key,
                    Permissions = group
                        .OrderBy(x => x.Name)
                        .Select(permission => new SelectablePermissionViewModel
                        {
                            PermissionKey = permission.Key,
                            PermissionName = permission.Name,
                            Description = permission.Description,
                            IsSelected = selected.Contains(permission.Key)
                        })
                        .ToList()
                })
                .ToList();
        }

        private async Task EnsureCompanySetupAsync(int companyId)
        {
            var company = await _db.Companies
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == companyId);

            if (company == null)
                throw new InvalidOperationException("Company was not found.");

            await ChartOfAccountsSeeder.EnsureCompanySetupAsync(_db, company);
        }

        /// <summary>
        /// Opens a separate practice company for a newly registered user, next to their
        /// real one: "<name> - Training". It is created empty; the sample data goes in
        /// afterwards. The user's real company stays their default, so they always land
        /// there and switch to the training copy when they want to practise.
        /// </summary>
        public async Task<int> CreateTrainingCompanyAsync(int userId, string realCompanyName)
        {
            var realCompany = await _db.Companies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Name == realCompanyName.Trim());

            var baseName = realCompanyName.Trim() + SampleDataService.TrainingSuffix;
            var name = baseName;
            for (var n = 2; await _db.Companies.IgnoreQueryFilters().AnyAsync(x => x.Name == name); n++)
                name = $"{baseName} {n}";

            var company = new Company
            {
                Name = name,
                BaseCurrency = realCompany?.BaseCurrency ?? "AED",
                FiscalYearStartMonth = realCompany?.FiscalYearStartMonth ?? 1,
                CreatedAt = DateTime.UtcNow
            };

            _db.Companies.Add(company);
            await _db.SaveChangesAsync();

            // Practice data is no use if it locks after the 15-day trial, so the training
            // copy runs for a year. It holds only fictional records.
            await _subscriptionService.EnsureTrialSubscriptionAsync(company.Id, trialDays: 365);
            await EnsureCompanySetupAsync(company.Id);

            _db.AppUserCompanies.Add(new AppUserCompany
            {
                AppUserId = userId,
                CompanyId = company.Id,
                IsDefault = false
            });
            await _db.SaveChangesAsync();

            await GrantSystemAdminsAccessAsync(company.Id);
            return company.Id;
        }

        private async Task GrantSystemAdminsAccessAsync(int companyId)
        {
            var systemAdminIds = await _db.AppUsers
                .IgnoreQueryFilters()
                .Where(x => x.IsSystemAdmin)
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var userId in systemAdminIds)
            {
                var exists = await _db.AppUserCompanies
                    .IgnoreQueryFilters()
                    .AnyAsync(x => x.AppUserId == userId && x.CompanyId == companyId);

                if (exists)
                    continue;

                var hasDefault = await _db.AppUserCompanies
                    .IgnoreQueryFilters()
                    .AnyAsync(x => x.AppUserId == userId && x.IsDefault);

                _db.AppUserCompanies.Add(new AppUserCompany
                {
                    AppUserId = userId,
                    CompanyId = companyId,
                    IsDefault = !hasDefault
                });
            }

            await _db.SaveChangesAsync();
        }
    }
}
