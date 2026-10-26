using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Services.Assistant;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The AI assistant's server side. Behind the same global filters as every staff
    /// screen (sign-in, portal boundary, subscription, Assistant.Use permission), and
    /// every call works out the user, company and permissions from the cookie alone.
    /// </summary>
    public class AssistantController : Controller
    {
        private readonly AssistantService _assistant;

        public AssistantController(AssistantService assistant)
        {
            _assistant = assistant;
        }

        public sealed class SessionRequest
        {
            public string? Mode { get; set; }
            public string? Screen { get; set; }
        }

        public sealed class ToolRequest
        {
            public int SessionId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? Arguments { get; set; }
            public string? Screen { get; set; }
        }

        public sealed class LogRequest
        {
            public int SessionId { get; set; }
            public string Role { get; set; } = "user";
            public string? Text { get; set; }
        }

        public sealed class UsageRequest
        {
            public int SessionId { get; set; }
            public JsonElement Usage { get; set; }
        }

        public sealed class EndRequest
        {
            public int SessionId { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Session([FromBody] SessionRequest request) =>
            Guard(async () =>
            {
                var start = await _assistant.StartSessionAsync(User, request.Mode, request.Screen);
                return Json(new
                {
                    sessionId = start.SessionId,
                    clientSecret = start.ClientSecret,
                    model = start.Model,
                    expiresAt = start.ExpiresAt,
                    maxMinutes = start.MaxMinutes,
                    mode = start.Mode
                });
            });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Tool([FromBody] ToolRequest request) =>
            Guard(async () =>
            {
                var outcome = await _assistant.RunToolAsync(User, request.SessionId, request.Name, request.Arguments, request.Screen);
                return Json(new { output = outcome.Output, pendingAction = outcome.PendingAction });
            });

        /// <summary>A scanned invoice attached in the panel, read for the model. Nothing is saved.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(12 * 1024 * 1024)]
        public Task<IActionResult> ReadDocument([FromForm] int sessionId, IFormFile? file) =>
            Guard(async () => Json(new { output = await _assistant.ReadDocumentAsync(User, sessionId, file) }));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Confirm(int id) =>
            Guard(async () => Json(await _assistant.ConfirmAsync(User, id)));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Cancel(int id) =>
            Guard(async () =>
            {
                await _assistant.CancelAsync(User, id);
                return Json(new { ok = true });
            });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Log([FromBody] LogRequest request) =>
            Guard(async () =>
            {
                await _assistant.LogMessageAsync(User, request.SessionId, request.Role, request.Text);
                return Json(new { ok = true });
            });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Usage([FromBody] UsageRequest request) =>
            Guard(async () =>
            {
                await _assistant.RecordUsageAsync(User, request.SessionId, request.Usage);
                return Json(new { ok = true });
            });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> End([FromBody] EndRequest request) =>
            Guard(async () =>
            {
                await _assistant.EndSessionAsync(User, request.SessionId);
                return Json(new { ok = true });
            });

        /// <summary>Turns the assistant's refusals into a JSON message the panel can show.</summary>
        private async Task<IActionResult> Guard(Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (AssistantService.AssistantException ex)
            {
                return StatusCode(ex.StatusCode, new { error = ex.Message });
            }
        }
    }
}
