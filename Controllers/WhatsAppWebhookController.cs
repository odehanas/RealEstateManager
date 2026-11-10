using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Services.WhatsApp;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The address Meta calls. Nobody signs in here: the proof of who is calling is the
    /// signature on each delivery, checked against the secret of the company whose address
    /// was called, before a single byte of the delivery is read.
    ///
    /// The answers matter to Meta: it repeats a delivery it did not get a 200 for. So a
    /// message we have safely saved gets a 200 even if it could not become a request, and
    /// only a failure to save it at all (a 500) asks for it to be sent again.
    /// </summary>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [Route("webhooks/whatsapp/{key}")]
    public class WhatsAppWebhookController : Controller
    {
        // Messages are text and media ids, never the media itself.
        private const int MaxBodyBytes = 2 * 1024 * 1024;

        private readonly WhatsAppConnectionService _connections;
        private readonly WhatsAppIntakeService _intake;
        private readonly ILogger<WhatsAppWebhookController> _logger;

        public WhatsAppWebhookController(
            WhatsAppConnectionService connections,
            WhatsAppIntakeService intake,
            ILogger<WhatsAppWebhookController> logger)
        {
            _connections = connections;
            _intake = intake;
            _logger = logger;
        }

        /// <summary>Meta checking the address when it is registered: it must echo the challenge back.</summary>
        [HttpGet]
        public async Task<IActionResult> Verify(
            string key,
            [FromQuery(Name = "hub.mode")] string? mode,
            [FromQuery(Name = "hub.verify_token")] string? verifyToken,
            [FromQuery(Name = "hub.challenge")] string? challenge)
        {
            var connection = await _connections.FindEnabledAsync(key);
            if (connection == null)
                return NotFound();

            if (mode != "subscribe" || !_connections.VerifyToken(connection, verifyToken))
                return StatusCode(StatusCodes.Status403Forbidden);

            return Content(challenge ?? string.Empty, "text/plain");
        }

        [HttpPost]
        [RequestSizeLimit(MaxBodyBytes)]
        public async Task<IActionResult> Receive(string key)
        {
            var connection = await _connections.FindEnabledAsync(key);
            if (connection == null)
                return NotFound();

            using var buffer = new MemoryStream();
            await Request.Body.CopyToAsync(buffer, HttpContext.RequestAborted);
            var body = buffer.ToArray();

            if (!_connections.VerifySignature(connection, body, Request.Headers["X-Hub-Signature-256"].ToString()))
            {
                _logger.LogWarning("WhatsApp delivery with a bad signature refused for company {CompanyId}.", connection.CompanyId);
                return Unauthorized();
            }

            List<WhatsAppDeliveredMessage> messages;
            try
            {
                messages = WhatsAppWebhookParser.Parse(body);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Signed WhatsApp delivery for company {CompanyId} was not JSON.", connection.CompanyId);
                return BadRequest();
            }

            await _intake.ReceiveAsync(connection, messages);
            return Ok();
        }
    }
}
