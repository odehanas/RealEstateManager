using System.ComponentModel.DataAnnotations;

namespace RealEstateManager.Domain
{
    public class Property
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }

        // Examples: Residential, Commercial, Warehouse, MixedUse
        public string PropertyType { get; set; } = string.Empty;

        // Owned or Managed
        public string OwnershipType { get; set; } = "Owned";

        // Default VAT treatment for units in this property. Units may override it,
        // which is what makes a mixed-use building work: commercial units standard
        // rated, residential units exempt, in the same building.
        public string VatCategory { get; set; } = VatCategories.Standard;

        public string? OwnerName { get; set; }

        /// <summary>
        /// The landlord's VAT registration number, for a managed property. Our commission
        /// tax invoice to a registered owner must show it.
        /// </summary>
        public string? OwnerTrn { get; set; }

        /// <summary>
        /// Managed property option: tenants are invoiced in the owner's name, the company
        /// acting as the owner's agent. The rent invoice carries the owner's TRN (no VAT
        /// when the owner is not registered), its VAT is the owner's and stays off the
        /// company's VAT return; the company's own income is its commission, invoiced to
        /// the owner under the company's TRN.
        /// </summary>
        public bool RentBilledInOwnerName { get; set; }

        /// <summary>Whether tenants of this property are invoiced in the owner's name.</summary>
        public bool BillsInOwnerName => OwnershipType == "Managed" && RentBilledInOwnerName && !string.IsNullOrWhiteSpace(OwnerName);
        public decimal AssetValue { get; set; } = 0m;
        public decimal CommissionPercentage { get; set; } = 0m;

        public bool IsActive { get; set; } = true;

        // ---- Location ----------------------------------------------------------

        public string? Emirate { get; set; }

        [Display(Name = "Area / community")]
        public string? AreaName { get; set; }

        [Display(Name = "Plot number")]
        public string? PlotNumber { get; set; }

        /// <summary>The land or municipality number the registry files the plot under.</summary>
        [Display(Name = "Land / municipality number")]
        public string? MunicipalityNumber { get; set; }

        /// <summary>A location code such as a Makani number.</summary>
        [Display(Name = "Location reference")]
        public string? LocationReference { get; set; }

        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }

        // ---- Title deed and land -----------------------------------------------

        [Display(Name = "Title deed number")]
        public string? TitleDeedNumber { get; set; }

        [Display(Name = "Title deed date")]
        [DataType(DataType.Date)]
        public DateTime? TitleDeedDate { get; set; }

        /// <summary>The land department or municipality that issued the deed.</summary>
        [Display(Name = "Issued by")]
        public string? TitleDeedAuthority { get; set; }

        /// <summary>Freehold, Leasehold, Musataha, Usufruct or Government grant.</summary>
        [Display(Name = "Tenure")]
        public string? TenureType { get; set; }

        /// <summary>Zoning on the affection plan: Residential, Commercial, Mixed use...</summary>
        [Display(Name = "Land use")]
        public string? LandUse { get; set; }

        [Display(Name = "Plot area (sq ft)")]
        public decimal? PlotAreaSqFt { get; set; }

        /// <summary>The company's share of the title, when it is owned jointly.</summary>
        [Display(Name = "Ownership share %")]
        public decimal? OwnershipPercentage { get; set; }

        [Display(Name = "Mortgaged")]
        public bool IsMortgaged { get; set; }

        /// <summary>Lender, mortgage reference and amount, when the title is pledged.</summary>
        [Display(Name = "Mortgage details")]
        public string? MortgageDetails { get; set; }

        // ---- Building and completion -------------------------------------------

        [Display(Name = "Building permit number")]
        public string? BuildingPermitNumber { get; set; }

        [Display(Name = "Building permit date")]
        [DataType(DataType.Date)]
        public DateTime? BuildingPermitDate { get; set; }

        [Display(Name = "Completion certificate number")]
        public string? CompletionCertificateNumber { get; set; }

        /// <summary>
        /// The date on the completion certificate. VAT turns on it: the first sale or
        /// lease of a new residential unit within three years of this date is zero rated.
        /// </summary>
        [Display(Name = "Completion date")]
        [DataType(DataType.Date)]
        public DateTime? CompletionDate { get; set; }

        [Display(Name = "Built-up area (sq ft)")]
        public decimal? BuiltUpAreaSqFt { get; set; }

        [Display(Name = "Number of floors")]
        public int? NumberOfFloors { get; set; }

        [Display(Name = "Parking bays")]
        public int? ParkingBays { get; set; }

        [Display(Name = "Developer")]
        public string? DeveloperName { get; set; }

        [Display(Name = "Main contractor")]
        public string? MainContractor { get; set; }

        [Display(Name = "Consultant")]
        public string? ConsultantName { get; set; }

        // ---- Utilities and insurance -------------------------------------------

        [Display(Name = "Electricity / water account")]
        public string? UtilityAccountNumber { get; set; }

        [Display(Name = "Insurer")]
        public string? Insurer { get; set; }

        [Display(Name = "Insurance policy number")]
        public string? InsurancePolicyNumber { get; set; }

        [Display(Name = "Insurance expiry")]
        [DataType(DataType.Date)]
        public DateTime? InsuranceExpiryDate { get; set; }

        [Display(Name = "Registration notes")]
        public string? RegistrationNotes { get; set; }

        // ---- Valuation ---------------------------------------------------------

        /// <summary>
        /// Latest independent valuation. Investment property is carried at cost less
        /// depreciation (cost model), so this figure is never booked - it is disclosed in
        /// the investment property note, as IAS 40 requires.
        /// </summary>
        [Display(Name = "Fair value")]
        public decimal? FairValue { get; set; }

        [Display(Name = "Valuation date")]
        [DataType(DataType.Date)]
        public DateTime? FairValueDate { get; set; }

        [Display(Name = "Valuer")]
        public string? ValuerName { get; set; }

        public ICollection<Unit> Units { get; set; } = new List<Unit>();
        public ICollection<PropertyDocument> Documents { get; set; } = new List<PropertyDocument>();

        /// <summary>
        /// Last day on which the first supply of a new residential unit is still zero rated:
        /// three years after completion. Null until a completion date is recorded.
        /// </summary>
        public DateTime? ZeroRatingWindowEnds => CompletionDate?.AddYears(3).AddDays(-1);

        /// <summary>Copies the registration details - everything below the core fields - from a submitted form.</summary>
        public void CopyRegistrationFrom(Property source)
        {
            static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

            Emirate = Clean(source.Emirate);
            AreaName = Clean(source.AreaName);
            PlotNumber = Clean(source.PlotNumber);
            MunicipalityNumber = Clean(source.MunicipalityNumber);
            LocationReference = Clean(source.LocationReference);
            Latitude = source.Latitude;
            Longitude = source.Longitude;

            TitleDeedNumber = Clean(source.TitleDeedNumber);
            TitleDeedDate = source.TitleDeedDate;
            TitleDeedAuthority = Clean(source.TitleDeedAuthority);
            TenureType = Clean(source.TenureType);
            LandUse = Clean(source.LandUse);
            PlotAreaSqFt = source.PlotAreaSqFt;
            OwnershipPercentage = source.OwnershipPercentage;
            IsMortgaged = source.IsMortgaged;
            MortgageDetails = source.IsMortgaged ? Clean(source.MortgageDetails) : null;

            BuildingPermitNumber = Clean(source.BuildingPermitNumber);
            BuildingPermitDate = source.BuildingPermitDate;
            CompletionCertificateNumber = Clean(source.CompletionCertificateNumber);
            CompletionDate = source.CompletionDate;
            BuiltUpAreaSqFt = source.BuiltUpAreaSqFt;
            NumberOfFloors = source.NumberOfFloors;
            ParkingBays = source.ParkingBays;
            DeveloperName = Clean(source.DeveloperName);
            MainContractor = Clean(source.MainContractor);
            ConsultantName = Clean(source.ConsultantName);

            UtilityAccountNumber = Clean(source.UtilityAccountNumber);
            Insurer = Clean(source.Insurer);
            InsurancePolicyNumber = Clean(source.InsurancePolicyNumber);
            InsuranceExpiryDate = source.InsuranceExpiryDate;
            RegistrationNotes = Clean(source.RegistrationNotes);

            FairValue = source.FairValue;
            FairValueDate = source.FairValueDate;
            ValuerName = Clean(source.ValuerName);
        }
    }

    /// <summary>Choices offered on the property form.</summary>
    public static class PropertyRegistrationLists
    {
        public static readonly string[] Emirates =
        {
            "Abu Dhabi", "Dubai", "Sharjah", "Ajman", "Umm Al Quwain", "Ras Al Khaimah", "Fujairah"
        };

        public static readonly string[] TenureTypes =
        {
            "Freehold", "Leasehold", "Musataha", "Usufruct", "Government grant"
        };

        public static readonly string[] LandUses =
        {
            "Residential", "Commercial", "Mixed use", "Industrial", "Hospitality", "Agricultural", "Other"
        };
    }
}
