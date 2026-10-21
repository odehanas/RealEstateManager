using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Photos of a building and the units marked on them, for the photo mode of the
    /// building view. A unit is marked once per photo by clicking the corners of its windows
    /// or balcony; the corners are kept as fractions of the photo's width and height.
    ///
    /// Towers repeat floor after floor, so marking every unit by hand is not needed: mark two
    /// floors one above the other, and <see cref="RepeatFloorsAsync"/> carries the same
    /// outlines up the building by the distance between those two floors.
    /// </summary>
    public class BuildingPhotoService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentCompanyAccessor _currentCompany;

        public BuildingPhotoService(ApplicationDbContext db, ICurrentCompanyAccessor currentCompany)
        {
            _db = db;
            _currentCompany = currentCompany;
        }

        private int CompanyId => _currentCompany.CompanyId
            ?? throw new InvalidOperationException("No company is selected for the current user.");

        /// <summary>A property's photos with their outlines - never the images themselves.</summary>
        public async Task<List<PropertyPhoto>> GetPhotosAsync(int propertyId)
        {
            return await _db.PropertyPhotos.AsNoTracking()
                .Where(x => x.PropertyId == propertyId)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
                .Select(x => new PropertyPhoto
                {
                    Id = x.Id,
                    PropertyId = x.PropertyId,
                    Title = x.Title,
                    SortOrder = x.SortOrder,
                    FileName = x.FileName,
                    SizeBytes = x.SizeBytes,
                    UploadedAt = x.UploadedAt,
                    UploadedByName = x.UploadedByName,
                    Areas = x.Areas.ToList()
                })
                .ToListAsync();
        }

        public async Task<PropertyPhoto?> GetPhotoInfoAsync(int photoId)
        {
            return await _db.PropertyPhotos.AsNoTracking()
                .Include(x => x.Property)
                .Where(x => x.Id == photoId)
                .Select(x => new PropertyPhoto
                {
                    Id = x.Id,
                    PropertyId = x.PropertyId,
                    Property = x.Property,
                    Title = x.Title,
                    FileName = x.FileName,
                    Areas = x.Areas.ToList()
                })
                .FirstOrDefaultAsync();
        }

        public async Task<(byte[] Content, string ContentType)?> GetImageAsync(int photoId)
        {
            var image = await _db.PropertyPhotos.AsNoTracking()
                .Where(x => x.Id == photoId)
                .Select(x => new { x.Content, x.ContentType })
                .FirstOrDefaultAsync();
            return image == null ? null : (image.Content, image.ContentType);
        }

        public async Task<PropertyPhoto> UploadAsync(int propertyId, IFormFile? file, string? title, string? userName)
        {
            if (!await _db.Properties.AnyAsync(x => x.Id == propertyId))
                throw new InvalidOperationException("The property was not found.");

            var extension = Path.GetExtension(file?.FileName ?? string.Empty).ToLowerInvariant();
            if (file == null || file.Length == 0)
                throw new InvalidOperationException("Choose the photo to upload.");
            if (file.Length > PropertyPhoto.MaxFileBytes)
                throw new InvalidOperationException(
                    $"The photo is {file.Length / 1024 / 1024.0:N1} MB. The limit is {PropertyPhoto.MaxFileBytes / 1024 / 1024} MB - save it at a smaller size.");
            if (!PropertyPhoto.AllowedExtensions.Contains(extension))
                throw new InvalidOperationException("Only JPG and PNG photos can be used.");

            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            var bytes = stream.ToArray();

            // The extension is only a name; check the file really is a JPEG or PNG image.
            var isPng = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
            var isJpeg = bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            if (!isPng && !isJpeg)
                throw new InvalidOperationException("That file is not a JPG or PNG photo.");

            var order = await _db.PropertyPhotos.Where(x => x.PropertyId == propertyId).Select(x => (int?)x.SortOrder).MaxAsync() ?? 0;
            var photo = new PropertyPhoto
            {
                CompanyId = CompanyId,
                PropertyId = propertyId,
                Title = string.IsNullOrWhiteSpace(title) ? "Front" : title.Trim(),
                SortOrder = order + 1,
                FileName = Path.GetFileName(file.FileName),
                ContentType = isPng ? "image/png" : "image/jpeg",
                SizeBytes = bytes.Length,
                Content = bytes,
                UploadedAt = DateTime.UtcNow,
                UploadedByName = userName
            };
            _db.PropertyPhotos.Add(photo);
            await _db.SaveChangesAsync();
            return photo;
        }

        public async Task<int> DeletePhotoAsync(int photoId)
        {
            var photo = await _db.PropertyPhotos.FirstOrDefaultAsync(x => x.Id == photoId)
                        ?? throw new InvalidOperationException("The photo was not found.");
            var propertyId = photo.PropertyId;
            _db.PropertyPhotos.Remove(photo);
            await _db.SaveChangesAsync();
            return propertyId;
        }

        /// <summary>Saves a unit's outline on a photo, replacing any earlier one.</summary>
        public async Task SaveAreaAsync(int photoId, int unitId, double[][] points)
        {
            var photo = await _db.PropertyPhotos.AsNoTracking().Select(x => new { x.Id, x.PropertyId }).FirstOrDefaultAsync(x => x.Id == photoId)
                        ?? throw new InvalidOperationException("The photo was not found.");
            if (!await _db.Units.AnyAsync(x => x.Id == unitId && x.PropertyId == photo.PropertyId))
                throw new InvalidOperationException("That unit is not in this building.");

            var clean = Validate(points);
            var area = await _db.UnitPhotoAreas.FirstOrDefaultAsync(x => x.PropertyPhotoId == photoId && x.UnitId == unitId);
            if (area == null)
            {
                area = new UnitPhotoArea { PropertyPhotoId = photoId, UnitId = unitId };
                _db.UnitPhotoAreas.Add(area);
            }
            area.PointsJson = JsonSerializer.Serialize(clean);
            area.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        public async Task DeleteAreaAsync(int photoId, int unitId)
        {
            var area = await _db.UnitPhotoAreas.FirstOrDefaultAsync(x => x.PropertyPhotoId == photoId && x.UnitId == unitId);
            if (area == null)
                return;
            _db.UnitPhotoAreas.Remove(area);
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Carries the outlines of a marked floor up (or down) the building. The step between
        /// floors is measured from two floors already marked one above the other: the average
        /// move of the same units (matched left to right) from <paramref name="lowerFloor"/> to
        /// <paramref name="upperFloor"/>. Each following floor gets the upper floor's outlines
        /// moved by that step once more, matched to its units in the same order. Units that
        /// already have an outline keep it unless <paramref name="overwrite"/> is set.
        /// Returns how many outlines were added.
        /// </summary>
        public async Task<int> RepeatFloorsAsync(int photoId, int lowerFloor, int upperFloor, int lastFloor, bool overwrite)
        {
            var photo = await _db.PropertyPhotos.AsNoTracking().Select(x => new { x.Id, x.PropertyId }).FirstOrDefaultAsync(x => x.Id == photoId)
                        ?? throw new InvalidOperationException("The photo was not found.");
            if (lowerFloor == upperFloor)
                throw new InvalidOperationException("Choose two different floors that are already marked.");

            var units = await _db.Units.AsNoTracking().Where(x => x.PropertyId == photo.PropertyId && x.IsActive).ToListAsync();
            var areas = await _db.UnitPhotoAreas.Where(x => x.PropertyPhotoId == photoId).ToListAsync();
            var byUnit = areas.ToDictionary(x => x.UnitId);

            List<Unit> FloorUnits(int floor) => units
                .Where(u => BuildingViewService.ResolveFloor(u) == floor)
                .OrderBy(u => u.FloorPosition ?? int.MaxValue).ThenBy(u => u.UnitNumber)
                .ToList();

            // Only the units marked on this photo count: a photo shows one side of the building.
            var lower = FloorUnits(lowerFloor).Where(u => byUnit.ContainsKey(u.Id)).ToList();
            var upper = FloorUnits(upperFloor).Where(u => byUnit.ContainsKey(u.Id)).ToList();
            var pairs = Math.Min(lower.Count, upper.Count);
            if (pairs == 0)
                throw new InvalidOperationException($"Mark the units of floors {lowerFloor} and {upperFloor} on this photo first - the step between floors is measured from them.");

            double dx = 0, dy = 0;
            for (var i = 0; i < pairs; i++)
            {
                var a = Centre(Read(byUnit[lower[i].Id].PointsJson));
                var b = Centre(Read(byUnit[upper[i].Id].PointsJson));
                dx += b.X - a.X;
                dy += b.Y - a.Y;
            }
            // The average move of one floor: the two marked floors may be several floors apart.
            var gap = Math.Abs(upperFloor - lowerFloor);
            dx /= pairs * gap;
            dy /= pairs * gap;

            var direction = Math.Sign(upperFloor - lowerFloor);
            var template = upper.Select(u => Read(byUnit[u.Id].PointsJson)).ToList();
            var added = 0;

            for (var floor = upperFloor + direction; direction > 0 ? floor <= lastFloor : floor >= lastFloor; floor += direction)
            {
                var steps = Math.Abs(floor - upperFloor);
                var targets = FloorUnits(floor);
                for (var i = 0; i < Math.Min(targets.Count, template.Count); i++)
                {
                    var moved = template[i].Select(p => new[] { p[0] + dx * steps, p[1] + dy * steps }).ToArray();
                    if (moved.Any(p => p[0] < 0 || p[0] > 1 || p[1] < 0 || p[1] > 1))
                        continue;   // it would fall off the photo

                    if (byUnit.TryGetValue(targets[i].Id, out var existing))
                    {
                        if (!overwrite) continue;
                        existing.PointsJson = JsonSerializer.Serialize(Round(moved));
                        existing.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        var area = new UnitPhotoArea { PropertyPhotoId = photoId, UnitId = targets[i].Id, PointsJson = JsonSerializer.Serialize(Round(moved)), UpdatedAt = DateTime.UtcNow };
                        _db.UnitPhotoAreas.Add(area);
                        byUnit[targets[i].Id] = area;
                    }
                    added++;
                }
            }

            await _db.SaveChangesAsync();
            return added;
        }

        // ---- helpers ------------------------------------------------------------------

        private static double[][] Validate(double[][]? points)
        {
            if (points == null || points.Length < 3)
                throw new InvalidOperationException("Click at least three corners to outline the unit.");
            if (points.Length > 60)
                throw new InvalidOperationException("An outline can have at most 60 corners.");
            if (points.Any(p => p == null || p.Length != 2 || p.Any(v => double.IsNaN(v) || v < 0 || v > 1)))
                throw new InvalidOperationException("The outline is outside the photo.");
            return Round(points);
        }

        private static double[][] Round(double[][] points) =>
            points.Select(p => new[] { Math.Round(p[0], 5), Math.Round(p[1], 5) }).ToArray();

        public static double[][] Read(string json)
        {
            try { return JsonSerializer.Deserialize<double[][]>(json) ?? Array.Empty<double[]>(); }
            catch (JsonException) { return Array.Empty<double[]>(); }
        }

        private static (double X, double Y) Centre(double[][] points) =>
            points.Length == 0 ? (0, 0) : (points.Average(p => p[0]), points.Average(p => p[1]));
    }
}
