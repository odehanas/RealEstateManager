namespace RealEstateManager.Domain
{
    /// <summary>
    /// The owner of a managed property: the person or company whose rent we collect.
    ///
    /// Properties also keep the landlord's name and TRN as text (OwnerName, OwnerTrn),
    /// because settlements, commission invoices and reports group on that text. Saving a
    /// property with a landlord copies them across, so the two never disagree.
    /// </summary>
    public class Landlord
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>VAT registration number, printed on our commission tax invoices.</summary>
        public string? Trn { get; set; }

        public string? Email { get; set; }
        public string? Phone { get; set; }

        // ---- Where the landlord's own money goes ------------------------------
        public string? BankName { get; set; }
        public string? AccountName { get; set; }
        public string? Iban { get; set; }

        /// <summary>How rent is collected on a new property of this landlord. See <see cref="CollectionModes"/>.</summary>
        public string DefaultCollectionMode { get; set; } = CollectionModes.Company;

        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Who holds the rent of a managed property. It decides only the account the money
    /// lands in when a tenant pays: the receipt, or the cheque when it clears.
    /// </summary>
    public static class CollectionModes
    {
        /// <summary>Into the company's own bank, as before.</summary>
        public const string Company = "Company";

        /// <summary>Into a company bank account kept for owners' money only.</summary>
        public const string OwnersFunds = "OwnersFunds";

        /// <summary>
        /// Straight into the landlord's own bank account; cheques are written to the
        /// landlord. No company bank moves: what the landlord banks reduces what we owe them.
        /// </summary>
        public const string Owner = "Owner";

        public static readonly string[] All = { Company, OwnersFunds, Owner };

        public static string Clean(string? mode) => All.Contains(mode) ? mode! : Company;

        public static string Describe(string? mode) => mode switch
        {
            OwnersFunds => "Owners' funds bank account",
            Owner => "Paid to the owner directly",
            _ => "Company bank account"
        };
    }

    /// <summary>
    /// A bank account the company holds, linked to its account in the chart of accounts.
    /// An owners' funds account holds only landlords' money; one linked to a landlord holds
    /// only that landlord's.
    /// </summary>
    public class BankAccount
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>What staff call it: "ENBD current", "Owners' funds - ADCB".</summary>
        public string Name { get; set; } = string.Empty;

        public string? BankName { get; set; }
        public string? Iban { get; set; }

        /// <summary>The ledger account its balance is kept in.</summary>
        public int AccountId { get; set; }
        public Account? Account { get; set; }

        /// <summary>See <see cref="BankAccountPurposes"/>.</summary>
        public string Purpose { get; set; } = BankAccountPurposes.Operating;

        /// <summary>An owners' funds account kept for one landlord alone; empty when it serves them all.</summary>
        public int? LandlordId { get; set; }
        public Landlord? Landlord { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public static class BankAccountPurposes
    {
        public const string Operating = "Operating";
        public const string OwnersFunds = "OwnersFunds";

        public static readonly string[] All = { Operating, OwnersFunds };

        public static string Describe(string purpose) => purpose == OwnersFunds ? "Owners' funds" : "Company operating";
    }
}
