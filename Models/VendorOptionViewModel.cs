namespace RealEstateManager.Models
{
    /// <summary>
    /// A supplier as the bill form needs to see it. Carries the VAT position so the
    /// browser can switch the whole document between a tax invoice and a commercial
    /// invoice the moment the supplier is chosen, without a round trip.
    /// </summary>
    public class VendorOptionViewModel
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? TrnNumber { get; set; }
        public bool IsVatRegistered { get; set; }

        /// <summary>Registered AND holding a TRN. Anything less means no tax may be charged.</summary>
        public bool CanChargeVat => IsVatRegistered && !string.IsNullOrWhiteSpace(TrnNumber);

        public string DisplayName => $"{Code} - {Name}";
    }
}
