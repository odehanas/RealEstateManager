using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services.WhatsApp
{
    /// <summary>
    /// Each company's WhatsApp credentials, and the two checks they exist for. The secrets
    /// are encrypted with the application's data protection keys under a purpose of their
    /// own, so nothing else that uses those keys can read them.
    /// </summary>
    public class WhatsAppConnectionService
    {
        private const string SignaturePrefix = "sha256=";

        private readonly ApplicationDbContext _db;
        private readonly IDataProtector _protector;
        private readonly ILogger<WhatsAppConnectionService> _logger;

        public WhatsAppConnectionService(
            ApplicationDbContext db,
            IDataProtectionProvider protection,
            ILogger<WhatsAppConnectionService> logger)
        {
            _db = db;
            _protector = protection.CreateProtector("RealEstateManager.WhatsApp.Credentials.v1");
            _logger = logger;
        }

        /// <summary>The connection behind a webhook address. A switched-off one is treated as not there.</summary>
        public Task<WhatsAppConnection?> FindEnabledAsync(string? publicKey) =>
            string.IsNullOrWhiteSpace(publicKey)
                ? Task.FromResult<WhatsAppConnection?>(null)
                : _db.WhatsAppConnections.AsNoTracking().IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.PublicKey == publicKey && x.IsEnabled);

        /// <summary>Meta quoting the token when the webhook address is registered.</summary>
        public bool VerifyToken(WhatsAppConnection connection, string? token)
        {
            var expected = Reveal(connection, connection.ProtectedVerifyToken);
            return expected != null && !string.IsNullOrEmpty(token) && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(token));
        }

        /// <summary>
        /// Checks X-Hub-Signature-256: "sha256=" and the HMAC-SHA256 of the raw bytes of the
        /// body under the App Secret. It has to be the bytes as they arrived, as re-reading the
        /// JSON would change them.
        /// </summary>
        public bool VerifySignature(WhatsAppConnection connection, byte[] body, string? header)
        {
            if (string.IsNullOrEmpty(header) || !header.StartsWith(SignaturePrefix, StringComparison.Ordinal))
                return false;

            byte[] supplied;
            try
            {
                supplied = Convert.FromHexString(header[SignaturePrefix.Length..]);
            }
            catch (FormatException)
            {
                return false;
            }

            var secret = Reveal(connection, connection.ProtectedAppSecret);
            if (secret == null)
                return false;

            return CryptographicOperations.FixedTimeEquals(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body), supplied);
        }

        public async Task<WhatsAppSettingsViewModel> BuildSettingsAsync(int companyId)
        {
            var connection = await _db.WhatsAppConnections.AsNoTracking()
                .FirstOrDefaultAsync(x => x.CompanyId == companyId);

            if (connection == null)
                return new WhatsAppSettingsViewModel();

            return new WhatsAppSettingsViewModel
            {
                Exists = true,
                PublicKey = connection.PublicKey,
                PhoneNumberId = connection.PhoneNumberId,
                DisplayPhoneNumber = connection.DisplayPhoneNumber,
                DefaultCallingCode = connection.DefaultCallingCode,
                IsEnabled = connection.IsEnabled,
                VerifyToken = Reveal(connection, connection.ProtectedVerifyToken)
            };
        }

        /// <summary>Saves the settings. An empty App Secret on an existing connection keeps the one stored.</summary>
        public async Task<bool> SaveAsync(ModelStateDictionary modelState, int companyId, WhatsAppSettingsViewModel model, int? changedBy)
        {
            var connection = await _db.WhatsAppConnections.FirstOrDefaultAsync(x => x.CompanyId == companyId);

            var phoneNumberId = model.PhoneNumberId?.Trim() ?? string.Empty;
            var callingCode = model.DefaultCallingCode?.Trim().TrimStart('+') ?? string.Empty;
            var secret = model.AppSecret?.Trim();

            if (phoneNumberId.Length is 0 or > 50 || !phoneNumberId.All(char.IsAsciiDigit))
                modelState.AddModelError(nameof(model.PhoneNumberId), "The Phone Number ID is the long number Meta shows under API Setup, in digits only.");

            if (callingCode.Length is 0 or > 4 || !callingCode.All(char.IsAsciiDigit) || callingCode[0] == '0')
                modelState.AddModelError(nameof(model.DefaultCallingCode), "Enter the country calling code in digits, for example 971.");

            if (string.IsNullOrEmpty(secret))
            {
                if (connection == null)
                    modelState.AddModelError(nameof(model.AppSecret), "Enter the App Secret of your Meta app.");
            }
            else if (secret.Length is < 16 or > 200)
            {
                modelState.AddModelError(nameof(model.AppSecret), "That does not look like an App Secret. Copy it again from your Meta app.");
            }

            if (modelState.IsValid && await _db.WhatsAppConnections.IgnoreQueryFilters()
                    .AnyAsync(x => x.PhoneNumberId == phoneNumberId && x.CompanyId != companyId))
            {
                modelState.AddModelError(nameof(model.PhoneNumberId), "This WhatsApp number is already connected to another company.");
            }

            if (!modelState.IsValid)
                return false;

            if (connection == null)
            {
                connection = new WhatsAppConnection
                {
                    CompanyId = companyId,
                    PublicKey = RandomNumberGenerator.GetHexString(40, lowercase: true),
                    ProtectedVerifyToken = _protector.Protect(RandomNumberGenerator.GetHexString(40, lowercase: true))
                };
                _db.WhatsAppConnections.Add(connection);
            }

            connection.PhoneNumberId = phoneNumberId;
            connection.DisplayPhoneNumber = string.IsNullOrWhiteSpace(model.DisplayPhoneNumber) ? null : model.DisplayPhoneNumber.Trim();
            connection.DefaultCallingCode = callingCode;
            connection.IsEnabled = model.IsEnabled;
            connection.ChangedAt = DateTime.UtcNow;
            connection.ChangedByAppUserId = changedBy;
            if (!string.IsNullOrEmpty(secret))
                connection.ProtectedAppSecret = _protector.Protect(secret);

            await _db.SaveChangesAsync();
            return true;
        }

        private string? Reveal(WhatsAppConnection connection, string protectedValue)
        {
            try
            {
                return _protector.Unprotect(protectedValue);
            }
            catch (CryptographicException ex)
            {
                // The keys were lost or replaced. Refuse everything until the secret is entered again.
                _logger.LogError(ex, "The WhatsApp credentials of company {CompanyId} could not be decrypted.", connection.CompanyId);
                return null;
            }
        }
    }
}
