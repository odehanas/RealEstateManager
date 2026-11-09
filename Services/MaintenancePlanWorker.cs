using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Runs the planned maintenance programme: a minute after the site starts and every
    /// few hours after, each company's plans that have come due open their jobs. A company
    /// whose subscription has lapsed is skipped; its plans catch up once it is renewed.
    /// </summary>
    public sealed class MaintenancePlanWorker : BackgroundService
    {
        private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan Interval = TimeSpan.FromHours(4);

        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<MaintenancePlanWorker> _logger;

        public MaintenancePlanWorker(IServiceScopeFactory scopes, ILogger<MaintenancePlanWorker> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(FirstRun, stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    await RunOnceAsync(stoppingToken);
                    await Task.Delay(Interval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // The site is stopping.
            }
        }

        private async Task RunOnceAsync(CancellationToken stoppingToken)
        {
            List<int> companies;
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                companies = await db.MaintenancePlans.IgnoreQueryFilters()
                    .Where(x => x.IsActive)
                    .Select(x => x.CompanyId)
                    .Distinct()
                    .ToListAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The planned maintenance programme could not read its plans.");
                return;
            }

            foreach (var companyId in companies)
            {
                if (stoppingToken.IsCancellationRequested)
                    return;

                try
                {
                    // Each company in a scope of its own, filtered to that company.
                    using var scope = _scopes.CreateScope();
                    scope.ServiceProvider.GetRequiredService<CompanyContextOverride>().CompanyId = companyId;

                    if (!await scope.ServiceProvider.GetRequiredService<SubscriptionService>().IsCompanyAccessibleAsync(companyId))
                        continue;

                    var opened = await scope.ServiceProvider.GetRequiredService<MaintenanceService>()
                        .OpenDueJobsAsync(companyId, scope.ServiceProvider.GetRequiredService<ServiceRequestService>(), DateTime.Today);
                    if (opened > 0)
                        _logger.LogInformation("Planned maintenance opened {Count} job(s) for company {CompanyId}.", opened, companyId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Planned maintenance failed for company {CompanyId}.", companyId);
                }
            }
        }
    }
}
