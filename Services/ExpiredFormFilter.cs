using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace RealEstateManager.Services
{
    /// <summary>
    /// A form whose anti-forgery token is no longer valid (opened before a restart, or
    /// left open past the sign-in) would get a bare "400 Bad Request". A browser form is
    /// instead sent back to the page it came from with a message; nothing was saved.
    /// Script calls (JSON) keep the 400 so the calling code sees the failure.
    /// </summary>
    public sealed class ExpiredFormFilter : IAlwaysRunResultFilter
    {
        public const string MessageKey = "FormExpired";

        public void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.Result is not IAntiforgeryValidationFailedResult)
                return;

            var request = context.HttpContext.Request;
            if (!request.HasFormContentType)
                return;

            var tempData = context.HttpContext.RequestServices
                .GetRequiredService<ITempDataDictionaryFactory>()
                .GetTempData(context.HttpContext);
            tempData[MessageKey] = "The page had expired, so nothing was saved. Please enter it again.";
            tempData.Save(); // the framework's own save may already have run for this request

            context.Result = new RedirectResult(ReturnPath(request));
        }

        public void OnResultExecuted(ResultExecutedContext context)
        {
        }

        // Back to the page the form was on, when it is on this site; otherwise home.
        private static string ReturnPath(HttpRequest request)
        {
            if (Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer) &&
                string.Equals(referer.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase))
            {
                var local = referer.PathAndQuery;
                if (local.StartsWith('/') && !local.StartsWith("//") && !local.StartsWith("/\\"))
                    return local;
            }

            return "/";
        }
    }
}
