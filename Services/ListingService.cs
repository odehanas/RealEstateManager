using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The vacancy website. Two sides share it:
    ///
    /// the office, signed in, chooses which units to advertise, gives them photos and a
    /// description, and answers the enquiries;
    ///
    /// the public, not signed in, sees one company's page at /vacancies/{slug}. A visitor has
    /// no company of their own, so the first thing every public request does is
    /// <see cref="OpenPublicSiteAsync"/>, which puts the request inside the company the
    /// address belongs to. From then on every query is filtered to that company exactly as
    /// for a signed-in user, and nothing of another company can be reached.
    ///
    /// A unit is public only while all of this holds: the page is enabled, the office ticked
    /// the unit, it is active and vacant with no running contract, and its property is active.
    /// </summary>
    public class ListingService
    {
        private static readonly Regex SlugPattern = new("^[a-z0-9](?:[a-z0-9-]{1,58})[a-z0-9]$", RegexOptions.Compiled);

        private readonly ApplicationDbContext _db;
        private readonly ICurrentCompanyAccessor _currentCompany;
        private readonly CompanyContextOverride _companyOverride;

        public ListingService(ApplicationDbContext db, ICurrentCompanyAccessor currentCompany, CompanyContextOverride companyOverride)
        {
            _db = db;
            _currentCompany = currentCompany;
            _companyOverride = companyOverride;
        }

        private int CompanyId => _currentCompany.CompanyId
            ?? throw new InvalidOperationException("No company is selected for the current user.");

        // ---- The company's page -----------------------------------------------------

        /// <summary>The company's page, made (switched off) the first time the screen is opened.</summary>
        public async Task<ListingSite> GetOrCreateSiteAsync()
        {
            var companyId = CompanyId;
            var site = await _db.ListingSites.FirstOrDefaultAsync(x => x.CompanyId == companyId);
            if (site != null)
                return site;

            var company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId);
            site = new ListingSite
            {
                CompanyId = companyId,
                Slug = await FreeSlugAsync(Slugify(company.Name, companyId), companyId),
                IsEnabled = false,
                ContactPhone = company.Phone,
                ContactEmail = company.Email,
                UpdatedAt = DateTime.UtcNow
            };
            _db.ListingSites.Add(site);
            await _db.SaveChangesAsync();
            return site;
        }

        public sealed class SiteInput
        {
            public string? Slug { get; set; }
            public bool IsEnabled { get; set; }
            public string? DisplayName { get; set; }
            public string? DisplayNameAr { get; set; }
            public string? Intro { get; set; }
            public string? IntroAr { get; set; }
            public string? ContactPhone { get; set; }
            public string? WhatsAppNumber { get; set; }
            public string? ContactEmail { get; set; }
            // Not defaulted to true: an unticked box is simply absent from the form.
            public bool ShowRent { get; set; }
        }

        public async Task SaveSiteAsync(SiteInput input)
        {
            var site = await GetOrCreateSiteAsync();

            var slug = (input.Slug ?? string.Empty).Trim().ToLowerInvariant();
            if (!SlugPattern.IsMatch(slug))
                throw new InvalidOperationException("The address may hold only small English letters, digits and hyphens, 3 to 60 characters, starting and ending with a letter or digit.");
            if (await _db.ListingSites.IgnoreQueryFilters().AnyAsync(x => x.Slug == slug && x.CompanyId != site.CompanyId))
                throw new InvalidOperationException("That address is already used by another company. Choose a different one.");

            var whatsApp = DigitsOnly(input.WhatsAppNumber);
            if (whatsApp != null && (whatsApp.Length < 8 || whatsApp.Length > 15))
                throw new InvalidOperationException("Enter the WhatsApp number in international format, for example 971501234567.");

            var email = Clean(input.ContactEmail, 200);
            if (email != null && !LooksLikeEmail(email))
                throw new InvalidOperationException("The contact email does not look right.");

            site.Slug = slug;
            site.IsEnabled = input.IsEnabled;
            site.DisplayName = Clean(input.DisplayName, 200);
            site.DisplayNameAr = Clean(input.DisplayNameAr, 200);
            site.Intro = Clean(input.Intro, 600);
            site.IntroAr = Clean(input.IntroAr, 600);
            site.ContactPhone = Clean(input.ContactPhone, 30);
            site.WhatsAppNumber = whatsApp;
            site.ContactEmail = email;
            site.ShowRent = input.ShowRent;
            site.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        // ---- The office's board -----------------------------------------------------

        public sealed record BoardUnit(
            int Id, string UnitNumber, int PropertyId, string PropertyName, string? UsageType, string Status,
            decimal Rent, int? Bedrooms, bool IsListed, int PhotoCount, int? CoverPhotoId, bool HasDescription,
            bool IsPublic, int OpenEnquiries);

        /// <summary>
        /// Every active unit that is vacant or ticked for the website, with what its
        /// advertisement still lacks. Occupied units that are not ticked are left out: there
        /// is nothing to do with them here.
        /// </summary>
        public async Task<List<BoardUnit>> GetBoardAsync()
        {
            var today = DateTime.Today;
            var rows = await _db.Units.AsNoTracking()
                .Where(u => u.IsActive && u.Property != null && u.Property.IsActive && (u.Status == "Vacant" || u.IsListed))
                .Select(u => new
                {
                    u.Id,
                    u.UnitNumber,
                    u.PropertyId,
                    PropertyName = u.Property!.Name,
                    u.UsageType,
                    u.Status,
                    u.StandardAnnualRent,
                    u.AskingRent,
                    u.Bedrooms,
                    u.IsListed,
                    PhotoCount = u.Photos.Count,
                    CoverPhotoId = u.Photos.OrderBy(p => p.SortOrder).ThenBy(p => p.Id).Select(p => (int?)p.Id).FirstOrDefault(),
                    HasDescription = u.ListingDescription != null || u.ListingDescriptionAr != null,
                    HasRunningLease = u.Leases.Any(l => l.Status == "Active" && l.StartDate <= today && l.EndDate >= today)
                })
                .ToListAsync();

            var open = await _db.UnitEnquiries.AsNoTracking()
                .Where(x => x.UnitId != null && x.Status == EnquiryStatuses.New)
                .GroupBy(x => x.UnitId!.Value)
                .Select(g => new { UnitId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UnitId, x => x.Count);

            return rows
                .OrderBy(x => x.PropertyName).ThenBy(x => x.UnitNumber)
                .Select(x => new BoardUnit(
                    x.Id, x.UnitNumber, x.PropertyId, x.PropertyName, x.UsageType, x.Status,
                    x.AskingRent is > 0 ? x.AskingRent.Value : x.StandardAnnualRent,
                    x.Bedrooms, x.IsListed, x.PhotoCount, x.CoverPhotoId, x.HasDescription,
                    x.IsListed && x.Status == "Vacant" && !x.HasRunningLease,
                    open.GetValueOrDefault(x.Id)))
                .ToList();
        }

        public Task<Unit?> GetUnitAsync(int unitId) =>
            _db.Units.Include(x => x.Property).FirstOrDefaultAsync(x => x.Id == unitId);

        public sealed class UnitInput
        {
            public int? Bedrooms { get; set; }
            public int? Bathrooms { get; set; }
            public bool IsFurnished { get; set; }
            public decimal? AskingRent { get; set; }
            public string? Amenities { get; set; }
            public string? ListingDescription { get; set; }
            public string? ListingDescriptionAr { get; set; }
            public bool IsListed { get; set; }
        }

        public async Task SaveUnitAsync(int unitId, UnitInput input)
        {
            var unit = await _db.Units.FirstOrDefaultAsync(x => x.Id == unitId)
                       ?? throw new InvalidOperationException("The unit was not found.");

            if (input.Bedrooms is < 0 or > 50 || input.Bathrooms is < 0 or > 50)
                throw new InvalidOperationException("Bedrooms and bathrooms must be between 0 and 50.");
            if (input.AskingRent is < 0)
                throw new InvalidOperationException("The asking rent cannot be negative.");

            unit.Bedrooms = input.Bedrooms;
            unit.Bathrooms = input.Bathrooms;
            unit.IsFurnished = input.IsFurnished;
            unit.AskingRent = input.AskingRent is > 0 ? input.AskingRent : null;
            unit.Amenities = CleanLines(input.Amenities, 600);
            unit.ListingDescription = Clean(input.ListingDescription, 2000);
            unit.ListingDescriptionAr = Clean(input.ListingDescriptionAr, 2000);
            SetListed(unit, input.IsListed);
            await _db.SaveChangesAsync();
        }

        public async Task<Unit> SetListedAsync(int unitId, bool listed)
        {
            var unit = await _db.Units.FirstOrDefaultAsync(x => x.Id == unitId)
                       ?? throw new InvalidOperationException("The unit was not found.");
            SetListed(unit, listed);
            await _db.SaveChangesAsync();
            return unit;
        }

        private static void SetListed(Unit unit, bool listed)
        {
            if (listed && !unit.IsListed)
                unit.ListedAt = DateTime.UtcNow;
            unit.IsListed = listed;
        }

        // ---- Photos -----------------------------------------------------------------

        public sealed record PhotoItem(int Id, string FileName, long SizeBytes);

        public async Task<List<PhotoItem>> GetPhotosAsync(int unitId) =>
            (await _db.UnitPhotos.AsNoTracking()
                .Where(x => x.UnitId == unitId)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.FileName, x.SizeBytes })
                .ToListAsync())
            .Select(x => new PhotoItem(x.Id, x.FileName, x.SizeBytes))
            .ToList();

        /// <summary>
        /// Adds the photos chosen, as many as the unit's limit still allows. A photo that is
        /// refused never stops the others; the reasons come back to be shown.
        /// </summary>
        public async Task<(int Added, List<string> Problems)> AddPhotosAsync(int unitId, IEnumerable<IFormFile>? files, string? byName)
        {
            var problems = new List<string>();
            var chosen = (files ?? Enumerable.Empty<IFormFile>()).Where(x => x != null && x.Length > 0).ToList();
            if (chosen.Count == 0)
                return (0, new List<string> { "Choose the photos to upload." });

            if (!await _db.Units.AnyAsync(x => x.Id == unitId))
                return (0, new List<string> { "The unit was not found." });

            var existing = await _db.UnitPhotos.Where(x => x.UnitId == unitId).Select(x => x.SortOrder).ToListAsync();
            var room = UnitPhoto.MaxPerUnit - existing.Count;
            var order = existing.Count == 0 ? 0 : existing.Max();
            var added = 0;

            foreach (var file in chosen)
            {
                var name = Path.GetFileName(file.FileName ?? "photo");
                if (name.Length > 200) name = name[^200..];

                if (room <= 0)
                {
                    problems.Add($"A unit takes up to {UnitPhoto.MaxPerUnit} photos; the others were left out.");
                    break;
                }
                if (file.Length > UnitPhoto.MaxFileBytes)
                {
                    problems.Add($"{name} is larger than {UnitPhoto.MaxFileBytes / (1024 * 1024)} MB and was left out.");
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

                _db.UnitPhotos.Add(new UnitPhoto
                {
                    CompanyId = CompanyId,
                    UnitId = unitId,
                    SortOrder = ++order,
                    FileName = name,
                    ContentType = type,
                    SizeBytes = bytes.LongLength,
                    Content = bytes,
                    UploadedAt = DateTime.UtcNow,
                    UploadedByName = byName
                });
                room--;
                added++;
            }

            if (added > 0)
                await _db.SaveChangesAsync();
            return (added, problems);
        }

        public async Task<int> DeletePhotoAsync(int photoId)
        {
            var photo = await _db.UnitPhotos.FirstOrDefaultAsync(x => x.Id == photoId)
                        ?? throw new InvalidOperationException("The photo was not found.");
            var unitId = photo.UnitId;
            _db.UnitPhotos.Remove(photo);
            await _db.SaveChangesAsync();
            return unitId;
        }

        /// <summary>Puts a photo first: it becomes the picture on the unit's card.</summary>
        public async Task<int> MakeCoverAsync(int photoId)
        {
            var photo = await _db.UnitPhotos.FirstOrDefaultAsync(x => x.Id == photoId)
                        ?? throw new InvalidOperationException("The photo was not found.");
            var lowest = await _db.UnitPhotos.Where(x => x.UnitId == photo.UnitId).MinAsync(x => x.SortOrder);
            photo.SortOrder = lowest - 1;
            await _db.SaveChangesAsync();
            return photo.UnitId;
        }

        /// <summary>A photo's picture. Inside a public request it is only ever one of the site's own company.</summary>
        public async Task<(byte[] Content, string ContentType)?> GetPhotoImageAsync(int photoId, bool publicOnly)
        {
            var today = DateTime.Today;
            var query = _db.UnitPhotos.AsNoTracking().Where(x => x.Id == photoId);
            if (publicOnly)
                query = query.Where(x => x.Unit != null && x.Unit.IsListed && x.Unit.IsActive && x.Unit.Status == "Vacant"
                    && x.Unit.Property != null && x.Unit.Property.IsActive
                    && !x.Unit.Leases.Any(l => l.Status == "Active" && l.StartDate <= today && l.EndDate >= today));

            var image = await query.Select(x => new { x.Content, x.ContentType }).FirstOrDefaultAsync();
            return image == null ? null : (image.Content, image.ContentType);
        }

        // ---- Enquiries --------------------------------------------------------------

        public sealed record EnquiryRow(
            int Id, DateTime CreatedAt, string Name, string Phone, string? Email, string? Message, string Language,
            string Status, string? Notes, int? UnitId, string? UnitNumber, string? PropertyName,
            DateTime? HandledAt, string? HandledByName);

        public async Task<List<EnquiryRow>> GetEnquiriesAsync(string? status, int take = 500)
        {
            var query = _db.UnitEnquiries.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(status))
            {
                var wanted = EnquiryStatuses.Clean(status);
                query = query.Where(x => x.Status == wanted);
            }

            return (await query
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(take)
                    .Select(x => new
                    {
                        x.Id, x.CreatedAt, x.Name, x.Phone, x.Email, x.Message, x.Language, x.Status, x.Notes, x.UnitId,
                        UnitNumber = x.Unit != null ? x.Unit.UnitNumber : null,
                        PropertyName = x.Unit != null && x.Unit.Property != null ? x.Unit.Property.Name : null,
                        x.HandledAt, x.HandledByName
                    })
                    .ToListAsync())
                .Select(x => new EnquiryRow(x.Id, x.CreatedAt, x.Name, x.Phone, x.Email, x.Message, x.Language,
                    x.Status, x.Notes, x.UnitId, x.UnitNumber, x.PropertyName, x.HandledAt, x.HandledByName))
                .ToList();
        }

        public Task<int> CountNewEnquiriesAsync() =>
            _db.UnitEnquiries.CountAsync(x => x.Status == EnquiryStatuses.New);

        public async Task UpdateEnquiryAsync(int id, string? status, string? notes, string? byName)
        {
            var enquiry = await _db.UnitEnquiries.FirstOrDefaultAsync(x => x.Id == id)
                          ?? throw new InvalidOperationException("The enquiry was not found.");
            var newStatus = EnquiryStatuses.Clean(status);
            if (newStatus != enquiry.Status)
            {
                enquiry.Status = newStatus;
                enquiry.HandledAt = newStatus == EnquiryStatuses.New ? null : DateTime.UtcNow;
                enquiry.HandledByName = newStatus == EnquiryStatuses.New ? null : byName;
            }
            enquiry.Notes = Clean(notes, 1000);
            await _db.SaveChangesAsync();
        }

        // ---- The public side --------------------------------------------------------

        public sealed record PublicSite(
            int CompanyId, string Slug, string Name, string? Intro, string? Phone, string? WhatsApp, string? Email,
            bool ShowRent, string Currency);

        /// <summary>
        /// Finds the enabled page of an address and puts the rest of this request inside its
        /// company. Null when there is no such page or it is switched off.
        /// </summary>
        public async Task<PublicSite?> OpenPublicSiteAsync(string? slug, bool arabic)
        {
            slug = (slug ?? string.Empty).Trim().ToLowerInvariant();
            if (!SlugPattern.IsMatch(slug))
                return null;

            var site = await _db.ListingSites.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Slug == slug && x.IsEnabled)
                .Select(x => new
                {
                    Site = x,
                    CompanyName = x.Company!.Name,
                    x.Company.BaseCurrency
                })
                .FirstOrDefaultAsync();
            if (site == null)
                return null;

            // From here on, this request sees only this company's records.
            _companyOverride.CompanyId = site.Site.CompanyId;
            _companyOverride.UserId = null;

            var name = (arabic ? site.Site.DisplayNameAr : null) ?? site.Site.DisplayName ?? site.CompanyName;
            var intro = arabic ? site.Site.IntroAr ?? site.Site.Intro : site.Site.Intro ?? site.Site.IntroAr;
            return new PublicSite(site.Site.CompanyId, site.Site.Slug, name, intro, site.Site.ContactPhone,
                site.Site.WhatsAppNumber, site.Site.ContactEmail, site.Site.ShowRent, site.BaseCurrency);
        }

        public sealed record PublicUnit(
            int Id, string UnitNumber, int PropertyId, string PropertyName, string? AreaName, string? Emirate, string? Address,
            decimal? Latitude, decimal? Longitude, string? UsageType, string? Floor, decimal AreaSqFt, decimal Rent,
            int? Bedrooms, int? Bathrooms, bool IsFurnished, int? ParkingBays, string? Amenities,
            string? Description, string? DescriptionAr, List<int> PhotoIds, DateTime? ListedAt);

        public sealed class PublicFilter
        {
            public int? PropertyId { get; set; }
            public string? Usage { get; set; }
            public int? Bedrooms { get; set; }
            public decimal? MaxRent { get; set; }
            public string? Sort { get; set; }
        }

        private IQueryable<Unit> PublicUnits(int companyId)
        {
            var today = DateTime.Today;
            // The company is named here as well as by the request's filter: belt and braces
            // on the one query that answers people who are not signed in.
            return _db.Units.AsNoTracking()
                .Where(u => u.IsListed && u.IsActive && u.Status == "Vacant"
                    && u.Property != null && u.Property.IsActive && u.Property.CompanyId == companyId
                    && !u.Leases.Any(l => l.Status == "Active" && l.StartDate <= today && l.EndDate >= today));
        }

        public async Task<List<PublicUnit>> GetPublicUnitsAsync(PublicSite site, PublicFilter? filter = null, int? onlyUnitId = null)
        {
            var query = PublicUnits(site.CompanyId);
            if (onlyUnitId.HasValue)
                query = query.Where(u => u.Id == onlyUnitId.Value);

            var rows = await query
                .Select(u => new
                {
                    u.Id, u.UnitNumber, u.PropertyId,
                    PropertyName = u.Property!.Name, u.Property.AreaName, u.Property.Emirate, u.Property.Address,
                    u.Property.Latitude, u.Property.Longitude,
                    u.UsageType, u.Floor, u.AreaSqFt, u.StandardAnnualRent, u.AskingRent,
                    u.Bedrooms, u.Bathrooms, u.IsFurnished, u.ParkingBays, u.Amenities,
                    u.ListingDescription, u.ListingDescriptionAr, u.ListedAt,
                    PhotoIds = u.Photos.OrderBy(p => p.SortOrder).ThenBy(p => p.Id).Select(p => p.Id).ToList()
                })
                .ToListAsync();

            var units = rows.Select(u => new PublicUnit(
                u.Id, u.UnitNumber, u.PropertyId, u.PropertyName, u.AreaName, u.Emirate, u.Address, u.Latitude, u.Longitude,
                u.UsageType, u.Floor, u.AreaSqFt, u.AskingRent is > 0 ? u.AskingRent.Value : u.StandardAnnualRent,
                u.Bedrooms, u.Bathrooms, u.IsFurnished, u.ParkingBays, u.Amenities,
                u.ListingDescription, u.ListingDescriptionAr, u.PhotoIds, u.ListedAt));

            if (filter != null)
            {
                if (filter.PropertyId.HasValue)
                    units = units.Where(u => u.PropertyId == filter.PropertyId.Value);
                if (!string.IsNullOrWhiteSpace(filter.Usage))
                    units = units.Where(u => string.Equals(u.UsageType, filter.Usage, StringComparison.OrdinalIgnoreCase));
                if (filter.Bedrooms.HasValue)
                    units = units.Where(u => filter.Bedrooms.Value >= 4 ? u.Bedrooms >= 4 : u.Bedrooms == filter.Bedrooms.Value);
                if (filter.MaxRent is > 0 && site.ShowRent)
                    units = units.Where(u => u.Rent <= filter.MaxRent.Value);
            }

            units = (filter?.Sort, site.ShowRent) switch
            {
                ("rent", true) => units.OrderBy(u => u.Rent).ThenBy(u => u.UnitNumber),
                ("rent-desc", true) => units.OrderByDescending(u => u.Rent).ThenBy(u => u.UnitNumber),
                ("area", _) => units.OrderByDescending(u => u.AreaSqFt).ThenBy(u => u.UnitNumber),
                _ => units.OrderByDescending(u => u.ListedAt ?? DateTime.MinValue).ThenBy(u => u.PropertyName).ThenBy(u => u.UnitNumber)
            };
            return units.ToList();
        }

        public async Task<PublicUnit?> GetPublicUnitAsync(PublicSite site, int unitId) =>
            (await GetPublicUnitsAsync(site, null, unitId)).FirstOrDefault();

        public sealed class EnquiryInput
        {
            public int? UnitId { get; set; }
            public string? Name { get; set; }
            public string? Phone { get; set; }
            public string? Email { get; set; }
            public string? Message { get; set; }
        }

        /// <summary>Saves a visitor's enquiry. The unit is kept only if it is one the public can see.</summary>
        public async Task AddEnquiryAsync(PublicSite site, EnquiryInput input, string language)
        {
            var name = Clean(input.Name, 150);
            var phone = Clean(input.Phone, 30);
            var email = Clean(input.Email, 200);

            if (name == null || name.Length < 2)
                throw new InvalidOperationException("Please enter your name.");
            if (phone == null || (DigitsOnly(phone)?.Length ?? 0) < 7)
                throw new InvalidOperationException("Please enter a phone number we can reach you on.");
            if (email != null && !LooksLikeEmail(email))
                throw new InvalidOperationException("The email address does not look right.");

            int? unitId = null;
            if (input.UnitId.HasValue && await PublicUnits(site.CompanyId).AnyAsync(u => u.Id == input.UnitId.Value))
                unitId = input.UnitId.Value;

            _db.UnitEnquiries.Add(new UnitEnquiry
            {
                CompanyId = site.CompanyId,
                UnitId = unitId,
                Name = name,
                Phone = phone,
                Email = email,
                Message = Clean(input.Message, 1000),
                Language = language == "ar" ? "ar" : "en",
                Status = EnquiryStatuses.New,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        /// <summary>The headline of a unit: "2-bedroom home", "Studio", "Office"... in the reader's language.</summary>
        public static string Title(Localization.UiText text, string? usage, int? bedrooms)
        {
            var residential = string.IsNullOrWhiteSpace(usage) || string.Equals(usage, "Residential", StringComparison.OrdinalIgnoreCase);
            if (residential && bedrooms == 0)
                return text["Studio"];
            if (residential && bedrooms > 0)
                return text.Format("{0}-bedroom home", bedrooms);
            return usage?.Trim().ToLowerInvariant() switch
            {
                null or "" or "residential" => text["Residential unit"],
                "commercial" => text["Commercial unit"],
                "office" => text["Office"],
                "warehouse" => text["Warehouse"],
                _ => text[usage!.Trim()]
            };
        }

        // ---- helpers ----------------------------------------------------------------

        private async Task<string> FreeSlugAsync(string wanted, int companyId)
        {
            var slug = wanted;
            for (var n = 2; await _db.ListingSites.IgnoreQueryFilters().AnyAsync(x => x.Slug == slug && x.CompanyId != companyId); n++)
                slug = $"{wanted}-{n}";
            return slug;
        }

        private static string Slugify(string? name, int companyId)
        {
            var slug = Regex.Replace((name ?? string.Empty).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            if (slug.Length > 50) slug = slug[..50].Trim('-');
            // A name written in Arabic leaves nothing: fall back to a neutral address.
            return slug.Length >= 3 ? slug : $"company-{companyId}";
        }

        private static string? Clean(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = value.Trim();
            return text.Length > max ? text[..max] : text;
        }

        private static string? CleanLines(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var lines = value.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
            return Clean(string.Join("\n", lines), max);
        }

        private static string? DigitsOnly(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var digits = new string(value.Where(char.IsDigit).ToArray());
            return digits.Length == 0 ? null : digits;
        }

        private static bool LooksLikeEmail(string value) =>
            Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

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
