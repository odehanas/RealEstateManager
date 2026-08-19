using Microsoft.AspNetCore.Http;

namespace RealEstateManager.Services
{
    public interface ICurrentCompanyAccessor
    {
        int? UserId { get; }
        int? CompanyId { get; }
        bool IsAuthenticated { get; }
    }

    public class CurrentCompanyAccessor : ICurrentCompanyAccessor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentCompanyAccessor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public int? UserId => _httpContextAccessor.HttpContext?.User.GetAppUserId();

        public int? CompanyId => _httpContextAccessor.HttpContext?.User.GetCurrentCompanyId();

        public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;
    }
}
