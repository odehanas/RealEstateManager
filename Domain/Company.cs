namespace RealEstateManager.Domain
{
    public class Company
    {
        /// <summary>How costs of owners' managed properties are passed on (see OwnerCostTreatments).</summary>
        public string OwnerCostTreatment { get; set; } = RealEstateManager.Domain.OwnerCostTreatments.Recharge;

        /// <summary>Charged to a tenant who asks for a cheque back before it is deposited (editable on each withdrawal).</summary>
        public decimal PdcWithdrawalCharge { get; set; } = 500m;

        /// <summary>Charged to a tenant whose cheque is returned unpaid; 0 for none (editable on each bounce).</summary>
        public decimal PdcBounceCharge { get; set; } = 0m;

        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string BaseCurrency { get; set; } = "AED";
        public int FiscalYearStartMonth { get; set; } = 1;

        /// <summary>
        /// When on, a new contract must be approved by a second person - one holding the
        /// Approve Contracts permission who neither created nor submitted it - before it
        /// takes effect. When off, its creator activates it.
        /// </summary>
        public bool RequireContractApproval { get; set; }

        /// <summary>
        /// When on, every payment (supplier payments, petty cash, owner settlements and
        /// withdrawals, payroll, salary advances) needs someone whose approval level covers
        /// the amount before it reaches the accounts.
        /// </summary>
        public bool RequirePaymentApproval { get; set; }

        /// <summary>
        /// Whether the person who enters a payment may release it themselves when it is within
        /// their own limit. Petty cash is never self-released: someone else always checks it.
        /// </summary>
        public bool AllowSelfReleaseWithinLimit { get; set; } = true;

        /// <summary>
        /// When on, approving a tenancy contract also needs an approval level that covers the
        /// contract's annual rent - the same limits as payments. Off: any contract approver may
        /// approve any contract (still never their own).
        /// </summary>
        public bool ApplyLimitsToContracts { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ---------------------------------------------------------------------
        // VAT identity. A tax invoice must carry the supplier's legal name,
        // address and TRN, so these are required before one can be issued.
        // ---------------------------------------------------------------------

        /// <summary>Registered legal name, if it differs from the trading name.</summary>
        public string? LegalName { get; set; }

        /// <summary>Tax Registration Number. Null means the company is not VAT registered.</summary>
        public string? TrnNumber { get; set; }

        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; } = "United Arab Emirates";
        public string? Phone { get; set; }
        public string? Email { get; set; }

        /// <summary>
        /// Provisional input tax recovery rate (percentage) applied to residual input
        /// tax during the year, before the annual apportionment wash-up. Normally the
        /// previous year's actual rate.
        /// </summary>
        public decimal ProvisionalVatRecoveryRate { get; set; } = 100m;

        public bool IsVatRegistered => !string.IsNullOrWhiteSpace(TrnNumber);

        // ---------------------------------------------------------------------
        // E-invoicing identity. Every e-invoice must identify the seller on the
        // Peppol network (its TIN), by its commercial registration, and by a
        // structured address that names the emirate.
        // ---------------------------------------------------------------------

        /// <summary>
        /// Tax Identification Number: the first 10 digits of the TRN for a VAT
        /// registered business, or the TIN the FTA issues to one that is not.
        /// Blank means "take it from the TRN".
        /// </summary>
        public string? TaxIdentificationNumber { get; set; }

        public string? TradeLicenseNumber { get; set; }

        /// <summary>TL, EID, PAS or CD - see <see cref="LegalRegistrationTypes"/>.</summary>
        public string LegalRegistrationType { get; set; } = LegalRegistrationTypes.TradeLicence;

        /// <summary>Authority that issued the trade licence, e.g. "Department of Economy and Tourism".</summary>
        public string? LicensingAuthority { get; set; }

        /// <summary>Emirate code (AUH, DXB, SHJ, AJM, UAQ, RAK, FUJ).</summary>
        public string? Emirate { get; set; }

        /// <summary>ISO 3166 country code of the registered address.</summary>
        public string CountryCode { get; set; } = "AE";

        /// <summary>Printed as the account to pay into when the payment means is a transfer.</summary>
        public string? BankIban { get; set; }
        public string? BankAccountName { get; set; }

        // ---------------------------------------------------------------------
        // E-invoicing switch and provider.
        // ---------------------------------------------------------------------

        /// <summary>
        /// Off until the company has appointed a provider. While off, every invoice is
        /// marked Not Required and nothing changes for the user.
        /// </summary>
        public bool EInvoicingEnabled { get; set; }

        /// <summary>Large, Standard or Government - decides the statutory dates.</summary>
        public string? EInvoicingRevenueBand { get; set; }

        /// <summary>
        /// Supplies on or after this date are e-invoiced. Defaults to the statutory date
        /// for the revenue band; may be earlier for voluntary adoption.
        /// </summary>
        public DateTime? EInvoicingStartDate { get; set; }

        /// <summary>The Accredited Service Provider appointed by the company.</summary>
        public string? EInvoicingProviderName { get; set; }
        public DateTime? EInvoicingProviderAppointedOn { get; set; }

        /// <summary>Payment means code printed on every e-invoice (UNCL 4461).</summary>
        public string EInvoicePaymentMeansCode { get; set; } = EInvoicePaymentMeans.CreditTransfer;

        /// <summary>The TIN to use: the one entered, else the first 10 digits of the TRN.</summary>
        public string? EffectiveTin =>
            !string.IsNullOrWhiteSpace(TaxIdentificationNumber)
                ? TaxIdentificationNumber.Trim()
                : TinFromTrn(TrnNumber);

        /// <summary>A TRN is the TIN followed by five more digits.</summary>
        public static string? TinFromTrn(string? trn)
        {
            var digits = trn?.Trim();
            return !string.IsNullOrEmpty(digits) && digits.Length == 15 && digits.All(char.IsDigit)
                ? digits[..10]
                : null;
        }

        public CompanyAccountSetting? AccountSetting { get; set; }
        public CompanySubscription? Subscription { get; set; }

        public ICollection<Account> Accounts { get; set; } = new List<Account>();
        public ICollection<AccountingPeriod> AccountingPeriods { get; set; } = new List<AccountingPeriod>();
        public ICollection<FiscalYearClosing> FiscalYearClosings { get; set; } = new List<FiscalYearClosing>();

        public ICollection<Property> Properties { get; set; } = new List<Property>();
        public ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
        public ICollection<Lease> Leases { get; set; } = new List<Lease>();

        public ICollection<Vendor> Vendors { get; set; } = new List<Vendor>();
        public ICollection<CustomerInvoice> CustomerInvoices { get; set; } = new List<CustomerInvoice>();
        public ICollection<CustomerReceipt> CustomerReceipts { get; set; } = new List<CustomerReceipt>();
        public ICollection<VendorBill> VendorBills { get; set; } = new List<VendorBill>();
        public ICollection<VendorPayment> VendorPayments { get; set; } = new List<VendorPayment>();
        public ICollection<SecurityDeposit> SecurityDeposits { get; set; } = new List<SecurityDeposit>();
        public ICollection<SubscriptionActivationCode> SubscriptionActivationCodes { get; set; } = new List<SubscriptionActivationCode>();
        public ICollection<PrepaymentSchedule> PrepaymentSchedules { get; set; } = new List<PrepaymentSchedule>();
        public ICollection<RevenueRecognitionSchedule> RevenueRecognitionSchedules { get; set; } = new List<RevenueRecognitionSchedule>();
        public ICollection<UserFeedback> FeedbackItems { get; set; } = new List<UserFeedback>();
    }
}
