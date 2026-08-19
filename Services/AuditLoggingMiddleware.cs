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
                if (endpoint != null)
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
                        ErrorMessage = requestException?.Message,
                        IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                        OccurredAt = occurredAt
                    };

                    await auditLogService.WriteAsync(log);
                }
            }
        }
    }
}
