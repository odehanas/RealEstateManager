using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RealEstateManager.Data
{
    /// <summary>
    /// The keys that protect sign-in cookies and form tokens. Kept in the database so a
    /// republish or an application pool recycle does not sign everyone out or reject
    /// forms that were open at the time. Not company data: no company filter.
    /// </summary>
    public partial class ApplicationDbContext : IDataProtectionKeyContext
    {
        public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    }
}
