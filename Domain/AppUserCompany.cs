namespace RealEstateManager.Domain
{
    public class AppUserCompany
    {
        public int Id { get; set; }

        public int AppUserId { get; set; }
        public AppUser? AppUser { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public bool IsDefault { get; set; } = false;

        /// <summary>
        /// The user administers this company: manages its users and their permissions.
        /// Held per company, so a customer's administrator has no say over any other company.
        /// </summary>
        public bool IsCompanyAdmin { get; set; }
    }
}
