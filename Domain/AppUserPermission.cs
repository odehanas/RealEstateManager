namespace RealEstateManager.Domain
{
    public class AppUserPermission
    {
        public int Id { get; set; }

        public int AppUserId { get; set; }
        public AppUser? AppUser { get; set; }

        public string PermissionKey { get; set; } = string.Empty;
        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    }
}
