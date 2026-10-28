using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RealEstateManager.Services.Push
{
    /// <summary>
    /// The server's notification keys (VAPID), from configuration: Push:PublicKey,
    /// Push:PrivateKey and Push:Subject. They are secrets like the database password -
    /// kept in the hosting environment and local user-secrets, never in the repository.
    /// Without them the application still installs on phones; it just sends nothing.
    /// </summary>
    public class PushOptions
    {
        public string? PublicKey { get; set; }
        public string? PrivateKey { get; set; }

        /// <summary>Who to contact about these notifications: the site's address or a mailto: link.</summary>
        public string? Subject { get; set; }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
    }

    public enum PushResult { Sent, Gone, Failed }

    /// <summary>
    /// Sends one notification to one device using the Web Push standard: the message is
    /// encrypted for that device alone (RFC 8291, aes128gcm) and the request is signed with
    /// the server's key (RFC 8292, VAPID). Google, Apple and Mozilla's push services all
    /// accept it, so the same code reaches Android, iPhone and desktop browsers.
    /// </summary>
    public class WebPushSender
    {
        private const int RecordSize = 4096;
        private readonly HttpClient _http;
        private readonly PushOptions _options;
        private readonly ILogger<WebPushSender> _logger;

        public WebPushSender(HttpClient http, IOptions<PushOptions> options, ILogger<WebPushSender> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public bool IsConfigured => _options.IsConfigured;

        public async Task<PushResult> SendAsync(string endpoint, string p256dh, string auth, object payload, CancellationToken cancellation)
        {
            if (!IsConfigured || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return PushResult.Failed;

            byte[] body;
            try
            {
                body = Encrypt(Base64Url.Decode(p256dh), Base64Url.Decode(auth), JsonSerializer.SerializeToUtf8Bytes(payload));
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
            {
                // Keys the browser sent that cannot be used: the subscription is worthless.
                _logger.LogWarning(ex, "Push subscription keys for {Host} are not usable.", uri.Host);
                return PushResult.Gone;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new("application/octet-stream");
            request.Content.Headers.ContentEncoding.Add("aes128gcm");
            request.Headers.Add("TTL", "86400");
            request.Headers.Add("Urgency", "normal");
            request.Headers.TryAddWithoutValidation("Authorization",
                $"vapid t={VapidToken(uri)}, k={_options.PublicKey!.Trim()}");

            try
            {
                using var response = await _http.SendAsync(request, cancellation);
                if (response.IsSuccessStatusCode)
                    return PushResult.Sent;

                // 404 and 410: the person turned notifications off or the browser dropped them.
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                    return PushResult.Gone;

                _logger.LogWarning("Push to {Host} was refused: {Status}.", uri.Host, (int)response.StatusCode);
                return PushResult.Failed;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Push to {Host} could not be delivered.", uri.Host);
                return PushResult.Failed;
            }
        }

        /// <summary>A key pair for configuration, printed by the setup step; see the help topic.</summary>
        public static (string PublicKey, string PrivateKey) CreateKeys()
        {
            using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var parameters = key.ExportParameters(true);
            return (Base64Url.Encode(Uncompressed(parameters.Q)), Base64Url.Encode(parameters.D!));
        }

        // ---------------------------------------------------------------------
        //  RFC 8292: the signed token that proves the message comes from this server
        // ---------------------------------------------------------------------

        private string VapidToken(Uri endpoint)
        {
            var header = Base64Url.Encode(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"));
            var claims = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["aud"] = endpoint.GetLeftPart(UriPartial.Authority),
                ["exp"] = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds(),
                ["sub"] = string.IsNullOrWhiteSpace(_options.Subject) ? "https://realestatemanager.odehanas.com" : _options.Subject.Trim()
            }));

            var publicKey = Base64Url.Decode(_options.PublicKey!.Trim());
            using var signer = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = Base64Url.Decode(_options.PrivateKey!.Trim()),
                Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] }
            });

            // .NET signs in the fixed r||s form that JWT's ES256 expects.
            var signature = signer.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256);
            return $"{header}.{claims}.{Base64Url.Encode(signature)}";
        }

        // ---------------------------------------------------------------------
        //  RFC 8291: encrypting the message for the one device
        // ---------------------------------------------------------------------

        private static byte[] Encrypt(byte[] devicePublicKey, byte[] authSecret, byte[] plaintext)
        {
            if (devicePublicKey.Length != 65 || devicePublicKey[0] != 0x04 || authSecret.Length != 16)
                throw new ArgumentException("Not a P-256 push subscription.");

            using var device = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = devicePublicKey[1..33], Y = devicePublicKey[33..65] }
            });
            using var server = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var serverPublicKey = Uncompressed(server.ExportParameters(false).Q);

            // PRK_key = HMAC-SHA256(auth_secret, ecdh_secret): the HKDF extract step, done by
            // the key agreement itself so the shared secret never leaves the crypto library.
            var prkKey = server.DeriveKeyFromHmac(device.PublicKey, HashAlgorithmName.SHA256, authSecret);
            var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), devicePublicKey, serverPublicKey);
            var ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, keyInfo);

            var salt = RandomNumberGenerator.GetBytes(16);
            var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
            var contentKey = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
            var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

            // One record: the message, then the 0x02 delimiter that marks it as the last.
            var record = Concat(plaintext, new byte[] { 0x02 });
            if (record.Length + 16 > RecordSize)
                throw new ArgumentException("The notification is too long.");

            var cipher = new byte[record.Length];
            var tag = new byte[16];
            using (var aes = new AesGcm(contentKey, 16))
                aes.Encrypt(nonce, record, cipher, tag);

            // Header: salt, record size, the server's public key; then the encrypted record.
            var recordSize = new byte[] { 0, 0, RecordSize >> 8 & 0xFF, RecordSize & 0xFF };
            return Concat(salt, recordSize, new[] { (byte)serverPublicKey.Length }, serverPublicKey, cipher, tag);
        }

        private static byte[] Uncompressed(ECPoint point) => Concat(new byte[] { 0x04 }, point.X!, point.Y!);

        private static byte[] Concat(params byte[][] parts)
        {
            var result = new byte[parts.Sum(x => x.Length)];
            var offset = 0;
            foreach (var part in parts)
            {
                Buffer.BlockCopy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }
            return result;
        }
    }

    /// <summary>The URL-safe base64 without padding that browsers use for push keys.</summary>
    public static class Base64Url
    {
        public static string Encode(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static byte[] Decode(string text)
        {
            var s = text.Trim().Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
        }
    }
}
