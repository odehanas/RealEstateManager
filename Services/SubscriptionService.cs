using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    public class SubscriptionService
    {
        private readonly ApplicationDbContext _db;

        public SubscriptionService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task EnsureTrialSubscriptionAsync(int companyId, int trialDays = 15)
        {
            var subscription = await _db.CompanySubscriptions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.CompanyId == companyId);

            if (subscription != null)
                return;

            var companyExists = await _db.Companies
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Id == companyId);

            if (!companyExists)
                throw new InvalidOperationException("Company was not found.");

            var today = DateTime.Today;
            _db.CompanySubscriptions.Add(new CompanySubscription
            {
                CompanyId = companyId,
                TrialStartDate = today,
                TrialEndDate = today.AddDays(trialDays),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }

        public async Task<SubscriptionStatus> GetStatusAsync(int companyId)
        {
            await EnsureTrialSubscriptionAsync(companyId);

            var company = await _db.Companies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == companyId);

            if (company == null)
                throw new InvalidOperationException("Company was not found.");

            var subscription = await _db.CompanySubscriptions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CompanyId == companyId);

            if (subscription == null)
            {
                return new SubscriptionStatus
                {
                    CompanyId = company.Id,
                    CompanyName = company.Name,
                    IsExpired = true,
                    StatusLabel = "No Subscription Setup"
                };
            }

            var today = DateTime.Today;
            var accessEndDate = GetAccessEndDate(subscription);
            var daysRemaining = accessEndDate.HasValue
                ? Math.Max(0, (accessEndDate.Value.Date - today).Days)
                : 0;

            var isTrialActive = !subscription.IsSuspended && subscription.TrialEndDate.Date >= today;
            var isPaidActive = !subscription.IsSuspended &&
                               subscription.SubscriptionEndDate.HasValue &&
                               subscription.SubscriptionEndDate.Value.Date >= today;
            var isActive = isTrialActive || isPaidActive;

            var statusLabel = subscription.IsSuspended
                ? "Suspended"
                : isPaidActive
                    ? "Active Subscription"
                    : isTrialActive
                        ? "Trial Active"
                        : "Expired";

            return new SubscriptionStatus
            {
                CompanyId = company.Id,
                CompanyName = company.Name,
                TrialStartDate = subscription.TrialStartDate.Date,
                TrialEndDate = subscription.TrialEndDate.Date,
                SubscriptionEndDate = subscription.SubscriptionEndDate?.Date,
                AccessEndDate = accessEndDate?.Date,
                DaysRemaining = daysRemaining,
                IsInTrial = isTrialActive && !isPaidActive,
                IsExpired = !isActive,
                IsSuspended = subscription.IsSuspended,
                StatusLabel = statusLabel,
                Notes = subscription.Notes
            };
        }

        public async Task<bool> IsCompanyAccessibleAsync(int companyId)
        {
            var status = await GetStatusAsync(companyId);
            return !status.IsExpired;
        }

        public async Task<CurrentSubscriptionViewModel> BuildCurrentCompanyViewModelAsync(int companyId, string? returnUrl = null)
        {
            var status = await GetStatusAsync(companyId);
            return new CurrentSubscriptionViewModel
            {
                CompanyId = status.CompanyId,
                CompanyName = status.CompanyName,
                StatusLabel = status.StatusLabel,
                IsInTrial = status.IsInTrial,
                IsExpired = status.IsExpired,
                IsSuspended = status.IsSuspended,
                DaysRemaining = status.DaysRemaining,
                TrialStartDate = status.TrialStartDate,
                TrialEndDate = status.TrialEndDate,
                SubscriptionEndDate = status.SubscriptionEndDate,
                AccessEndDate = status.AccessEndDate,
                Notes = status.Notes,
                ReturnUrl = returnUrl
            };
        }

        public async Task<IReadOnlyList<SubscriptionActivationCodeListItemViewModel>> GetActivationCodesAsync(int companyId, int maxItems = 10)
        {
            return await _db.SubscriptionActivationCodes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.IssuedAt)
                .Take(maxItems)
                .Select(x => new SubscriptionActivationCodeListItemViewModel
                {
                    Code = x.Code,
                    DurationDays = x.DurationDays,
                    Notes = x.Notes,
                    IssuedAt = x.IssuedAt,
                    ActivatedAt = x.ActivatedAt,
                    IsCancelled = x.IsCancelled
                })
                .ToListAsync();
        }

        public async Task<List<CompanySubscriptionAlertItemViewModel>> GetExpiredCompaniesAsync()
        {
            return await GetCompaniesByAccessWindowAsync(includeExpired: true, expiringWithinDays: null);
        }

        public async Task<List<CompanySubscriptionAlertItemViewModel>> GetExpiringCompaniesAsync(int expiringWithinDays = 15)
        {
            return await GetCompaniesByAccessWindowAsync(includeExpired: false, expiringWithinDays: expiringWithinDays);
        }

        public async Task<string> IssueActivationCodeAsync(int companyId, int durationDays, string? notes = null)
        {
            if (durationDays <= 0)
                throw new InvalidOperationException("Activation code duration must be greater than zero.");

            await EnsureTrialSubscriptionAsync(companyId);

            string code;
            do
            {
                code = GenerateCode();
            }
            while (await _db.SubscriptionActivationCodes
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Code == code));

            _db.SubscriptionActivationCodes.Add(new SubscriptionActivationCode
            {
                CompanyId = companyId,
                Code = code,
                DurationDays = durationDays,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                IssuedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            return code;
        }

        public async Task ActivateCodeAsync(int companyId, string code)
        {
            var normalizedCode = NormalizeCode(code);
            if (string.IsNullOrWhiteSpace(normalizedCode))
                throw new InvalidOperationException("Activation code is required.");

            var subscription = await _db.CompanySubscriptions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.CompanyId == companyId);

            if (subscription == null)
                throw new InvalidOperationException("Subscription record was not found for the selected company.");

            var activationCode = await _db.SubscriptionActivationCodes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x =>
                    x.CompanyId == companyId &&
                    x.Code == normalizedCode &&
                    !x.IsCancelled &&
                    !x.ActivatedAt.HasValue);

            if (activationCode == null)
                throw new InvalidOperationException("Activation code is invalid or already used.");

            var baseDate = DateTime.Today;
            var currentAccessEndDate = GetAccessEndDate(subscription);
            if (currentAccessEndDate.HasValue && currentAccessEndDate.Value.Date > baseDate)
            {
                baseDate = currentAccessEndDate.Value.Date;
            }

            subscription.SubscriptionEndDate = baseDate.AddDays(activationCode.DurationDays);
            subscription.LastActivatedAt = DateTime.UtcNow;
            subscription.IsSuspended = false;
            subscription.UpdatedAt = DateTime.UtcNow;

            activationCode.ActivatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        public static DateTime? GetAccessEndDate(CompanySubscription subscription)
        {
            if (subscription.IsSuspended)
                return null;

            return MaxDate(subscription.TrialEndDate, subscription.SubscriptionEndDate);
        }

        private static DateTime? MaxDate(DateTime first, DateTime? second)
        {
            if (!second.HasValue)
                return first;

            return first >= second.Value ? first : second.Value;
        }

        private static string GenerateCode()
        {
            var random = Random.Shared;
            var part1 = random.Next(1000, 9999).ToString();
            var part2 = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
            var part3 = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
            return $"REM-{part1}-{part2}-{part3}";
        }

        private static string NormalizeCode(string? code)
        {
            return (code ?? string.Empty).Trim().ToUpperInvariant();
        }

        private async Task<List<CompanySubscriptionAlertItemViewModel>> GetCompaniesByAccessWindowAsync(bool includeExpired, int? expiringWithinDays)
        {
            var companies = await _db.Companies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name })
                .ToListAsync();

            var results = new List<CompanySubscriptionAlertItemViewModel>();

            foreach (var company in companies)
            {
                var status = await GetStatusAsync(company.Id);

                if (includeExpired)
                {
                    if (!status.IsExpired)
                        continue;
                }
                else
                {
                    if (status.IsExpired)
                        continue;

                    if (expiringWithinDays.HasValue && status.DaysRemaining > expiringWithinDays.Value)
                        continue;
                }

                results.Add(new CompanySubscriptionAlertItemViewModel
                {
                    CompanyId = status.CompanyId,
                    CompanyName = status.CompanyName,
                    SubscriptionStatus = status.StatusLabel,
                    AccessEndDate = status.AccessEndDate,
                    DaysRemaining = status.DaysRemaining
                });
            }

            return includeExpired
                ? results.OrderBy(x => x.CompanyName).ToList()
                : results.OrderBy(x => x.DaysRemaining).ThenBy(x => x.CompanyName).ToList();
        }
    }

    public sealed class SubscriptionStatus
    {
        public int CompanyId { get; init; }
        public string CompanyName { get; init; } = string.Empty;
        public DateTime TrialStartDate { get; init; }
        public DateTime TrialEndDate { get; init; }
        public DateTime? SubscriptionEndDate { get; init; }
        public DateTime? AccessEndDate { get; init; }
        public int DaysRemaining { get; init; }
        public bool IsInTrial { get; init; }
        public bool IsExpired { get; init; }
        public bool IsSuspended { get; init; }
        public string StatusLabel { get; init; } = string.Empty;
        public string? Notes { get; init; }
    }
}
