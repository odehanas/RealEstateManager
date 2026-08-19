using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    public class FeedbackNotificationOptions
    {
        public bool Enabled { get; set; }
        public string? WebhookUrl { get; set; }
        public string? SharedSecret { get; set; }
        public string ApplicationName { get; set; } = "RealEstateManager";
    }

    public class FeedbackNotificationService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly FeedbackNotificationOptions _options;
        private readonly ILogger<FeedbackNotificationService> _logger;

        public FeedbackNotificationService(
            IHttpClientFactory httpClientFactory,
            IOptions<FeedbackNotificationOptions> options,
            ILogger<FeedbackNotificationService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _logger = logger;
        }

        public async Task NotifyAsync(UserFeedback feedback)
        {
            if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.WebhookUrl))
                return;

            try
            {
                var client = _httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Post, _options.WebhookUrl)
                {
                    Content = JsonContent.Create(new
                    {
                        application = _options.ApplicationName,
                        eventType = "feedback.submitted",
                        sharedSecret = _options.SharedSecret,
                        feedback = new
                        {
                            feedback.Id,
                            feedback.CompanyId,
                            feedback.CompanyName,
                            feedback.AppUserId,
                            feedback.Username,
                            feedback.DisplayName,
                            feedback.Category,
                            feedback.Subject,
                            feedback.Message,
                            feedback.Status,
                            feedback.SubmittedAt
                        }
                    })
                };

                using var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Feedback notification webhook failed for feedback item {FeedbackId}.", feedback.Id);
            }
        }
    }
}
