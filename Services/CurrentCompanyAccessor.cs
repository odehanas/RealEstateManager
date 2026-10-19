using Microsoft.AspNetCore.Http;

namespace RealEstateManager.Services
{
    public interface ICurrentCompanyAccessor
    {
        int? UserId { get; }
        int? CompanyId { get; }
        bool IsAuthenticated { get; }
    }

    /// <summary>
    /// Lets work that is not a user's own request run inside one particular company:
    /// building a training company's sample data, for instance, while the person who
    /// asked for it is signed in to a different company. Set it on a fresh service scope
    /// before anything in that scope touches the database; every query in the scope is
    /// then filtered to that company exactly as it would be for a signed-in user.
    /// </summary>
    public class CompanyContextOverride
    {
        public int? CompanyId { get; set; }
        public int? UserId { get; set; }
    }

    public class CurrentCompanyAccessor : ICurrentCompanyAccessor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly CompanyContextOverride _override;

        public CurrentCompanyAccessor(IHttpContextAccessor httpContextAccessor, CompanyContextOverride companyOverride)
        {
            _httpContextAccessor = httpContextAccessor;
            _override = companyOverride;
        }

        public int? UserId => _override.CompanyId.HasValue
            ? _override.UserId
            : _httpContextAccessor.HttpContext?.User.GetAppUserId();

        public int? CompanyId => _override.CompanyId ?? _httpContextAccessor.HttpContext?.User.GetCurrentCompanyId();

        public bool IsAuthenticated =>
            _override.CompanyId.HasValue || _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;
    }
}
