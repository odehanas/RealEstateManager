using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Photos on a request: a tenant shows the problem instead of describing it. The number
    /// and the size are limited, because they are kept in the database with the request,
    /// and only real pictures are accepted - the file's first bytes are checked, not just
    /// its name, since the name and the type come from whoever uploads it.
    /// </summary>
    public class ServiceRequestPhotoService
    {
        public const int MaxPerRequest = 4;
        public const int MaxFileBytes = 5 * 1024 * 1024;

        private readonly ApplicationDbContext _db;

        public ServiceRequestPhotoService(ApplicationDbContext db)
        {
            _db = db;
        }

        public sealed record Item(int Id, string FileName, long SizeBytes, DateTime UploadedAt, string? UploadedByName, bool UploadedByTenant);

        /// <summary>The photos of a request, without their contents.</summary>
        public async Task<List<Item>> ListAsync(int requestId) =>
            (await _db.ServiceRequestPhotos.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantServiceRequestId == requestId)
                .OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.FileName, x.SizeBytes, x.UploadedAt, x.UploadedByName, x.UploadedByTenant })
                .ToListAsync())
            .Select(x => new Item(x.Id, x.FileName, x.SizeBytes, x.UploadedAt, x.UploadedByName, x.UploadedByTenant))
            .ToList();

        /// <summary>One photo with its contents, only if it is on that request.</summary>
        public Task<ServiceRequestPhoto?> GetAsync(int requestId, int photoId) =>
            _db.ServiceRequestPhotos.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == photoId && x.TenantServiceRequestId == requestId);

        /// <summary>
        /// Adds the photos chosen to a request, as many as its limit still allows. Returns how
        /// many were added and, for the ones left out, why - a photo that is refused never
        /// stops the others, or the request.
        /// </summary>
        public async Task<(int Added, List<string> Problems)> AddAsync(
            int requestId, IEnumerable<IFormFile>? files, string? byName, bool byTenant)
        {
            var problems = new List<string>();
            var chosen = (files ?? Enumerable.Empty<IFormFile>()).Where(x => x != null && x.Length > 0).ToList();
            if (chosen.Count == 0)
                return (0, problems);

            var request = await _db.TenantServiceRequests.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Id == requestId)
                .Select(x => new { x.Id, x.CompanyId, x.Status })
                .FirstOrDefaultAsync();
            if (request == null)
                return (0, new List<string> { "The request was not found." });
            if (request.Status is ServiceRequestStatuses.Closed or ServiceRequestStatuses.Cancelled or ServiceRequestStatuses.Rejected)
                return (0, new List<string> { "The request is closed, so photos can no longer be added." });

            var room = MaxPerRequest - await _db.ServiceRequestPhotos.IgnoreQueryFilters().CountAsync(x => x.TenantServiceRequestId == requestId);
            var added = 0;

            foreach (var file in chosen)
            {
                var name = Path.GetFileName(file.FileName ?? "photo");
                if (name.Length > 200) name = name[^200..];

                if (room <= 0)
                {
                    problems.Add($"A request takes up to {MaxPerRequest} photos; the others were left out.");
                    break;
                }
                if (file.Length > MaxFileBytes)
                {
                    problems.Add($"{name} is larger than {MaxFileBytes / (1024 * 1024)} MB and was left out.");
                    continue;
                }

                using var memory = new MemoryStream();
                await file.CopyToAsync(memory);
                var bytes = memory.ToArray();
                var type = PictureType(bytes);
                if (type == null)
                {
                    problems.Add($"{name} is not a photo (JPEG, PNG or WebP) and was left out.");
                    continue;
                }

                _db.ServiceRequestPhotos.Add(new ServiceRequestPhoto
                {
                    CompanyId = request.CompanyId,
                    TenantServiceRequestId = request.Id,
                    FileName = name,
                    ContentType = type,
                    SizeBytes = bytes.LongLength,
                    Content = bytes,
                    UploadedByName = byName,
                    UploadedByTenant = byTenant
                });
                room--;
                added++;
            }

            if (added > 0)
                await _db.SaveChangesAsync();
            return (added, problems);
        }

        /// <summary>The picture type the bytes really are, or null when they are not a picture we take.</summary>
        private static string? PictureType(byte[] b)
        {
            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
                return "image/jpeg";
            if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)
                return "image/png";
            if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
                return "image/webp";
            return null;
        }
    }
}
