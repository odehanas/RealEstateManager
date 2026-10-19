using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The platform owner is whoever runs this system as a service: the one who sells
    /// subscriptions to client companies. It is a different thing from a System Admin.
    ///
    /// A System Admin helps run the system - users, permissions, audit logs. The owner
    /// alone controls what customers pay for: activation codes, companies, and who is
    /// made a System Admin. If that were just another tick box on the Users screen,
    /// any System Admin could tick it for themselves or for a client, and hand out free
    /// subscription time.
    ///
    /// So the owner is named in the server configuration (PlatformOwner:Usernames in
    /// appsettings.json), which only whoever controls the server can change. Nothing
    /// inside the application can make someone the owner.
    /// </summary>
    public class PlatformOwnerOptions
    {
        public List<string> Usernames { get; set; } = new();
    }

    public class PlatformOwnerService
    {
        // Used only when the setting is missing, so a deployment without it is not
        // left with no owner at all. ADMIN is the account the system creates at setup.
        private const string DefaultOwnerUsername = "ADMIN";

        private readonly IOptionsMonitor<PlatformOwnerOptions> _options;

        public PlatformOwnerService(IOptionsMonitor<PlatformOwnerOptions> options)
        {
            _options = options;
        }

        public IReadOnlyCollection<string> OwnerUsernames
        {
            get
            {
                var names = _options.CurrentValue.Usernames
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(Normalize)
                    .Distinct()
                    .ToList();

                return names.Count > 0 ? names : new[] { DefaultOwnerUsername };
            }
        }

        public bool IsOwnerUsername(string? username) =>
            !string.IsNullOrWhiteSpace(username) && OwnerUsernames.Contains(Normalize(username));

        /// <summary>
        /// The owner must also be a System Admin. That flag can only be changed by the
        /// owner, so a missing flag is a deliberate demotion and is respected.
        /// </summary>
        public bool IsOwner(ClaimsPrincipal user) =>
            user.IsInRole("SystemAdmin") &&
            IsOwnerUsername(user.FindFirstValue(AppClaimTypes.Username));

        private static string Normalize(string username) => username.Trim().ToUpperInvariant();
    }

    public class PlatformOwnerRequirement : IAuthorizationRequirement
    {
        public const string PolicyName = "PlatformOwner";
    }

    public class PlatformOwnerAuthorizationHandler : AuthorizationHandler<PlatformOwnerRequirement>
    {
        private readonly PlatformOwnerService _platformOwnerService;

        public PlatformOwnerAuthorizationHandler(PlatformOwnerService platformOwnerService)
        {
            _platformOwnerService = platformOwnerService;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PlatformOwnerRequirement requirement)
        {
            if (_platformOwnerService.IsOwner(context.User))
                context.Succeed(requirement);

            return Task.CompletedTask;
        }
    }
}
