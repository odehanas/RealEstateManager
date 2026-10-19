namespace RealEstateManager.Domain
{
    public class Tenant
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Code { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Nationality { get; set; }
        public string? IdNumber { get; set; }
        public string? PassportNumber { get; set; }
        public string? Address { get; set; }

        // Commercial licence number for a corporate tenant. Together with the Emirates
        // ID, passport and TRN this is one of the identifiers the system refuses to
        // duplicate, so the same party cannot be set up twice under two codes.
        public string? TradeLicenseNumber { get; set; }

        // Tax Registration Number. When present the tenant gets a FULL tax invoice
        // showing their TRN; when absent, a simplified tax invoice.
        public string? TrnNumber { get; set; }

        // ---------------------------------------------------------------------
        // E-invoicing. Whether a tenant's invoices go on the network depends on who
        // they are, not on whether they hold a TRN: a company that is not VAT
        // registered is still a business, and B2B invoices are in scope either way.
        // ---------------------------------------------------------------------

        /// <summary>Individual, Business or Government - see <see cref="PartyTypes"/>.</summary>
        public string PartyType { get; set; } = PartyTypes.Individual;

        /// <summary>
        /// The tenant's Tax Identification Number - their address on the e-invoicing
        /// network. Blank means "take it from the TRN".
        /// </summary>
        public string? TaxIdentificationNumber { get; set; }

        /// <summary>
        /// Which registration identifies a business tenant on the e-invoice: TL, EID,
        /// PAS or CD. Blank means work it out from the numbers on file.
        /// </summary>
        public string? LegalRegistrationType { get; set; }

        /// <summary>Authority that issued the tenant's trade licence.</summary>
        public string? LicensingAuthority { get; set; }

        /// <summary>ISO country code of the passport, when a passport identifies the tenant.</summary>
        public string? PassportIssuingCountry { get; set; }

        // Address lines. The existing Address field is the street address (line 1).
        public string? City { get; set; }

        /// <summary>Emirate code (AUH, DXB, SHJ, AJM, UAQ, RAK, FUJ).</summary>
        public string? Emirate { get; set; }

        public string CountryCode { get; set; } = "AE";

        /// <summary>The TIN to use: the one entered, else the first 10 digits of the TRN.</summary>
        public string? EffectiveTin =>
            !string.IsNullOrWhiteSpace(TaxIdentificationNumber)
                ? TaxIdentificationNumber.Trim()
                : Company.TinFromTrn(TrnNumber);

        // Opening balance brought forward from a legacy system / prior period.
        // Positive = receivable from this tenant (debit nature in AR control account).
        // Negative = advance held for this tenant (credit balance).
        public decimal OpeningBalance { get; set; } = 0m;

        public bool IsActive { get; set; } = true;

        public ICollection<Lease> Leases { get; set; } = new List<Lease>();
    }
}