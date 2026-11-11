using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Two-step sign-in for staff: after the password, a six-digit code from an
    /// authenticator app on the user's phone (Google Authenticator, Microsoft
    /// Authenticator, Authy... any app that follows the standard, RFC 6238). The phone
    /// and the server share a key; each works out the same code from the key and the
    /// time, so nothing is sent by SMS or email and no outside service is involved.
    ///
    /// A user who loses the phone signs in with one of the recovery codes shown when
    /// two-step sign-in was switched on, or asks an administrator to reset it.
    /// </summary>
    public class TwoStepSignInService
    {
        public const string PendingCookie = "RealEstateManager.TwoStep";
        public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);

        private const string Issuer = "RealEstateManager";
        private const int StepSeconds = 30;
        private const int RecoveryCodeCount = 8;
        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        private readonly ApplicationDbContext _db;
        private readonly IDataProtector _secrets;
        private readonly ITimeLimitedDataProtector _pending;

        public TwoStepSignInService(ApplicationDbContext db, IDataProtectionProvider protection)
        {
            _db = db;
            _secrets = protection.CreateProtector("TwoStepSignIn.Secret.v1");
            _pending = protection.CreateProtector("TwoStepSignIn.Pending.v1").ToTimeLimitedDataProtector();
        }

        // ---------------------------------------------------------------------
        //  Between the password and the code
        // ---------------------------------------------------------------------

        /// <summary>
        /// What the browser carries between the password page and the code page: who
        /// passed the password check, sealed so it cannot be forged and good for ten minutes.
        /// </summary>
        public string SealPending(int userId, string? returnUrl) =>
            _pending.Protect($"{userId}|{returnUrl}", PendingLifetime);

        public (int UserId, string? ReturnUrl)? OpenPending(string? sealedValue)
        {
            if (string.IsNullOrWhiteSpace(sealedValue))
                return null;

            try
            {
                var parts = _pending.Unprotect(sealedValue).Split('|', 2);
                return int.TryParse(parts[0], out var userId)
                    ? (userId, parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null)
                    : null;
            }
            catch (CryptographicException)
            {
                return null; // expired, or not ours
            }
        }

        // ---------------------------------------------------------------------
        //  Switching it on
        // ---------------------------------------------------------------------

        public sealed record Setup(string Key, string QrDataUri);

        /// <summary>
        /// The key for the user's authenticator app, as text and as a QR code. The key is
        /// kept on the user (encrypted) from this moment, but counts for nothing until
        /// <see cref="ConfirmSetupAsync"/> has seen a code made from it.
        /// </summary>
        public async Task<Setup> BeginSetupAsync(AppUser user)
        {
            var key = user.TwoFactorEnabled ? null : ReadKey(user);
            if (key == null)
            {
                key = Base32(RandomNumberGenerator.GetBytes(20));
                user.TwoFactorSecret = _secrets.Protect(key);
                user.TwoFactorEnabled = false;
                await _db.SaveChangesAsync();
            }

            var uri = $"otpauth://totp/{Uri.EscapeDataString(Issuer + ":" + user.Username)}" +
                      $"?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6&period={StepSeconds}";

            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
            var svg = new SvgQRCode(data).GetGraphic(5);

            return new Setup(
                string.Join(' ', Enumerable.Range(0, key.Length / 4).Select(i => key.Substring(i * 4, 4))),
                "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg)));
        }

        /// <summary>
        /// Switches two-step sign-in on once the user proves the app is set up by typing a
        /// code from it. Returns the recovery codes, which are shown this once only.
        /// </summary>
        public async Task<(List<string>? RecoveryCodes, string? Error)> ConfirmSetupAsync(AppUser user, string? code)
        {
            if (user.TwoFactorEnabled)
                return (null, "Two-step sign-in is already on.");

            var key = ReadKey(user);
            if (key == null)
                return (null, "Start again: scan the QR code shown on this page.");

            var step = MatchingStep(key, code);
            if (step == null)
                return (null, "That code is not right. Check the time on your phone is correct and type the code the app shows now.");

            user.TwoFactorEnabled = true;
            user.TwoFactorEnabledAt = DateTime.UtcNow;
            user.TwoFactorLastStep = step;
            var codes = IssueRecoveryCodes(user);
            await _db.SaveChangesAsync();
            return (codes, null);
        }

        // ---------------------------------------------------------------------
        //  Checking a code
        // ---------------------------------------------------------------------

        /// <summary>
        /// Checks the code typed at sign-in: the six digits from the app, or one of the
        /// recovery codes (which is then used up). Wrong codes count towards the same
        /// lock-out as wrong passwords, so the six digits cannot be guessed by trying.
        /// </summary>
        public async Task<(bool Ok, string? Error)> CheckAtSignInAsync(AppUser user, string? code)
        {
            if (user.LockedOutUntil.HasValue && user.LockedOutUntil > DateTime.UtcNow)
            {
                var minutes = Math.Max(1, (int)Math.Ceiling((user.LockedOutUntil.Value - DateTime.UtcNow).TotalMinutes));
                return (false, $"This account is locked after {AppAuthService.MaxFailedAttempts} wrong attempts. Try again in {minutes} minute(s), or ask an administrator.");
            }

            if (Accept(user, code))
            {
                user.FailedLoginAttempts = 0;
                await _db.SaveChangesAsync();
                return (true, null);
            }

            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= AppAuthService.MaxFailedAttempts)
            {
                user.LockedOutUntil = DateTime.UtcNow.Add(AppAuthService.LockoutDuration);
                user.FailedLoginAttempts = 0;
            }
            await _db.SaveChangesAsync();
            return (false, "That code is not right. Type the six digits your authenticator app shows now, or one of your recovery codes.");
        }

        /// <summary>Checks a code typed by a user who is already signed in (to switch off, or for new recovery codes).</summary>
        public async Task<bool> CheckAsync(AppUser user, string? code)
        {
            var ok = Accept(user, code);
            if (ok)
                await _db.SaveChangesAsync();
            return ok;
        }

        private bool Accept(AppUser user, string? code)
        {
            if (!user.TwoFactorEnabled)
                return false;

            var typed = new string((code ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (typed.Length == 0)
                return false;

            if (typed.Length == 6 && typed.All(char.IsDigit))
            {
                var key = ReadKey(user);
                var step = key == null ? null : MatchingStep(key, typed);

                // A code is good once: the one just used, or an older one, is refused, so
                // somebody who watched it being typed cannot use it in the same half minute.
                if (step == null || step <= user.TwoFactorLastStep)
                    return false;

                user.TwoFactorLastStep = step;
                return true;
            }

            var hashes = (user.TwoFactorRecoveryCodes ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
            var hash = Hash(typed);
            if (!hashes.Remove(hash))
                return false;

            user.TwoFactorRecoveryCodes = string.Join(';', hashes);
            return true;
        }

        // ---------------------------------------------------------------------
        //  Recovery codes, and switching off
        // ---------------------------------------------------------------------

        public static int RecoveryCodesLeft(AppUser user) =>
            (user.TwoFactorRecoveryCodes ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries).Length;

        /// <summary>Replaces the recovery codes with a new set; the old ones stop working.</summary>
        public async Task<List<string>> NewRecoveryCodesAsync(AppUser user)
        {
            var codes = IssueRecoveryCodes(user);
            await _db.SaveChangesAsync();
            return codes;
        }

        /// <summary>Back to the password alone: by the user, or by an administrator for one who lost the phone.</summary>
        public static void Clear(AppUser user)
        {
            user.TwoFactorEnabled = false;
            user.TwoFactorSecret = null;
            user.TwoFactorEnabledAt = null;
            user.TwoFactorRecoveryCodes = null;
            user.TwoFactorLastStep = null;
        }

        /// <summary>Whether any company this user works in requires two-step sign-in.</summary>
        public Task<bool> IsRequiredOfAsync(int userId) =>
            _db.AppUserCompanies.IgnoreQueryFilters()
                .AnyAsync(x => x.AppUserId == userId && x.Company!.RequireTwoFactor);

        private List<string> IssueRecoveryCodes(AppUser user)
        {
            var codes = Enumerable.Range(0, RecoveryCodeCount)
                .Select(_ => Base32(RandomNumberGenerator.GetBytes(7))[..10])
                .ToList();
            user.TwoFactorRecoveryCodes = string.Join(';', codes.Select(Hash));
            return codes.Select(x => x[..5] + "-" + x[5..]).ToList();
        }

        // ---------------------------------------------------------------------
        //  The arithmetic (RFC 6238: HMAC-SHA1, 30 seconds, 6 digits)
        // ---------------------------------------------------------------------

        private string? ReadKey(AppUser user)
        {
            if (string.IsNullOrEmpty(user.TwoFactorSecret))
                return null;

            try
            {
                return _secrets.Unprotect(user.TwoFactorSecret);
            }
            catch (CryptographicException)
            {
                return null; // the server's keys were replaced: the user sets it up again
            }
        }

        /// <summary>
        /// The time step whose code was typed, or null. The step before and the step after
        /// are accepted too, for a phone whose clock is a little off and for the seconds it
        /// takes to type.
        /// </summary>
        private static long? MatchingStep(string key, string? code)
        {
            var typed = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
            if (typed.Length != 6)
                return null;

            var keyBytes = FromBase32(key);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;
            long? match = null;
            for (var step = now - 1; step <= now + 1; step++)
            {
                var same = CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(CodeAt(keyBytes, step)), Encoding.ASCII.GetBytes(typed));
                if (same)
                    match = step;
            }
            return match;
        }

        public static string CodeAt(byte[] key, long step)
        {
            var counter = BitConverter.GetBytes(step);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(counter);

            var hash = HMACSHA1.HashData(key, counter);
            var offset = hash[^1] & 0x0F;
            var number = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
            return (number % 1_000_000).ToString("D6");
        }

        private static string Hash(string code) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

        private static string Base32(byte[] bytes)
        {
            var text = new StringBuilder();
            int buffer = 0, bits = 0;
            foreach (var b in bytes)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    text.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
            }
            if (bits > 0)
                text.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
            return text.ToString();
        }

        public static byte[] FromBase32(string text)
        {
            var bytes = new List<byte>();
            int buffer = 0, bits = 0;
            foreach (var c in text.ToUpperInvariant())
            {
                var value = Base32Alphabet.IndexOf(c);
                if (value < 0)
                    continue;
                buffer = (buffer << 5) | value;
                bits += 5;
                if (bits >= 8)
                {
                    bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                    bits -= 8;
                }
            }
            return bytes.ToArray();
        }
    }
}
