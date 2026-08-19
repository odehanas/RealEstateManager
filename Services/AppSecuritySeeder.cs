using System.Data;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    public static class AppSecuritySeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var passwordHashingService = scope.ServiceProvider.GetRequiredService<PasswordHashingService>();

            if (!await AuthTablesExistAsync(db))
                return;

            if (!await db.Companies.AnyAsync())
                return;

            var adminUser = await db.AppUsers
                .Include(x => x.CompanyAccesses)
                .FirstOrDefaultAsync(x => x.Username == "ADMIN");

            if (adminUser == null)
            {
                adminUser = new AppUser
                {
                    Username = "ADMIN",
                    DisplayName = "System Administrator",
                    PasswordHash = passwordHashingService.HashPassword("Admin@123"),
                    IsActive = true,
                    IsSystemAdmin = true,
                    CreatedAt = DateTime.UtcNow
                };

                db.AppUsers.Add(adminUser);
                await db.SaveChangesAsync();
                await db.Entry(adminUser).Collection(x => x.CompanyAccesses).LoadAsync();
            }
            else if (adminUser.IsSystemAdmin &&
                     adminUser.LastLoginAt == null &&
                     !passwordHashingService.VerifyPassword("Admin@123", adminUser.PasswordHash))
            {
                // Repair bootstrap environments where ADMIN was inserted before password hashing was finalized.
                adminUser.PasswordHash = passwordHashingService.HashPassword("Admin@123");
                adminUser.IsActive = true;
                await db.SaveChangesAsync();
            }

            var companyIds = await db.Companies
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync();

            var hasDefaultCompany = adminUser.CompanyAccesses.Any(x => x.IsDefault);

            foreach (var companyId in companyIds)
            {
                if (adminUser.CompanyAccesses.Any(x => x.CompanyId == companyId))
                    continue;

                var userCompany = new AppUserCompany
                {
                    AppUserId = adminUser.Id,
                    CompanyId = companyId,
                    IsDefault = !hasDefaultCompany
                };

                db.AppUserCompanies.Add(userCompany);
                adminUser.CompanyAccesses.Add(userCompany);
                hasDefaultCompany = true;
            }

            await db.SaveChangesAsync();
        }

        private static async Task<bool> AuthTablesExistAsync(ApplicationDbContext db)
        {
            var connection = db.Database.GetDbConnection();
            var shouldCloseConnection = connection.State != ConnectionState.Open;

            if (shouldCloseConnection)
            {
                await connection.OpenAsync();
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT COUNT(1)
                    FROM sys.tables
                    WHERE name IN ('AppUsers', 'AppUserCompanies');
                    """;

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result) == 2;
            }
            finally
            {
                if (shouldCloseConnection)
                {
                    await connection.CloseAsync();
                }
            }
        }
    }
}
