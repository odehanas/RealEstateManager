using Microsoft.AspNetCore.Mvc.Filters;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Refuses a form post that leaves out fields the record cannot be without.
    ///
    /// Model binding treats a field that was not sent at all like one that was left as it
    /// was: a missing Name binds as empty, without a validation error, and the edit copies
    /// that empty value onto the record. A post from the wrong page, a page saved half-loaded,
    /// or a hand-made request would blank the record. A field sent empty is still left to the
    /// action's own validation; only a field missing from the post is refused here.
    ///
    /// It adds the error to ModelState, and the action returns the form as for any other
    /// error, so nothing is saved. The fields are form field names, as the form sends them.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RequireFormFieldsAttribute : ActionFilterAttribute
    {
        public const string Message = "The form was incomplete, so nothing was saved. Open the page again and save it from there.";

        private readonly string[] _fields;

        public RequireFormFieldsAttribute(params string[] fields)
        {
            _fields = fields;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var request = context.HttpContext.Request;
            if (!HttpMethods.IsPost(request.Method))
                return;

            var form = request.HasFormContentType ? request.Form : null;
            var missing = _fields.Where(field => form == null || !form.ContainsKey(field)).ToList();
            if (missing.Count == 0)
                return;

            context.ModelState.AddModelError(string.Empty, Message);
            foreach (var field in missing)
                context.ModelState.AddModelError(field, Message);
        }
    }
}
