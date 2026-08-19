using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;

namespace RealEstateManager.Services
{
    public class ProfileService
    {
        private readonly ApplicationDbContext _db;
        private readonly PasswordHashingService _passwordHashingService;

        public ProfileService(
            ApplicationDbContext db,
            PasswordHashingService passwordHashingService)
        {
            _db = db;
            _passwordHashingService = passwordHashingService;
        }

        public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword)
        {
            var user = await _db.AppUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == userId && x.IsActive);

            if (user == null)
                throw new InvalidOperationException("User was not found.");

            if (!_passwordHashingService.VerifyPassword(currentPassword, user.PasswordHash))
                throw new InvalidOperationException("Current password is incorrect.");

            user.PasswordHash = _passwordHashingService.HashPassword(newPassword);
            await _db.SaveChangesAsync();
        }
    }
}
