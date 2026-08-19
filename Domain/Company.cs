namespace RealEstateManager.Domain
{
    public class Company
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string BaseCurrency { get; set; } = "AED";
        public int FiscalYearStartMonth { get; set; } = 1;
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
