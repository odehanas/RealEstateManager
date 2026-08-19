using QRCoder;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Renders a contract's QR code as an inline SVG data URI.
    ///
    /// SVG rather than PNG so it stays sharp at any print size, and a data URI so the
    /// contract label page has no external requests and prints correctly from any
    /// browser. QRCoder is pure managed code - it does not use System.Drawing, so it
    /// runs on the Linux and Windows hosting alike.
    /// </summary>
    public class ContractQrService
    {
        /// <summary>
        /// Payload encoded in the QR code. Deliberately the contract code alone rather
        /// than a URL: it stays valid if the domain ever changes, and any scanner shows
        /// something a human can act on.
        /// </summary>
        public static string BuildPayload(string contractNumber) => contractNumber;

        /// <summary>
        /// Returns a "data:image/svg+xml;base64,..." URI suitable for an img src.
        /// </summary>
        public string GetQrDataUri(string contractNumber, int pixelsPerModule = 5)
        {
            if (string.IsNullOrWhiteSpace(contractNumber))
                return string.Empty;

            using var generator = new QRCodeGenerator();

            // Q correction tolerates roughly 25% damage, which suits a label that may be
            // photocopied or scuffed on a paper contract.
            using var data = generator.CreateQrCode(
                BuildPayload(contractNumber),
                QRCodeGenerator.ECCLevel.Q);

            var svg = new SvgQRCode(data).GetGraphic(pixelsPerModule);
            var bytes = System.Text.Encoding.UTF8.GetBytes(svg);

            return "data:image/svg+xml;base64," + Convert.ToBase64String(bytes);
        }
    }
}
