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

            var policy = PasswordPolicy.Check(newPassword, user.Username);
            if (policy != null)
                throw new InvalidOperationException(policy);
            if (_passwordHashingService.VerifyPassword(newPassword, user.PasswordHash))
                throw new InvalidOperationException("Choose a password different from the current one.");

            user.PasswordHash = _passwordHashingService.HashPassword(newPassword);
            user.MustChangePassword = false;
            user.PasswordChangedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}
