using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    public class AuditLogService
    {
        private readonly ApplicationDbContext _db;

        public AuditLogService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task WriteAsync(AuditLog log)
        {
            try
            {
                _db.AuditLogs.Add(log);
                await _db.SaveChangesAsync();
            }
            catch
            {
                // Never break the main request because audit persistence failed.
            }
        }

        public async Task<AuditLogListViewModel> GetLogsAsync(AuditLogFilterViewModel filter)
        {
            var fromDate = filter.FromDate?.Date;
            var toDateExclusive = filter.ToDate?.Date.AddDays(1);

            var query = _db.AuditLogs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AsQueryable();

            if (fromDate.HasValue)
                query = query.Where(x => x.OccurredAt >= fromDate.Value);

            if (toDateExclusive.HasValue)
                query = query.Where(x => x.OccurredAt < toDateExclusive.Value);

            if (!string.IsNullOrWhiteSpace(filter.Username))
            {
                var username = filter.Username.Trim().ToUpperInvariant();
                query = query.Where(x => x.Username != null && x.Username.Contains(username));
            }

            if (!string.IsNullOrWhiteSpace(filter.ControllerName))
            {
                var controller = filter.ControllerName.Trim();
                query = query.Where(x => x.ControllerName == controller);
            }

            if (!string.IsNullOrWhiteSpace(filter.HttpMethod))
            {
                var method = filter.HttpMethod.Trim().ToUpperInvariant();
                query = query.Where(x => x.HttpMethod == method);
            }

            if (filter.OnlyFailures)
                query = query.Where(x => !x.IsSuccess);

            var items = await query
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.Id)
                .Take(500)
                .Select(x => new AuditLogListItemViewModel
                {
                    Id = x.Id,
                    Username = x.Username,
                    DisplayName = x.DisplayName,
                    CompanyName = x.CompanyName,
                    HttpMethod = x.HttpMethod,
                    RequestPath = x.RequestPath,
                    ControllerName = x.ControllerName,
                    ActionName = x.ActionName,
                    ActivityName = x.ActivityName,
                    IsSuccess = x.IsSuccess,
                    StatusCode = x.StatusCode,
                    ErrorMessage = x.ErrorMessage,
                    OccurredAt = x.OccurredAt,
                    IpAddress = x.IpAddress
                })
                .ToListAsync();

            var controllers = await _db.AuditLogs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.ControllerName != null && x.ControllerName != string.Empty)
                .Select(x => x.ControllerName!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            return new AuditLogListViewModel
            {
                Filter = filter,
                AvailableControllers = controllers,
                Items = items
            };
        }
    }
}
