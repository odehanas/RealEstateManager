using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;

namespace RealEstateManager.Services
{
    public class PermissionService
    {
        private const string CacheKey = "__user_permissions";

        private readonly ApplicationDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public PermissionService(ApplicationDbContext db, IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permissionKey)
        {
            if (string.IsNullOrWhiteSpace(permissionKey))
                return true;

            if (user.IsInRole("SystemAdmin"))
                return true;

            var permissions = await GetUserPermissionsAsync(user);
            return permissions.Contains(permissionKey);
        }

        public async Task<bool> HasAnyPermissionAsync(ClaimsPrincipal user, params string[] permissionKeys)
        {
            if (user.IsInRole("SystemAdmin"))
                return true;

            var requested = permissionKeys
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (requested.Count == 0)
                return true;

            var permissions = await GetUserPermissionsAsync(user);
            return requested.Any(permissions.Contains);
        }

        public async Task<HashSet<string>> GetUserPermissionsAsync(ClaimsPrincipal user)
        {
            var userId = user.GetAppUserId();
            if (!userId.HasValue)
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.Items.TryGetValue(CacheKey, out var cached) == true &&
                cached is HashSet<string> cachedPermissions)
            {
                return cachedPermissions;
            }

            var permissions = await _db.AppUserPermissions
                .IgnoreQueryFilters()
                .Where(x => x.AppUserId == userId.Value)
                .Select(x => x.PermissionKey)
                .ToListAsync();

            var result = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
            if (httpContext != null)
            {
                httpContext.Items[CacheKey] = result;
            }

            return result;
        }
    }
}
