using Microsoft.AspNetCore.Http.Extensions;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    public class AuditLoggingMiddleware
    {
        private readonly RequestDelegate _next;

        public AuditLoggingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, AuditLogService auditLogService)
        {
            var occurredAt = DateTime.UtcNow;
            Exception? requestException = null;

            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                requestException = ex;
                throw;
            }
            finally
            {
                var endpoint = context.GetEndpoint();
                if (endpoint != null && !IsAssistantHousekeeping(context, requestException))
                {
                    var routeValues = context.Request.RouteValues;
                    var controllerName = routeValues.TryGetValue("controller", out var controller) ? controller?.ToString() : null;
                    var actionName = routeValues.TryGetValue("action", out var action) ? action?.ToString() : null;

                    var log = new AuditLog
                    {
                        AppUserId = context.User.GetAppUserId(),
                        CompanyId = context.User.GetCurrentCompanyId(),
                        Username = context.User.FindFirst(AppClaimTypes.Username)?.Value,
                        DisplayName = context.User.FindFirst(AppClaimTypes.DisplayName)?.Value,
                        CompanyName = context.User.FindFirst(AppClaimTypes.CurrentCompanyName)?.Value,
                        HttpMethod = context.Request.Method.ToUpperInvariant(),
                        RequestPath = context.Request.Path.HasValue ? context.Request.Path.Value! : context.Request.GetDisplayUrl(),
                        QueryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
                        ControllerName = controllerName,
                        ActionName = actionName,
                        ActivityName = controllerName != null && actionName != null ? $"{controllerName}/{actionName}" : endpoint.DisplayName,
                        IsSuccess = requestException == null && context.Response.StatusCode < 400,
                        StatusCode = requestException == null ? context.Response.StatusCode : StatusCodes.Status500InternalServerError,
                        // The column holds 1000 characters; a longer message would stop the log being written at all.
                        ErrorMessage = requestException?.Message is { Length: > 1000 } longMessage ? longMessage[..1000] : requestException?.Message,
                        IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                        OccurredAt = occurredAt
                    };

                    await auditLogService.WriteAsync(log);
                }
            }
        }

        /// <summary>
        /// The assistant panel's background calls - its transcript, token usage, lookups and
        /// the end of a session - are kept in the assistant's own tables. Logging each one here
        /// would bury real activity, so only their failures are recorded. Starting a session
        /// and confirming or cancelling an entry are still logged.
        /// </summary>
        private static readonly HashSet<string> AssistantHousekeeping =
            new(StringComparer.OrdinalIgnoreCase) { "Log", "Usage", "Tool", "End" };

        private static bool IsAssistantHousekeeping(HttpContext context, Exception? error) =>
            error == null && context.Response.StatusCode < 400 &&
            string.Equals(context.Request.RouteValues["controller"]?.ToString(), "Assistant", StringComparison.OrdinalIgnoreCase) &&
            AssistantHousekeeping.Contains(context.Request.RouteValues["action"]?.ToString() ?? string.Empty);
    }
}
