namespace RealEstateManager.Domain
{
    /// <summary>
    /// A photograph of a building's facade - front, back or a side - for the photo mode of
    /// the building view. Each unit's windows or balcony are marked on it once as an outline
    /// (<see cref="UnitPhotoArea"/>), and the building view then tints every outline with the
    /// unit's rent-health colour.
    /// </summary>
    public class PropertyPhoto
    {
        public const int MaxFileBytes = 8 * 1024 * 1024;
        public static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png" };

        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int PropertyId { get; set; }
        public Property? Property { get; set; }

        /// <summary>What the photo shows: "Front", "Sea side", "Street view"...</summary>
        public string Title { get; set; } = "Front";

        public int SortOrder { get; set; }

        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "image/jpeg";
        public long SizeBytes { get; set; }

        /// <summary>The image. Loaded only when the picture itself is requested.</summary>
        public byte[] Content { get; set; } = Array.Empty<byte>();

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public string? UploadedByName { get; set; }

        public ICollection<UnitPhotoArea> Areas { get; set; } = new List<UnitPhotoArea>();
    }

    /// <summary>
    /// A unit's outline on a building photo: the corners of its windows or balcony, as
    /// fractions of the photo's width and height (0 to 1), so the outline fits the photo at
    /// any size it is shown.
    /// </summary>
    public class UnitPhotoArea
    {
        public int Id { get; set; }

        public int PropertyPhotoId { get; set; }
        public PropertyPhoto? PropertyPhoto { get; set; }

        public int UnitId { get; set; }
        public Unit? Unit { get; set; }

        /// <summary>The corners as JSON: [[x, y], [x, y], ...], each value from 0 to 1.</summary>
        public string PointsJson { get; set; } = "[]";

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
