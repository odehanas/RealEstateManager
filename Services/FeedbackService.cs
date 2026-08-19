using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    public class FeedbackService
    {
        private static readonly string[] OpenStatuses = ["New", "Reviewed", "Planned"];

        private readonly ApplicationDbContext _db;
        private readonly FeedbackNotificationService _feedbackNotificationService;

        public FeedbackService(
            ApplicationDbContext db,
            FeedbackNotificationService feedbackNotificationService)
        {
            _db = db;
            _feedbackNotificationService = feedbackNotificationService;
        }

        public async Task SubmitAsync(ClaimsPrincipal user, FeedbackFormViewModel model)
        {
            var companyId = user.GetCurrentCompanyId()
                ?? throw new InvalidOperationException("Select a company before submitting feedback.");
            var userId = user.GetAppUserId()
                ?? throw new InvalidOperationException("Current user is not recognized.");

            var feedback = new UserFeedback
            {
                CompanyId = companyId,
                AppUserId = userId,
                Username = user.FindFirst(AppClaimTypes.Username)?.Value ?? user.Identity?.Name ?? "Unknown",
                DisplayName = user.FindFirst(AppClaimTypes.DisplayName)?.Value ?? "Unknown",
                CompanyName = user.FindFirst(AppClaimTypes.CurrentCompanyName)?.Value ?? "Unknown",
                Category = model.Category.Trim(),
                Subject = model.Subject.Trim(),
                Message = model.Message.Trim(),
                Status = "New",
                SubmittedAt = DateTime.UtcNow
            };

            _db.UserFeedback.Add(feedback);
            await _db.SaveChangesAsync();
            await _feedbackNotificationService.NotifyAsync(feedback);
        }

        public async Task<FeedbackPageViewModel> BuildUserPageAsync(ClaimsPrincipal user)
        {
            var companyId = user.GetCurrentCompanyId()
                ?? throw new InvalidOperationException("Select a company before viewing feedback.");

            var items = await _db.UserFeedback
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.SubmittedAt)
                .Take(10)
                .Select(x => new FeedbackListItemViewModel
                {
                    Id = x.Id,
                    Username = x.Username,
                    DisplayName = x.DisplayName,
                    CompanyName = x.CompanyName,
                    Category = x.Category,
                    Subject = x.Subject,
                    Message = x.Message,
                    Status = x.Status,
                    AdminNotes = x.AdminNotes,
                    SubmittedAt = x.SubmittedAt,
                    ReviewedAt = x.ReviewedAt,
                    ReviewedByName = x.ReviewedByAppUser != null ? x.ReviewedByAppUser.DisplayName : null
                })
                .ToListAsync();

            return new FeedbackPageViewModel
            {
                Form = new FeedbackFormViewModel(),
                RecentItems = items
            };
        }

        public async Task<FeedbackAdminListViewModel> GetAdminListAsync(FeedbackAdminFilterViewModel filter)
        {
            filter ??= new FeedbackAdminFilterViewModel();

            var query = BuildAdminQuery(filter);

            var items = await query
                .OrderByDescending(x => x.SubmittedAt)
                .Take(500)
                .Select(x => new FeedbackListItemViewModel
                {
                    Id = x.Id,
                    Username = x.Username,
                    DisplayName = x.DisplayName,
                    CompanyName = x.CompanyName,
                    Category = x.Category,
                    Subject = x.Subject,
                    Message = x.Message,
                    Status = x.Status,
                    AdminNotes = x.AdminNotes,
                    SubmittedAt = x.SubmittedAt,
                    ReviewedAt = x.ReviewedAt,
                    ReviewedByName = x.ReviewedByAppUser != null ? x.ReviewedByAppUser.DisplayName : null
                })
                .ToListAsync();

            var statsSource = await _db.UserFeedback
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(x => x.Status)
                .ToListAsync();

            var statuses = await _db.UserFeedback
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(x => x.Status)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var categories = await _db.UserFeedback
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(x => x.Category)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            return new FeedbackAdminListViewModel
            {
                Filter = filter,
                AvailableStatuses = statuses,
                AvailableCategories = categories,
                TotalItems = statsSource.Count,
                NewItems = statsSource.Count(x => x == "New"),
                OpenItems = statsSource.Count(x => OpenStatuses.Contains(x)),
                PlannedItems = statsSource.Count(x => x == "Planned"),
                ClosedItems = statsSource.Count(x => x == "Closed"),
                Items = items
            };
        }

        public async Task<byte[]> ExportCsvAsync(FeedbackAdminFilterViewModel filter)
        {
            var rows = await BuildAdminQuery(filter ?? new FeedbackAdminFilterViewModel())
                .OrderByDescending(x => x.SubmittedAt)
                .Select(x => new
                {
                    x.SubmittedAt,
                    x.CompanyName,
                    x.DisplayName,
                    x.Username,
                    x.Category,
                    x.Subject,
                    x.Message,
                    x.Status,
                    x.AdminNotes,
                    x.ReviewedAt
                })
                .ToListAsync();

            var lines = new List<string>
            {
                "SubmittedAt,Company,DisplayName,Username,Category,Subject,Message,Status,AdminNotes,ReviewedAt"
            };

            lines.AddRange(rows.Select(x => string.Join(",",
                EscapeCsv(x.SubmittedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                EscapeCsv(x.CompanyName),
                EscapeCsv(x.DisplayName),
                EscapeCsv(x.Username),
                EscapeCsv(x.Category),
                EscapeCsv(x.Subject),
                EscapeCsv(x.Message),
                EscapeCsv(x.Status),
                EscapeCsv(x.AdminNotes),
                EscapeCsv(x.ReviewedAt?.ToString("yyyy-MM-dd HH:mm:ss")))));

            return System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, lines));
        }

        public async Task<FeedbackReviewViewModel> GetReviewAsync(int id)
        {
            var feedback = await _db.UserFeedback
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new FeedbackReviewViewModel
                {
                    Id = x.Id,
                    Username = x.Username,
                    DisplayName = x.DisplayName,
                    CompanyName = x.CompanyName,
                    Category = x.Category,
                    Subject = x.Subject,
                    Message = x.Message,
                    SubmittedAt = x.SubmittedAt,
                    Status = x.Status,
                    AdminNotes = x.AdminNotes,
                    ReviewedAt = x.ReviewedAt,
                    ReviewedByName = x.ReviewedByAppUser != null ? x.ReviewedByAppUser.DisplayName : null
                })
                .FirstOrDefaultAsync();

            return feedback ?? throw new InvalidOperationException("Feedback item was not found.");
        }

        public async Task ReviewAsync(int id, int reviewerUserId, FeedbackReviewViewModel model)
        {
            var feedback = await _db.UserFeedback
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (feedback == null)
                throw new InvalidOperationException("Feedback item was not found.");

            feedback.Status = model.Status.Trim();
            feedback.AdminNotes = string.IsNullOrWhiteSpace(model.AdminNotes) ? null : model.AdminNotes.Trim();
            feedback.ReviewedAt = DateTime.UtcNow;
            feedback.ReviewedByAppUserId = reviewerUserId;

            await _db.SaveChangesAsync();
        }

        private IQueryable<UserFeedback> BuildAdminQuery(FeedbackAdminFilterViewModel filter)
        {
            var query = _db.UserFeedback
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                query = query.Where(x => x.Status == filter.Status);
            }

            if (!string.IsNullOrWhiteSpace(filter.Category))
            {
                query = query.Where(x => x.Category == filter.Category);
            }

            if (!string.IsNullOrWhiteSpace(filter.Username))
            {
                var username = filter.Username.Trim();
                query = query.Where(x => x.Username.Contains(username));
            }

            if (filter.OnlyOpenItems)
            {
                query = query.Where(x => OpenStatuses.Contains(x.Status));
            }

            return query;
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}
