using System.Diagnostics;
using System.Threading.Channels;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Fills a company with a realistic, fictional year of property business so a new
    /// user can learn the system on it: owned and managed buildings, tenants on monthly,
    /// quarterly, half-yearly and annual billing, receipts, cheques, deposits, supplier
    /// bills and payments, petty cash, owner drawings, landlord remittances, VAT
    /// settlements, prepayments, fixed assets with depreciation, a credit note, tenant
    /// requests, and closed periods - every report has something to show. Also property
    /// documents, development projects with a completion transfer, off-plan and completed
    /// unit sales with a handover, a contract awaiting approval, a building under the
    /// Capital Assets Scheme, and e-invoices sent and received.
    ///
    /// Everything is created through the same services the screens use, so each journal
    /// is genuine: the sample data obeys the same rules as data a user would enter.
    ///
    /// The data runs from the start of a fiscal year to today, always at least six
    /// months. It only goes into an empty company; one that already holds business
    /// records is refused, so it can never be mixed into real figures.
    /// </summary>
    public class SampleDataService
    {
        public const string TrainingSuffix = " - Training";

        private readonly IServiceScopeFactory _scopeFactory;

        public SampleDataService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>Why the company cannot take sample data, or null when it can.</summary>
        public async Task<string?> GetBlockReasonAsync(int companyId)
        {
            using var scope = CreateCompanyScope(companyId, null);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var found = new List<string>();
            if (await db.Properties.AnyAsync()) found.Add("properties");
            if (await db.Tenants.AnyAsync()) found.Add("tenants");
            if (await db.Vendors.AnyAsync()) found.Add("vendors");
            if (await db.Leases.AnyAsync()) found.Add("leases");
            if (await db.JournalEntries.AnyAsync()) found.Add("journal entries");

            return found.Count == 0
                ? null
                : $"This company already has {string.Join(", ", found)}. Sample data only goes into an empty company, " +
                  "so it can never be mixed with real records.";
        }

        public async Task<SampleDataResult> FillCompanyAsync(int companyId, int? actingUserId)
        {
            var reason = await GetBlockReasonAsync(companyId);
            if (reason != null)
                throw new InvalidOperationException(reason);

            using var scope = CreateCompanyScope(companyId, actingUserId);
            var builder = ActivatorUtilities.CreateInstance<SampleDataBuilder>(scope.ServiceProvider);
            return await builder.BuildAsync(companyId, actingUserId);
        }

        /// <summary>
        /// A fresh scope whose every query is filtered to the given company, whatever
        /// company the person behind the current request is signed in to.
        /// </summary>
        private IServiceScope CreateCompanyScope(int companyId, int? userId)
        {
            var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<CompanyContextOverride>();
            context.CompanyId = companyId;
            context.UserId = userId;
            return scope;
        }
    }

    /// <summary>
    /// Companies waiting for their sample data. Building a year of business takes from
    /// half a minute to a couple of minutes, far too long to keep a new user waiting on
    /// the sign-up page, so the work is queued and done in the background.
    /// </summary>
    public sealed class SampleDataQueue
    {
        private readonly Channel<(int CompanyId, int? UserId)> _channel =
            Channel.CreateUnbounded<(int CompanyId, int? UserId)>();

        public void Enqueue(int companyId, int? userId) => _channel.Writer.TryWrite((companyId, userId));

        public IAsyncEnumerable<(int CompanyId, int? UserId)> ReadAllAsync(CancellationToken cancellationToken) =>
            _channel.Reader.ReadAllAsync(cancellationToken);
    }

    public sealed class SampleDataWorker : BackgroundService
    {
        private readonly SampleDataQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SampleDataWorker> _logger;

        public SampleDataWorker(SampleDataQueue queue, IServiceScopeFactory scopeFactory, ILogger<SampleDataWorker> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var (companyId, userId) in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<SampleDataService>();
                    var result = await service.FillCompanyAsync(companyId, userId);

                    _logger.LogInformation("Sample data for company {CompanyId}: {Summary}", companyId, result.Summary);
                    foreach (var warning in result.Warnings)
                        _logger.LogWarning("Sample data for company {CompanyId}: {Warning}", companyId, warning);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sample data for company {CompanyId} could not be built.", companyId);
                }
            }
        }
    }

    public sealed class SampleDataResult
    {
        public int CompanyId { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int Leases { get; set; }
        public int PostedJournals { get; set; }
        public TimeSpan Elapsed { get; set; }
        public List<string> Warnings { get; } = new();

        public string Summary =>
            $"Sample data from {FromDate:dd MMM yyyy} to {ToDate:dd MMM yyyy}: {Leases} leases and " +
            $"{PostedJournals} posted journal entries, built in {Elapsed.TotalSeconds:N0} seconds." +
            (Warnings.Count > 0 ? $" {Warnings.Count} step(s) were skipped." : string.Empty);
    }

    /// <summary>
    /// Does the work for <see cref="SampleDataService"/>, inside a scope already set to the
    /// target company. One instance builds one company.
    /// </summary>
    internal sealed class SampleDataBuilder
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _posting;
        private readonly JournalWorkflowService _workflow;
        private readonly CodeGenerationService _codes;
        private readonly PdcService _pdc;
        private readonly VendorBillService _bills;
        private readonly PettyCashService _pettyCash;
        private readonly ManualJournalService _manualJournals;
        private readonly FixedAssetService _fixedAssets;
        private readonly CreditNoteService _creditNotes;
        private readonly OwnerSettlementService _ownerSettlements;
        private readonly OwnerCommissionInvoiceService _commissionInvoices;
        private readonly DevelopmentProjectService _developmentProjects;
        private readonly DevelopmentTransferService _developmentTransfers;
        private readonly InvestmentPropertySetupService _investmentPropertySetup;
        private readonly UnitSaleService _unitSales;
        private readonly CapitalAssetVatService _capitalAssets;
        private readonly LeaseWorkflowService _leaseWorkflow;
        private readonly EInvoiceService _eInvoices;
        private readonly List<int> _saleIds = new();
        private int _projectId;
        private int _annexProjectId;
        private int _annexSaleId;

        private readonly SampleDataResult _result = new();

        private int _companyId;
        private int? _userId;
        private DateTime _today;
        private DateTime _start;
        private Company _company = null!;
        private Dictionary<string, int> _acc = new();

        private int _receiptNo, _paymentNo, _depositNo, _chequeNo = 104_200;

        // Master data, kept for the monthly loop.
        private readonly List<SampleLease> _leases = new();
        private readonly Dictionary<string, Property> _properties = new();
        private readonly Dictionary<string, Vendor> _vendors = new();
        private readonly Dictionary<string, int> _tenantIds = new();
        private readonly HashSet<int> _pdcInvoiceIds = new();
        private readonly List<int> _ownerCategoryIds = new();

        public SampleDataBuilder(
            ApplicationDbContext db,
            AccountingPostingService posting,
            JournalWorkflowService workflow,
            CodeGenerationService codes,
            PdcService pdc,
            VendorBillService bills,
            PettyCashService pettyCash,
            ManualJournalService manualJournals,
            FixedAssetService fixedAssets,
            CreditNoteService creditNotes,
            OwnerSettlementService ownerSettlements,
            OwnerCommissionInvoiceService commissionInvoices,
            DevelopmentProjectService developmentProjects,
            DevelopmentTransferService developmentTransfers,
            InvestmentPropertySetupService investmentPropertySetup,
            UnitSaleService unitSales,
            CapitalAssetVatService capitalAssets,
            LeaseWorkflowService leaseWorkflow,
            EInvoiceService eInvoices)
        {
            _capitalAssets = capitalAssets;
            _leaseWorkflow = leaseWorkflow;
            _eInvoices = eInvoices;
            _db = db;
            _posting = posting;
            _workflow = workflow;
            _codes = codes;
            _pdc = pdc;
            _bills = bills;
            _pettyCash = pettyCash;
            _manualJournals = manualJournals;
            _fixedAssets = fixedAssets;
            _creditNotes = creditNotes;
            _ownerSettlements = ownerSettlements;
            _commissionInvoices = commissionInvoices;
            _developmentProjects = developmentProjects;
            _developmentTransfers = developmentTransfers;
            _investmentPropertySetup = investmentPropertySetup;
            _unitSales = unitSales;
        }

        public async Task<SampleDataResult> BuildAsync(int companyId, int? userId)
        {
            var clock = Stopwatch.StartNew();

            _companyId = companyId;
            _userId = userId;
            _today = DateTime.Today;
            _company = await _db.Companies.FirstAsync(x => x.Id == companyId);

            // Start on the first day of the fiscal year that contains the date six months
            // ago, so there are always at least six months and the year's reports are whole.
            var anchor = _today.AddMonths(-6);
            var fyStartMonth = _company.FiscalYearStartMonth is >= 1 and <= 12 ? _company.FiscalYearStartMonth : 1;
            var startYear = anchor.Month >= fyStartMonth ? anchor.Year : anchor.Year - 1;
            _start = new DateTime(startYear, fyStartMonth, 1);

            _result.CompanyId = companyId;
            _result.FromDate = _start;
            _result.ToDate = _today;

            await ChartOfAccountsSeeder.EnsureCompanySetupAsync(_db, _company);
            for (var year = startYear; new DateTime(year, fyStartMonth, 1) <= _today; year++)
                await ChartOfAccountsSeeder.EnsureFiscalYearPeriodsAsync(_db, _company, year);

            _acc = await _db.Accounts.ToDictionaryAsync(x => x.Code, x => x.Id);
            _ownerCategoryIds.AddRange(await _db.OwnerWithdrawalCategories
                .OrderBy(x => x.SortOrder).Select(x => x.Id).ToListAsync());

            await FillCompanyProfileAsync();
            await SetOpeningBalancesAsync();
            await CreateMasterDataAsync();
            await AddPropertyDocumentsAsync();
            await CreateLeasesAsync();
            await CreateDevelopmentProjectAsync();
            await SetUpBuildingDepreciationAsync();

            for (var month = _start; month <= _today; month = month.AddMonths(1))
            {
                await RunMonthAsync(month);
                _db.ChangeTracker.Clear();
            }

            // Anything still waiting - a PDC memorandum, a depreciation line - goes in now.
            await PostAllAsync(_start, _today);

            await ClosePastPeriodsAsync();
            await CreateServiceRequestsAsync();
            await LeaveWorkInDraftAsync();
            await RegisterCapitalAssetAsync();
            await SubmitContractForApprovalAsync();
            await StartEInvoicingAsync();
            await ReceiveSupplierEInvoicesAsync();

            _result.PostedJournals = await _db.JournalEntries.CountAsync(x => x.IsPosted);
            _result.Elapsed = clock.Elapsed;
            return _result;
        }

        // =====================================================================
        //  SET-UP
        // =====================================================================

        private async Task FillCompanyProfileAsync()
        {
            // Only blanks are filled: a company's own details are never overwritten.
            static string? Keep(string? current, string sample) => string.IsNullOrWhiteSpace(current) ? sample : current;

            _company.LegalName = Keep(_company.LegalName, _company.Name + " LLC");
            _company.TrnNumber = Keep(_company.TrnNumber, "100000000000003");
            _company.AddressLine1 = Keep(_company.AddressLine1, "Office 1104, Sample Tower");
            _company.AddressLine2 = Keep(_company.AddressLine2, "Al Hamra Street");
            _company.City = Keep(_company.City, "Ras Al Khaimah");
            _company.Country = Keep(_company.Country, "United Arab Emirates");
            _company.Phone = Keep(_company.Phone, "+971 7 200 0000");
            _company.Email = Keep(_company.Email, "accounts@training.example");
            _company.Emirate = Keep(_company.Emirate, "RAK");
            _company.CountryCode = Keep(_company.CountryCode, "AE");
            _company.TradeLicenseNumber = Keep(_company.TradeLicenseNumber, "RAK-00000");
            _company.LegalRegistrationType = Keep(_company.LegalRegistrationType, LegalRegistrationTypes.TradeLicence);
            _company.LicensingAuthority = Keep(_company.LicensingAuthority, "Department of Economic Development - Ras Al Khaimah");
            _company.BankAccountName = Keep(_company.BankAccountName, _company.LegalName ?? _company.Name);
            _company.BankIban = Keep(_company.BankIban, "AE070330000000000000000");

            // A mixed business recovers only part of its residual input tax.
            if (_company.ProvisionalVatRecoveryRate <= 0m)
                _company.ProvisionalVatRecoveryRate = 70m;

            await _db.SaveChangesAsync();
        }

        private async Task SetOpeningBalancesAsync()
        {
            // The balance sheet brought forward on day one. Assets equal equity, so the
            // opening balance control account nets to nil.
            var openings = new Dictionary<string, decimal>
            {
                ["11100"] = 1_850_000m,      // bank
                ["11150"] = 2_000m,          // petty cash float
                ["15100"] = 42_500_000m,     // the two owned buildings
                ["31100"] = 25_000_000m,     // share capital
                ["32100"] = 19_352_000m      // retained earnings
            };

            foreach (var (code, amount) in openings)
            {
                var account = await _db.Accounts.FirstOrDefaultAsync(x => x.Code == code);
                if (account != null && account.OpeningBalance == 0m)
                    account.OpeningBalance = amount;
            }

            await _db.SaveChangesAsync();
        }

        private async Task CreateMasterDataAsync()
        {
            async Task<Property> AddProperty(string key, string name, string type, string ownership, string vat,
                string address, decimal value, string? owner = null, decimal commission = 0m)
            {
                var property = new Property
                {
                    CompanyId = _companyId,
                    Code = await _codes.GeneratePropertyCodeAsync(ownership, type),
                    Name = name,
                    PropertyType = type,
                    OwnershipType = ownership,
                    VatCategory = vat,
                    Address = address,
                    AssetValue = value,
                    OwnerName = owner,
                    CommissionPercentage = commission,
                    IsActive = true
                };
                _db.Properties.Add(property);
                await _db.SaveChangesAsync();
                _properties[key] = property;
                return property;
            }

            var marina = await AddProperty("marina", "Marina Heights Residence", "Residential", "Owned", VatCategories.Exempt,
                "Al Marjan Island, Ras Al Khaimah", 18_500_000m);
            var naeem = await AddProperty("naeem", "Al Naeem Business Centre", "Commercial", "Owned", VatCategories.Standard,
                "Al Nakheel, Ras Al Khaimah", 24_000_000m);
            var corniche = await AddProperty("corniche", "Corniche Retail Plaza", "Commercial", "Managed", VatCategories.Standard,
                "Corniche Al Qawasim, Ras Al Khaimah", 0m, "Al Mansoori Holdings", 8m);
            var palm = await AddProperty("palm", "Palm View Villas", "Residential", "Managed", VatCategories.Exempt,
                "Mina Al Arab, Ras Al Khaimah", 0m, "Sheikha Investments LLC", 6m);

            // A VAT-registered landlord, so the commission tax invoice shows a recipient TRN.
            corniche.OwnerTrn = "100456789100003";

            // Registration details, as they appear on the title deeds and permits.
            void Register(Property property, string area, string plot, string deed, DateTime deedDate, string landUse,
                decimal plotArea, decimal builtUp, int floors, int parking, DateTime? completed, string insurer, DateTime insuranceExpiry)
            {
                property.Emirate = "Ras Al Khaimah";
                property.AreaName = area;
                property.PlotNumber = plot;
                property.MunicipalityNumber = "M-" + plot.Replace("/", "");
                property.TitleDeedNumber = deed;
                property.TitleDeedDate = deedDate;
                property.TitleDeedAuthority = "Ras Al Khaimah Land Department";
                property.TenureType = "Freehold";
                property.LandUse = landUse;
                property.PlotAreaSqFt = plotArea;
                property.OwnershipPercentage = 100m;
                property.BuiltUpAreaSqFt = builtUp;
                property.NumberOfFloors = floors;
                property.ParkingBays = parking;
                property.CompletionDate = completed;
                property.CompletionCertificateNumber = completed.HasValue ? "CC-" + completed.Value.Year + "-" + plot.Replace("/", "") : null;
                property.Insurer = insurer;
                property.InsurancePolicyNumber = "POL-" + plot.Replace("/", "");
                property.InsuranceExpiryDate = insuranceExpiry;
            }

            Register(marina, "Al Marjan Island", "214/7", "TD-2019-004417", new DateTime(2019, 3, 12), "Residential",
                42_000m, 118_000m, 12, 96, new DateTime(2019, 2, 20), "Al Ameen Insurance PJSC", _today.AddDays(45));
            Register(naeem, "Al Nakheel", "88/3", "TD-2017-001932", new DateTime(2017, 9, 4), "Commercial",
                36_500m, 152_000m, 15, 140, new DateTime(2017, 7, 30), "Al Ameen Insurance PJSC", _today.AddMonths(7));
            Register(corniche, "Corniche Al Qawasim", "12/41", "TD-2015-000876", new DateTime(2015, 5, 18), "Commercial",
                18_000m, 32_000m, 2, 40, new DateTime(2015, 4, 2), "Owner's insurer", _today.AddMonths(4));
            Register(palm, "Mina Al Arab", "301/2", "TD-2021-006120", new DateTime(2021, 11, 7), "Residential",
                24_000m, 9_800m, 2, 8, new DateTime(2021, 10, 15), "Owner's insurer", _today.AddMonths(10));
            marina.FairValue = 19_800_000m;
            marina.FairValueDate = new DateTime(_start.Year - 1, 12, 31);
            marina.ValuerName = "Gulf Property Valuers LLC";
            naeem.FairValue = 26_400_000m;
            naeem.FairValueDate = new DateTime(_start.Year - 1, 12, 31);
            naeem.ValuerName = "Gulf Property Valuers LLC";
            naeem.IsMortgaged = true;
            naeem.MortgageDetails = "Emirates Islamic - facility ref. EI-RAK-55102";

            await _db.SaveChangesAsync();

            // A plot being developed into flats for sale: the development project's building.
            var marjanBay = await AddProperty("marjanbay", "Marjan Bay Residences", "Residential", "Owned", VatCategories.Exempt,
                "Al Marjan Island, Ras Al Khaimah", 0m);
            marjanBay.Emirate = "Ras Al Khaimah";
            marjanBay.AreaName = "Al Marjan Island";
            marjanBay.PlotNumber = "219/1";
            marjanBay.TitleDeedNumber = "TD-" + _start.Year + "-007301";
            marjanBay.TitleDeedDate = _start.AddDays(14);
            marjanBay.TitleDeedAuthority = "Ras Al Khaimah Land Department";
            marjanBay.TenureType = "Freehold";
            marjanBay.LandUse = "Residential";
            marjanBay.PlotAreaSqFt = 28_000m;
            marjanBay.BuildingPermitNumber = "BP-" + _start.Year + "-0219";
            marjanBay.BuildingPermitDate = _start.AddMonths(1).AddDays(9);
            marjanBay.NumberOfFloors = 14;
            marjanBay.DeveloperName = _company.Name;
            marjanBay.MainContractor = "Al Hamra Contracting LLC";
            marjanBay.ConsultantName = "Gulf Design Consultants";
            await _db.SaveChangesAsync();

            // A four-shop retail block built on spare land beside the business centre, to rent out.
            var annex = await AddProperty("annex", "Al Naeem Retail Annex", "Commercial", "Owned", VatCategories.Standard,
                "Al Nakheel, Ras Al Khaimah", 0m);
            annex.Emirate = "Ras Al Khaimah";
            annex.AreaName = "Al Nakheel";
            annex.PlotNumber = "88/4";
            annex.TitleDeedNumber = "TD-2018-002210";
            annex.TitleDeedDate = new DateTime(2018, 4, 9);
            annex.TitleDeedAuthority = "Ras Al Khaimah Land Department";
            annex.TenureType = "Freehold";
            annex.LandUse = "Commercial";
            annex.PlotAreaSqFt = 6_500m;
            annex.BuildingPermitNumber = "BP-" + _start.Year + "-0088";
            annex.BuildingPermitDate = _start.AddDays(5);
            annex.NumberOfFloors = 1;
            annex.MainContractor = "Al Hamra Contracting LLC";
            annex.FairValue = 2_250_000m;
            annex.FairValueDate = _start.AddMonths(4).AddDays(-1);
            annex.ValuerName = "Gulf Property Valuers LLC";
            await _db.SaveChangesAsync();

            async Task AddUnit(Property property, string floor, string usage, decimal area, decimal rent, string status = "Vacant")
            {
                _db.Units.Add(new Unit
                {
                    PropertyId = property.Id,
                    UnitNumber = await _codes.GenerateUnitCodeAsync(property.Id),
                    Floor = floor,
                    UsageType = usage,
                    AreaSqFt = area,
                    StandardAnnualRent = rent,
                    Status = status,
                    IsActive = true
                });
                await _db.SaveChangesAsync();
            }

            await AddUnit(marina, "3", "Apartment 2BR", 1250, 78_000);
            await AddUnit(marina, "5", "Apartment 2BR", 1320, 84_000);
            await AddUnit(marina, "7", "Apartment 3BR", 1650, 96_000);
            await AddUnit(marina, "9", "Apartment 3BR", 1650, 98_000);
            await AddUnit(marina, "1", "Studio", 540, 52_000);
            await AddUnit(naeem, "2", "Office", 2100, 150_000);
            await AddUnit(naeem, "4", "Office", 2400, 190_000);
            await AddUnit(naeem, "6", "Office", 1200, 110_000, "UnderMaintenance");
            await AddUnit(naeem, "8", "Office", 1500, 125_000);
            await AddUnit(corniche, "G", "Retail", 1400, 120_000);
            await AddUnit(corniche, "G", "Clinic", 1100, 96_000);
            await AddUnit(palm, "-", "Villa 4BR", 3800, 140_000);
            await AddUnit(palm, "-", "Villa 4BR", 3800, 145_000);
            await AddUnit(marjanBay, "3", "Apartment 1BR", 820, 0);
            await AddUnit(marjanBay, "5", "Apartment 2BR", 1_240, 0);
            await AddUnit(marjanBay, "8", "Apartment 2BR", 1_260, 0);
            await AddUnit(marjanBay, "12", "Apartment 3BR", 1_680, 0);
            await AddUnit(annex, "G", "Retail", 950, 70_000);
            await AddUnit(annex, "G", "Retail", 950, 70_000);
            await AddUnit(annex, "G", "Retail", 1200, 85_000);
            await AddUnit(annex, "G", "Retail", 1400, 95_000);

            async Task AddTenant(string key, string name, string email, string phone, string nationality,
                string? eid = null, string? passport = null, string? licence = null, string? trn = null)
            {
                var tenant = new Tenant
                {
                    CompanyId = _companyId,
                    Code = await _codes.GenerateTenantCodeAsync(),
                    FullName = name,
                    Email = email,
                    Phone = phone,
                    Nationality = nationality,
                    IdNumber = eid,
                    PassportNumber = passport,
                    TradeLicenseNumber = licence,
                    TrnNumber = trn,
                    IsActive = true,
                    PartyType = licence != null ? PartyTypes.Business : PartyTypes.Individual,
                    LicensingAuthority = licence == null ? null : "Department of Economic Development - Ras Al Khaimah",
                    Address = licence != null ? "Al Muntasir Road, Al Nakheel" : null,
                    City = "Ras Al Khaimah",
                    Emirate = "RAK",
                    CountryCode = "AE"
                };
                _db.Tenants.Add(tenant);
                await _db.SaveChangesAsync();
                _tenantIds[key] = tenant.Id;
            }

            await AddTenant("omar", "Omar Al Hashimi", "omar@mail.example", "+971 50 000 1001", "UAE", eid: "784-1985-0001001-1");
            await AddTenant("priya", "Priya Raman", "priya@mail.example", "+971 55 000 1002", "India", eid: "784-1990-0001002-2", passport: "Z0001002");
            await AddTenant("northwind", "Northwind Trading LLC", "accounts@northwind.example", "+971 7 200 1003", "UAE", licence: "RAK-10003", trn: "100000000100003");
            await AddTenant("falcon", "Falcon Logistics LLC", "finance@falcon.example", "+971 7 200 1004", "UAE", licence: "RAK-10004", trn: "100000000200003");
            await AddTenant("sara", "Sara Mitchell", "sara@mail.example", "+971 52 000 1005", "United Kingdom", passport: "500001005");
            await AddTenant("desertbloom", "Desert Bloom Cafe LLC", "hello@desertbloom.example", "+971 7 200 1006", "UAE", licence: "RAK-10006", trn: "100000000300003");
            await AddTenant("khalid", "Khalid Al Suwaidi", "khalid@mail.example", "+971 56 000 1007", "UAE", eid: "784-1979-0001007-7");
            await AddTenant("brightsmile", "Bright Smile Dental Clinic", "admin@brightsmile.example", "+971 7 200 1008", "UAE", licence: "RAK-10008", trn: "100000000400003");
            await AddTenant("gulfpearl", "Gulf Pearl Pharmacy LLC", "office@gulfpearl.example", "+971 7 200 1009", "UAE", licence: "RAK-10009", trn: "100000000500003");
            await AddTenant("ahmed", "Ahmed Rashid", "ahmed@mail.example", "+971 50 000 1010", "Jordan", passport: "P0001010");
            // The retail annex's tenants, the shop's buyer, and a tenant whose contract awaits approval.
            await AddTenant("coastal", "Coastal Optics LLC", "accounts@coastaloptics.example", "+971 7 200 1011", "UAE", licence: "RAK-10011", trn: "100000001200003");
            await AddTenant("saffron", "Saffron Bakery LLC", "orders@saffronbakery.example", "+971 7 200 1012", "UAE", licence: "RAK-10012", trn: "100000001300003");
            await AddTenant("horizon", "Horizon Investments LLC", "invest@horizon.example", "+971 7 200 1013", "UAE", licence: "RAK-10013", trn: "100000001400003");
            await AddTenant("reem", "Al Reem Mobile Accessories LLC", "shop@alreem.example", "+971 7 200 1014", "UAE", licence: "RAK-10014", trn: "100000001500003");

            async Task AddVendor(string key, string name, string service, string contact, bool vatRegistered, string? trn)
            {
                var vendor = new Vendor
                {
                    CompanyId = _companyId,
                    Code = await _codes.GenerateVendorCodeAsync(),
                    Name = name,
                    ServiceType = service,
                    ContactPerson = contact,
                    Phone = "+971 7 200 2000",
                    Email = $"billing@{key}.example",
                    TrnNumber = trn,
                    TradeLicenseNumber = "RAK-2" + _vendors.Count.ToString("0000"),
                    IsVatRegistered = vatRegistered,
                    IsActive = true
                };
                _db.Vendors.Add(vendor);
                await _db.SaveChangesAsync();
                _vendors[key] = vendor;
            }

            await AddVendor("facility", "Emirates Facility Services LLC", "Cleaning & Security", "Anil Kumar", true, "100000000600003");
            await AddVendor("cooltech", "CoolTech Air Conditioning", "HVAC Maintenance", "Yousef Darwish", true, "100000000700003");
            await AddVendor("contractor", "Al Hamra Contracting LLC", "Main Contractor", "Imran Siddiqui", true, "100000001000003");
            await AddVendor("consultant", "Gulf Design Consultants", "Architecture & Supervision", "Layla Haddad", true, "100000001100003");
            await AddVendor("handyman", "Rashid Handyman Services", "General Repairs", "Rashid Khan", false, null);
            await AddVendor("utilities", "Northern Utilities Company", "Water & Electricity", "Customer Service", true, "100000000800003");
            await AddVendor("insurance", "Al Ameen Insurance PJSC", "Property Insurance", "Mona Saeed", true, "100000000900003");
        }

        private async Task CreateLeasesAsync()
        {
            var units = await _db.Units.Include(x => x.Property).OrderBy(x => x.Id).ToListAsync();

            Unit UnitOf(string property, int index) =>
                units.Where(x => x.PropertyId == _properties[property].Id).OrderBy(x => x.Id).ElementAt(index);

            // unit, tenant, months after the start, billing frequency, how the tenant pays
            var plan = new (Unit Unit, string Tenant, int Offset, int Frequency, decimal Deposit, string Behaviour)[]
            {
                (UnitOf("marina", 0), "omar", 0, 3, 3_900, "OnTime"),
                (UnitOf("marina", 1), "priya", 2, 3, 4_200, "PartLatest"),
                (UnitOf("naeem", 0), "northwind", 0, 3, 15_000, "Pdc"),
                (UnitOf("naeem", 1), "falcon", 1, 6, 19_000, "PdcSecondHalf"),
                (UnitOf("corniche", 1), "brightsmile", 3, 3, 9_600, "Pdc"),
                (UnitOf("palm", 0), "sara", 0, 6, 7_000, "OnTime"),
                (UnitOf("corniche", 0), "desertbloom", 0, 3, 12_000, "LatestUnpaid"),
                (UnitOf("marina", 2), "khalid", 4, 1, 4_800, "Arrears"),
                (UnitOf("naeem", 3), "gulfpearl", 0, 12, 12_500, "OnTime"),
                (UnitOf("palm", 1), "ahmed", 1, 1, 7_250, "OnTime")
            };

            foreach (var (unit, tenantKey, offset, frequency, deposit, behaviour) in plan)
            {
                var startDate = _start.AddMonths(offset);
                if (startDate <= _today)
                    await CreateLeaseAsync(unit, tenantKey, startDate, frequency, deposit, behaviour);
            }

            _result.Leases = _leases.Count;
        }

        private async Task CreateLeaseAsync(Unit unit, string tenantKey, DateTime startDate, int frequency, decimal deposit, string behaviour)
        {
            var tenantId = _tenantIds[tenantKey];
            var category = VatDeterminationService.ResolveCategory(unit.Property!, unit);

            var lease = new Lease
            {
                CompanyId = _companyId,
                UnitId = unit.Id,
                TenantId = tenantId,
                ContractNumber = await _codes.GenerateContractCodeAsync(unit.Id, tenantId, startDate),
                StartDate = startDate,
                EndDate = startDate.AddYears(1).AddDays(-1),
                AnnualRent = unit.StandardAnnualRent,
                BillingFrequencyMonths = frequency,
                SecurityDepositAmount = deposit,
                Status = "Active",
                Notes = "Sample lease",
                CreatedAt = DateTime.UtcNow
            };

            VatDeterminationService.ApplyToLease(lease, category, null);

            _db.Leases.Add(lease);
            await _db.SaveChangesAsync();

            unit.Status = "Occupied";
            LeaseScheduleBuilder.GenerateInvoices(_db, _companyId, lease);
            LeaseScheduleBuilder.GenerateRevenueRecognitionSchedules(_db, _companyId, lease, unit.Property!);
            await _db.SaveChangesAsync();

            _leases.Add(new SampleLease(lease.Id, tenantId, unit.Property!.Id, unit.Property.OwnershipType,
                unit.Property.CommissionPercentage, startDate, behaviour, deposit));
        }

        /// <summary>
        /// The month after the annex is transferred, two of its shops are let. Called before
        /// the month's deposits and invoices, so their first invoices go through that month.
        /// </summary>
        private async Task LeaseAnnexShopsAsync(DateTime monthStart)
        {
            await TryAsync("let the annex shops", async () =>
            {
                var shops = await _db.Units.Include(x => x.Property)
                    .Where(x => x.PropertyId == _properties["annex"].Id)
                    .OrderBy(x => x.Id).ToListAsync();

                await CreateLeaseAsync(shops[0], "coastal", monthStart, 3, 17_500m, "OnTime");
                await CreateLeaseAsync(shops[1], "saffron", monthStart, 1, 5_850m, "OnTime");
                _result.Leases = _leases.Count;
            });
        }

        // =====================================================================
        //  ONE MONTH
        // =====================================================================

        private async Task RunMonthAsync(DateTime monthStart)
        {
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var lastDay = monthEnd < _today ? monthEnd : _today;
            var index = (monthStart.Year - _start.Year) * 12 + monthStart.Month - _start.Month;

            if (index == 4)
                await LeaseAnnexShopsAsync(monthStart);

            await DepositsAndChequesAsync(monthStart, lastDay);
            await PostInvoicesAsync(monthStart, lastDay);

            if (index == 3)
                await RaiseCreditNoteAsync(monthStart);

            await ClearChequesAsync(monthStart, lastDay);
            await ReceiveRentAsync(monthStart, lastDay);
            await PettyCashAsync(monthStart, lastDay);
            await OneOffEventsAsync(index, monthStart, lastDay);

            if (monthEnd <= _today)
            {
                await SupplierBillsAsync(index, monthStart, monthEnd);
                await DevelopmentCostsAsync(index, monthStart, monthEnd);
                await UnitSalesAsync(index, monthStart, monthEnd);
                await MonthEndJournalsAsync(index, monthStart, monthEnd);

                await _posting.ProcessRevenueRecognitionUpToAsync(monthEnd);
                await _posting.ProcessPrepaymentRecognitionsUpToAsync(monthEnd);
                await _fixedAssets.PostDepreciationUpToAsync(monthEnd);
            }

            await PaySupplierBillsAsync(monthStart, lastDay);
            await PostAllAsync(monthStart, lastDay);

            // Remittances and VAT read posted balances, so they come after the month is in.
            if (monthEnd <= _today)
            {
                await IssueCommissionInvoicesAsync(monthEnd);
                await RemitToLandlordsAsync(monthStart, monthEnd);
                await PostAllAsync(monthStart, monthEnd);
            }

            await SettleVatAsync(monthStart, lastDay);
        }

        private async Task PostInvoicesAsync(DateTime from, DateTime to)
        {
            var ids = await _db.CustomerInvoices
                .Where(x => x.JournalEntryId == null && x.InvoiceDate >= from && x.InvoiceDate <= to)
                .OrderBy(x => x.InvoiceDate).ThenBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var id in ids)
            {
                await TryAsync($"post invoice {id}", async () =>
                {
                    var journal = await _posting.PostCustomerInvoiceAsync(id);
                    await PostJournalAsync(journal.Id);
                });
            }
        }

        private async Task RaiseCreditNoteAsync(DateTime monthStart)
        {
            // Road works outside Corniche Retail Plaza: the cafe is given a rent concession.
            var lease = _leases.FirstOrDefault(x => x.Behaviour == "LatestUnpaid");
            if (lease == null) return;

            var invoice = await _db.CustomerInvoices.AsNoTracking()
                .Where(x => x.LeaseId == lease.LeaseId && x.JournalEntryId != null && x.InvoiceDate >= monthStart)
                .OrderBy(x => x.InvoiceDate)
                .FirstOrDefaultAsync();
            if (invoice == null) return;

            await TryAsync("raise credit note", async () =>
            {
                var modelState = new ModelStateDictionary();
                var note = await _creditNotes.SaveAsync(modelState, _companyId, _userId, new CreditNoteEditViewModel
                {
                    CustomerInvoiceId = invoice.Id,
                    CreditNoteDate = invoice.InvoiceDate.AddDays(3),
                    ReasonCode = CreditNoteReasons.ConsiderationAltered,
                    Reason = "Agreed rent concession while road works blocked the shop front.",
                    NetAmount = 3_000m
                });

                if (note == null)
                    throw new InvalidOperationException(Describe(modelState));

                var journal = await _posting.PostCustomerCreditNoteAsync(note.Id);
                await PostJournalAsync(journal.Id);
            });
        }

        private async Task ReceiveRentAsync(DateTime from, DateTime to)
        {
            var invoices = await _db.CustomerInvoices.AsNoTracking()
                .Where(x => x.JournalEntryId != null && x.InvoiceDate >= from && x.InvoiceDate <= to)
                .OrderBy(x => x.InvoiceDate).ThenBy(x => x.Id)
                .ToListAsync();

            foreach (var invoice in invoices)
            {
                if (_pdcInvoiceIds.Contains(invoice.Id))
                    continue;

                var lease = _leases.FirstOrDefault(x => x.LeaseId == invoice.LeaseId);
                if (lease == null) continue;

                var outstanding = invoice.TotalAmount - invoice.AmountPaid;
                if (outstanding <= 0m) continue;

                var isRecent = invoice.InvoiceDate > _today.AddMonths(-2);
                var amount = lease.Behaviour switch
                {
                    "Arrears" when isRecent => 0m,
                    "LatestUnpaid" when invoice.InvoiceDate > _today.AddMonths(-3) => 0m,
                    "PartLatest" when invoice.InvoiceDate > _today.AddMonths(-3) => Math.Round(outstanding / 2m, 2),
                    _ => outstanding
                };
                if (amount <= 0m) continue;

                var payDay = invoice.InvoiceDate.AddDays(lease.TenantId % 2 == 0 ? 5 : 11);
                if (payDay > _today) continue;

                var method = lease.Behaviour == "Arrears" ? "Cash"
                    : lease.Ownership == "Managed" || lease.TenantId % 3 == 0 ? "Bank Transfer" : "Cheque";

                await TryAsync($"receipt for {invoice.InvoiceNumber}", async () =>
                {
                    var receipt = new CustomerReceipt
                    {
                        CompanyId = _companyId,
                        CustomerInvoiceId = invoice.Id,
                        ReceiptNumber = $"RCT-{payDay:yyyy}-{++_receiptNo:0000}",
                        ReceiptDate = payDay,
                        Amount = amount,
                        PaymentMethod = method
                    };
                    _db.CustomerReceipts.Add(receipt);
                    await _db.SaveChangesAsync();

                    var journal = await _posting.PostCustomerReceiptAsync(receipt.Id);
                    await PostJournalAsync(journal.Id);
                });
            }
        }

        private async Task DepositsAndChequesAsync(DateTime from, DateTime to)
        {
            foreach (var lease in _leases.Where(x => x.StartDate >= from && x.StartDate <= to))
            {
                await TryAsync("security deposit", async () =>
                {
                    var deposit = new SecurityDeposit
                    {
                        CompanyId = _companyId,
                        LeaseId = lease.LeaseId,
                        ReferenceNumber = $"SD-{lease.StartDate:yyyy}-{++_depositNo:000}",
                        ReceiptDate = lease.StartDate,
                        Amount = lease.Deposit,
                        PaymentMethod = "Cheque",
                        Status = "Held"
                    };
                    _db.SecurityDeposits.Add(deposit);
                    await _db.SaveChangesAsync();

                    var journal = await _posting.PostSecurityDepositAsync(deposit.Id);
                    await PostJournalAsync(journal.Id);
                });

                // Post-dated cheques handed over with the contract: every instalment after
                // the first for Northwind and the clinic, the second half-year for Falcon.
                // Cheques dated after today stay in hand as noted items.
                var chequeInvoices = lease.Behaviour switch
                {
                    "Pdc" => await _db.CustomerInvoices.AsNoTracking()
                        .Where(x => x.LeaseId == lease.LeaseId && x.InvoiceDate > lease.StartDate)
                        .OrderBy(x => x.InvoiceDate).ToListAsync(),
                    "PdcSecondHalf" => await _db.CustomerInvoices.AsNoTracking()
                        .Where(x => x.LeaseId == lease.LeaseId && x.InvoiceDate > lease.StartDate)
                        .OrderBy(x => x.InvoiceDate).Take(1).ToListAsync(),
                    _ => new List<CustomerInvoice>()
                };

                foreach (var invoice in chequeInvoices)
                {
                    await TryAsync($"PDC for {invoice.InvoiceNumber}", async () =>
                    {
                        var cheque = new PdcReceipt
                        {
                            CompanyId = _companyId,
                            LeaseId = lease.LeaseId,
                            TenantId = lease.TenantId,
                            CustomerInvoiceId = invoice.Id,
                            ChequeNumber = (++_chequeNo).ToString("000000"),
                            BankName = lease.Behaviour == "Pdc" ? "Emirates NBD" : "RAKBANK",
                            ChequeDate = invoice.InvoiceDate,
                            ReceivedDate = lease.StartDate,
                            Amount = invoice.TotalAmount,
                            SettlementMode = "Receivable",
                            Status = "Draft"
                        };
                        _db.PdcReceipts.Add(cheque);
                        await _db.SaveChangesAsync();

                        var journal = await _pdc.ReceivePdcAsync(cheque.Id);
                        await PostJournalAsync(journal.Id);
                        _pdcInvoiceIds.Add(invoice.Id);
                    });
                }
            }
        }

        private async Task ClearChequesAsync(DateTime from, DateTime to)
        {
            var due = await _db.PdcReceipts.AsNoTracking()
                .Where(x => x.Status == "Received" && x.ChequeDate >= from && x.ChequeDate <= to)
                .ToListAsync();

            foreach (var cheque in due)
            {
                await TryAsync($"clear cheque {cheque.ChequeNumber}", async () =>
                {
                    var journal = await _pdc.ClearPdcAsync(cheque.Id, cheque.ChequeDate);
                    await PostJournalAsync(journal.Id);
                });
            }
        }

        // =====================================================================
        //  SUPPLIERS
        // =====================================================================

        private async Task SupplierBillsAsync(int index, DateTime monthStart, DateTime monthEnd)
        {
            var marina = _properties["marina"].Id;
            var naeem = _properties["naeem"].Id;
            var corniche = _properties["corniche"].Id;
            var month = monthStart.ToString("MMMM yyyy");

            await BillAsync("facility", $"EFS-{monthStart:yyMM}", monthEnd, $"Cleaning & security - {month}", null,
                ("Common-area cleaning - Marina Heights", "51100", marina, 1, 2_500m, true, InputTaxAttributions.Exempt),
                ("Office cleaning - Al Naeem Business Centre", "51100", naeem, 1, 3_800m, true, InputTaxAttributions.Taxable),
                ("Security guards - Corniche Retail Plaza", "51100", corniche, 2, 1_400m, true, InputTaxAttributions.Taxable),
                ("Head office cleaning", "61100", null, 1, 900m, true, InputTaxAttributions.Residual));

            await BillAsync("utilities", $"NUC-{monthStart:yyMM}", monthEnd, $"Water & electricity - {month}", null,
                ("Common areas - Marina Heights", "51100", marina, 1, 2_350m + index * 40m, true, InputTaxAttributions.Exempt),
                ("Common areas - Al Naeem Business Centre", "51100", naeem, 1, 3_100m + index * 55m, true, InputTaxAttributions.Taxable));

            if (index % 3 == 2)
            {
                await BillAsync("cooltech", $"CT-{monthStart:yyMM}", monthEnd, "Quarterly AC maintenance", naeem,
                    ("Quarterly AC service - all floors", "52100", naeem, 1, 8_750m, true, InputTaxAttributions.Taxable),
                    ("Replacement filters", "52100", naeem, 12, 85m, true, InputTaxAttributions.Taxable));
            }

            if (index % 2 == 1)
            {
                await BillAsync("handyman", $"RH-{monthStart:yyMM}", monthEnd.AddDays(-5), "Minor repairs", marina,
                    ("Door lock and hinge repairs", "52100", marina, 1, 650m, false, InputTaxAttributions.Exempt),
                    ("Paint touch-up - corridors", "52100", marina, 1, 1_200m, false, InputTaxAttributions.Exempt));
            }
        }

        // =====================================================================
        //  DEVELOPMENT PROJECT
        // =====================================================================

        private async Task CreateDevelopmentProjectAsync()
        {
            await TryAsync("create development project", async () =>
            {
                var budget = new Dictionary<string, decimal>
                {
                    [DevelopmentCostCategories.Land] = 6_120_000m,
                    [DevelopmentCostCategories.Design] = 900_000m,
                    [DevelopmentCostCategories.Permits] = 250_000m,
                    [DevelopmentCostCategories.Construction] = 18_000_000m,
                    [DevelopmentCostCategories.Infrastructure] = 1_200_000m,
                    [DevelopmentCostCategories.Supervision] = 600_000m,
                    [DevelopmentCostCategories.Other] = 300_000m
                };

                _annexProjectId = await _developmentProjects.SaveAsync(new DevelopmentProjectFormViewModel
                {
                    Name = "Al Naeem Retail Annex",
                    PropertyId = _properties["annex"].Id,
                    IntendedUse = DevelopmentIntendedUses.Rental,
                    WipAccountId = _acc["15500"],
                    StartDate = _start.AddDays(5),
                    ExpectedCompletionDate = _start.AddMonths(3).AddDays(14),
                    MainContractorVendorId = _vendors["contractor"].Id,
                    Notes = "Four ground-floor shops built to rent out.",
                    Budget = DevelopmentCostCategories.All
                        .Select(c => new DevelopmentBudgetInput
                        {
                            Category = c,
                            Amount = c switch
                            {
                                DevelopmentCostCategories.Land => 450_000m,
                                DevelopmentCostCategories.Design => 40_000m,
                                DevelopmentCostCategories.Permits => 15_000m,
                                DevelopmentCostCategories.Construction => 760_000m,
                                _ => 0m
                            }
                        })
                        .ToList()
                });

                _projectId = await _developmentProjects.SaveAsync(new DevelopmentProjectFormViewModel
                {
                    Name = "Marjan Bay Residences",
                    PropertyId = _properties["marjanbay"].Id,
                    IntendedUse = DevelopmentIntendedUses.Sale,
                    WipAccountId = _acc["13100"],
                    StartDate = _start.AddDays(14),
                    ExpectedCompletionDate = _start.AddMonths(30).AddDays(-1),
                    MainContractorVendorId = _vendors["contractor"].Id,
                    Notes = "14-storey residential tower, 112 apartments, built for sale.",
                    Budget = DevelopmentCostCategories.All
                        .Select(c => new DevelopmentBudgetInput { Category = c, Amount = budget.GetValueOrDefault(c) })
                        .ToList()
                });
            });
        }

        private async Task DevelopmentCostsAsync(int index, DateTime monthStart, DateTime monthEnd)
        {
            await AnnexAsync(index, monthStart, monthEnd);

            if (_projectId == 0)
                return;

            async Task Direct(DateTime date, string category, string text, decimal amount, string creditCode, string reference)
            {
                await TryAsync($"development cost {reference}", async () =>
                {
                    var warning = await _developmentProjects.RecordCostAsync(_projectId, date, category, text, amount, _acc[creditCode], reference);
                    if (warning != null)
                        throw new InvalidOperationException(warning);
                });
            }

            if (index == 0)
            {
                // The plot was contributed by the shareholders, and its transfer fee paid from the bank.
                await Direct(monthStart.AddDays(14), DevelopmentCostCategories.Land,
                    "Plot 219/1 Al Marjan Island - contributed in kind by the shareholders", 6_000_000m, "31100", "TD-PLOT-219");
                await Direct(monthStart.AddDays(20), DevelopmentCostCategories.Land,
                    "Land transfer registration fee", 120_000m, "11100", "LD-RCPT-4471");
            }

            if (index == 1)
                await Direct(monthStart.AddDays(9), DevelopmentCostCategories.Permits, "Building permit fee", 85_000m, "11100", "BP-FEE-0219");

            if (index == 5)
                await Direct(monthStart.AddDays(12), DevelopmentCostCategories.Infrastructure,
                    "Electricity and water connection charges", 95_000m, "11100", "UTIL-CONN-219");

            if (index <= 3)
            {
                await ProjectBillAsync("consultant", $"GDC-{monthStart:yyMM}", monthEnd.AddDays(-2), "Design and consultancy",
                    ("Architectural and structural design - stage " + (index + 1), DevelopmentCostCategories.Design, 75_000m));
            }

            if (index >= 2)
            {
                await ProjectBillAsync("contractor", $"AHC-IPC-{index - 1:00}", monthEnd.AddDays(-3), "Construction progress",
                    ($"Interim payment certificate {index - 1} - substructure and frame", DevelopmentCostCategories.Construction, 140_000m + index * 10_000m));
            }

            if (index >= 3)
            {
                await ProjectBillAsync("consultant", $"GDC-SUP-{monthStart:yyMM}", monthEnd.AddDays(-1), "Site supervision",
                    ($"Site supervision - {monthStart:MMMM yyyy}", DevelopmentCostCategories.Supervision, 18_000m));
            }
        }

        /// <summary>
        /// The annex: built in three months, completed, and its cost transferred to investment
        /// property in month four - land kept apart, the building depreciated from then on.
        /// </summary>
        private async Task AnnexAsync(int index, DateTime monthStart, DateTime monthEnd)
        {
            if (_annexProjectId == 0 || index > 3)
                return;

            async Task Direct(DateTime date, string category, string text, decimal amount, string creditCode, string reference)
            {
                await TryAsync($"annex cost {reference}", async () =>
                {
                    var warning = await _developmentProjects.RecordCostAsync(_annexProjectId, date, category, text, amount, _acc[creditCode], reference);
                    if (warning != null)
                        throw new InvalidOperationException(warning);
                });
            }

            if (index == 0)
            {
                await Direct(monthStart.AddDays(5), DevelopmentCostCategories.Land,
                    "Plot 88/4 Al Nakheel - moved from the business centre's land", 450_000m, "31100", "TD-2018-002210");
                await Direct(monthStart.AddDays(6), DevelopmentCostCategories.Permits, "Building permit fee", 15_000m, "11100", "BP-FEE-0088");
                await ProjectBillAsync("consultant", $"GDC-ANX-{monthStart:yyMM}", monthEnd.AddDays(-4), "Annex design", _annexProjectId, "annex",
                    ("Annex design and tender drawings", DevelopmentCostCategories.Design, 40_000m));
            }

            if (index <= 2)
            {
                await ProjectBillAsync("contractor", $"AHC-ANX-{index + 1:00}", monthEnd.AddDays(-6), "Annex construction", _annexProjectId, "annex",
                    ($"Annex construction - certificate {index + 1}", DevelopmentCostCategories.Construction, 250_000m));
            }

            if (index == 3)
            {
                await TryAsync("complete and transfer the annex", async () =>
                {
                    await _developmentProjects.CompleteAsync(_annexProjectId, monthStart.AddDays(4), "CC-" + monthStart.Year + "-0088");

                    // Three shops are kept to rent out; the largest is sold to an investor, so its
                    // share of the cost goes to completed properties held for sale.
                    var units = await _developmentTransfers.GetUnitsAsync(_annexProjectId);
                    var forSale = units.OrderBy(x => x.Id).Last().Id;
                    await _developmentTransfers.TransferAsync(_annexProjectId, monthStart.AddDays(9), 30,
                        units.ToDictionary(x => x.Id, x => x.Id == forSale ? DevelopmentIntendedUses.Sale : DevelopmentIntendedUses.Rental),
                        "Sample data");
                });
            }
        }

        /// <summary>
        /// The business centre came in as an opening balance; it is brought under the cost model
        /// (land not depreciated, building over its remaining life). Marina Heights is left for
        /// the user to set up as an exercise.
        /// </summary>
        private async Task SetUpBuildingDepreciationAsync()
        {
            await TryAsync("depreciate the business centre", async () =>
            {
                await _investmentPropertySetup.SetUpAsync(_properties["naeem"].Id, _start, 9_000_000m, 15_000_000m, 45, true);
            });
        }

        /// <summary>
        /// Off-plan sales in the tower: each first sale of a new flat is zero rated. The buyers pay
        /// 20% on signing, then by the plan; payments are advances until handover.
        /// </summary>
        private async Task UnitSalesAsync(int index, DateTime monthStart, DateTime monthEnd)
        {
            async Task Sell(int unitOffset, string buyerKey, decimal price)
            {
                await TryAsync($"sell tower unit {unitOffset + 1}", async () =>
                {
                    var units = await _db.Units.AsNoTracking()
                        .Where(x => x.PropertyId == _properties["marjanbay"].Id)
                        .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();
                    var date = monthStart.AddDays(9);
                    var total = price; // zero rated: no VAT
                    var down = Math.Round(total * 0.2m, 2);
                    var mid = Math.Round(total * 0.3m, 2);
                    var sale = await _unitSales.CreateAsync(units[unitOffset], _tenantIds[buyerKey], date, price,
                        new List<(string, DateTime, decimal)>
                        {
                            ("Down payment on signing", date, down),
                            ("Second instalment", date.AddMonths(3), mid),
                            ("Third instalment", date.AddMonths(12), mid),
                            ("On handover", date.AddMonths(24), total - down - mid - mid)
                        },
                        "Off-plan sale", "Sample data");
                    _saleIds.Add(sale.Id);
                });
            }

            if (index == 2) await Sell(0, "khalid", 780_000m);
            if (index == 5) await Sell(2, "ahmed", 1_150_000m);

            // A completed shop in the annex: commercial, so standard rated at 5%. Half is paid on
            // signing - VAT is due on that advance - and the rest next month, when it is handed over.
            if (index == 4)
            {
                await TryAsync("sell the annex shop", async () =>
                {
                    var shop = await _db.Units.AsNoTracking()
                        .Where(x => x.PropertyId == _properties["annex"].Id)
                        .OrderBy(x => x.Id).Select(x => x.Id).LastAsync();
                    var date = monthStart.AddDays(11);
                    const decimal price = 1_150_000m;
                    var total = price * 1.05m;
                    var sale = await _unitSales.CreateAsync(shop, _tenantIds["horizon"], date, price,
                        new List<(string, DateTime, decimal)>
                        {
                            ("Deposit on signing", date, total / 2m),
                            ("Balance on handover", date.AddMonths(1), total / 2m)
                        },
                        "Completed shop sold to an investor", "Sample data");
                    _saleIds.Add(sale.Id);
                    _annexSaleId = sale.Id;
                });
            }

            // Each instalment that falls due in the month is paid on its due date.
            foreach (var saleId in _saleIds)
            {
                await TryAsync($"payments on sale {saleId}", async () =>
                {
                    var sale = await _unitSales.GetAsync(saleId);
                    if (sale == null) return;
                    foreach (var step in sale.Instalments.Where(x => x.DueDate >= monthStart && x.DueDate <= monthEnd && x.DueDate <= _today).OrderBy(x => x.Sequence))
                        await _unitSales.RecordReceiptAsync(saleId, step.DueDate, step.Amount, "Bank Transfer", _acc["11100"], $"TT-{step.DueDate:yyMMdd}-{saleId}");
                });
            }

            if (index == 5 && _annexSaleId != 0)
            {
                await TryAsync("hand over the annex shop", async () =>
                {
                    var sale = await _unitSales.GetAsync(_annexSaleId);
                    var lastPayment = sale?.Receipts.Max(x => (DateTime?)x.ReceiptDate);
                    if (lastPayment != null)
                        await _unitSales.HandoverAsync(_annexSaleId, lastPayment.Value, "TD-" + lastPayment.Value.Year + "-008841");
                });
            }
        }

        private Task ProjectBillAsync(string vendorKey, string reference, DateTime date, string category,
            params (string Text, string CostCategory, decimal Amount)[] lines) =>
            ProjectBillAsync(vendorKey, reference, date, category, _projectId, "marjanbay", lines);

        private async Task ProjectBillAsync(string vendorKey, string reference, DateTime date, string category,
            int projectId, string propertyKey, params (string Text, string CostCategory, decimal Amount)[] lines)
        {
            await TryAsync($"bill {reference}", async () =>
            {
                var modelState = new ModelStateDictionary();
                var bill = await _bills.SaveAsync(modelState, _companyId, new VendorBillEditViewModel
                {
                    VendorId = _vendors[vendorKey].Id,
                    VendorReference = reference,
                    BillDate = date,
                    AccountingDate = date,
                    DueDate = date.AddDays(30),
                    PropertyId = _properties[propertyKey].Id,
                    ExpenseCategory = category,
                    Lines = lines.Select(x => new VendorBillLineViewModel
                    {
                        Description = x.Text,
                        AccountId = _acc["13100"],
                        Quantity = 1m,
                        UnitPrice = x.Amount,
                        IsTaxable = true,
                        // The first sale of each new flat will be zero rated, so the VAT is recoverable.
                        InputTaxAttribution = InputTaxAttributions.Taxable,
                        DevelopmentCost = $"{projectId}|{x.CostCategory}"
                    }).ToList()
                });

                if (bill == null)
                    throw new InvalidOperationException(Describe(modelState));

                var journal = await _posting.PostVendorBillAsync(bill.Id);
                await PostJournalAsync(journal.Id);
            });
        }

        private async Task BillAsync(string vendorKey, string reference, DateTime date, string category, int? propertyId,
            params (string Text, string Account, int? Property, decimal Qty, decimal Price, bool Taxable, string Attribution)[] lines)
        {
            await TryAsync($"bill {reference}", async () =>
            {
                var modelState = new ModelStateDictionary();
                var bill = await _bills.SaveAsync(modelState, _companyId, new VendorBillEditViewModel
                {
                    VendorId = _vendors[vendorKey].Id,
                    VendorReference = reference,
                    BillDate = date,
                    AccountingDate = date,
                    DueDate = date.AddDays(30),
                    PropertyId = propertyId,
                    ExpenseCategory = category,
                    Lines = lines.Select(x => new VendorBillLineViewModel
                    {
                        Description = x.Text,
                        AccountId = _acc[x.Account],
                        PropertyId = x.Property,
                        Quantity = x.Qty,
                        UnitPrice = x.Price,
                        IsTaxable = x.Taxable,
                        InputTaxAttribution = x.Attribution
                    }).ToList()
                });

                if (bill == null)
                    throw new InvalidOperationException(Describe(modelState));

                var journal = await _posting.PostVendorBillAsync(bill.Id);
                await PostJournalAsync(journal.Id);
            });
        }

        private async Task PaySupplierBillsAsync(DateTime from, DateTime to)
        {
            // Suppliers are paid about three weeks after the bill; the newest stay open.
            var bills = await _db.VendorBills.AsNoTracking()
                .Where(x => x.JournalEntry != null && x.JournalEntry.IsPosted && x.TotalAmount > x.AmountPaid)
                .ToListAsync();

            foreach (var bill in bills)
            {
                var payDay = bill.BillDate.AddDays(21);
                if (payDay < from || payDay > to || payDay > _today)
                    continue;

                await TryAsync($"pay bill {bill.BillNumber}", async () =>
                {
                    var payment = new VendorPayment
                    {
                        CompanyId = _companyId,
                        VendorBillId = bill.Id,
                        PaymentNumber = $"VPAY-{payDay:yyyy}-{++_paymentNo:0000}",
                        PaymentDate = payDay,
                        Amount = bill.TotalAmount - bill.AmountPaid,
                        PaymentMethod = "Bank Transfer"
                    };
                    _db.VendorPayments.Add(payment);
                    await _db.SaveChangesAsync();

                    var journal = await _posting.PostVendorPaymentAsync(payment.Id);
                    await PostJournalAsync(journal.Id);
                });
            }
        }

        // =====================================================================
        //  PETTY CASH, JOURNALS, VAT, LANDLORDS
        // =====================================================================

        private async Task PettyCashAsync(DateTime monthStart, DateTime lastDay)
        {
            var topUp = monthStart.AddDays(1);
            if (topUp > _today) return;

            await TryAsync("petty cash top-up", async () =>
            {
                await _pettyCash.PostReplenishmentAsync(new PettyCashReplenishmentViewModel
                {
                    PettyCashAccountId = _acc["11150"],
                    FundingAccountId = _acc["11100"],
                    ReplenishmentDate = topUp,
                    Amount = 800m,
                    ReferenceNumber = $"PCR-{monthStart:yyMM}",
                    Description = "Monthly float top-up"
                });
            });

            // A voucher is refused while the cash it draws on is still a draft.
            await _workflow.PostAllDraftsAsync(topUp, topUp, "PettyCashReplenishment");

            var vouchers = new (int Day, string PaidTo, string Text, string Account, string? Property, decimal Amount)[]
            {
                (5, "Union Cleaning Supplies", "Cleaning supplies", "51100", "naeem", 240m),
                (11, "Express Courier", "Courier - lease documents", "61100", null, 85m),
                (18, "Sweet Water Co.", "Drinking water for the office", "61100", null, 120m),
                (24, "City Locksmiths", "Locksmith call-out", "52100", "marina", 350m)
            };

            foreach (var v in vouchers)
            {
                var day = monthStart.AddDays(v.Day - 1);
                if (day > lastDay) continue;

                await TryAsync("petty cash voucher", async () =>
                {
                    await _pettyCash.PostExpenseAsync(new PettyCashExpenseViewModel
                    {
                        PettyCashAccountId = _acc["11150"],
                        VoucherDate = day,
                        PaidTo = v.PaidTo,
                        Description = v.Text,
                        ExpenseAccountId = _acc[v.Account],
                        PropertyId = v.Property == null ? null : _properties[v.Property].Id,
                        Amount = v.Amount,
                        ReferenceNumber = $"PC-{day:yyMMdd}"
                    });
                    await _workflow.PostAllDraftsAsync(day, day, "PettyCashExpense");
                });
            }
        }

        private async Task MonthEndJournalsAsync(int index, DateTime monthStart, DateTime monthEnd)
        {
            var month = monthStart.ToString("MMMM yyyy");

            await JournalAsync(monthEnd.AddDays(-2), $"Staff salaries - {month}",
                ("61100", 18_500m, 0m, "Salaries and allowances", null),
                ("11100", 0m, 18_500m, "Payroll transfer", null));

            await JournalAsync(monthEnd, $"Bank charges - {month}",
                ("63100", 125m, 0m, "Bank charges", null),
                ("11100", 0m, 125m, "Bank", null));

            if (index % 2 == 1 && _ownerCategoryIds.Count > 0)
            {
                var category = _ownerCategoryIds[index / 2 % _ownerCategoryIds.Count];
                await JournalAsync(monthStart.AddDays(14), $"Owner drawing - {month}",
                    ("34100", 20_000m, 0m, "Owner drawing", category),
                    ("11100", 0m, 20_000m, "Bank transfer", null));
            }
        }

        private async Task IssueCommissionInvoicesAsync(DateTime monthEnd)
        {
            // Month end: each landlord gets a tax invoice for the month's commission plus VAT.
            await TryAsync("issue commission invoices", async () =>
            {
                var run = await _commissionInvoices.IssueAsync(monthEnd, "Sample data");
                _result.Warnings.AddRange(run.Warnings);
            });
        }

        private async Task RemitToLandlordsAsync(DateTime monthStart, DateTime monthEnd)
        {
            // Each landlord is paid what was collected for them, less our commission, through
            // the same Owner Settlement screen a user would use. One owner is paid in round
            // thousands, so the sample shows a partial settlement carrying a balance forward.
            var owners = _properties.Values
                .Where(x => x.OwnershipType == "Managed" && !string.IsNullOrWhiteSpace(x.OwnerName))
                .Select(x => x.OwnerName!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < owners.Count; i++)
            {
                var owner = owners[i];
                await TryAsync($"settle with {owner}", async () =>
                {
                    var statement = await _ownerSettlements.BuildStatementAsync(owner, monthEnd);
                    var amount = Math.Min(statement.AvailableFromCollections, statement.MaximumPayable);
                    if (i % 2 == 1)
                        amount = Math.Floor(amount / 1_000m) * 1_000m;
                    if (amount <= 0m) return;

                    var (_, warning) = await _ownerSettlements.PayAsync(new OwnerSettlementPaymentInput
                    {
                        OwnerName = owner,
                        AsOfDate = monthEnd,
                        SettlementDate = monthEnd,
                        Amount = amount,
                        PaymentMethod = "Bank Transfer",
                        PaidFromAccountId = _acc["11100"],
                        ReferenceNumber = $"TT-{monthEnd:yyMM}-{i + 1:D2}",
                        Notes = $"Rent collected to {monthEnd:dd MMM yyyy}, less management commission"
                    }, _userId, "Sample data");

                    if (warning != null)
                        throw new InvalidOperationException(warning);
                });
            }
        }

        private async Task SettleVatAsync(DateTime monthStart, DateTime lastDay)
        {
            // The return for each quarter is paid on the 28th of the following month.
            var payDay = monthStart.AddDays(27);
            if (payDay > lastDay || (monthStart.Month - _start.Month + 12) % 3 != 0 || monthStart == _start)
                return;

            var quarterEnd = monthStart.AddDays(-1);

            async Task<decimal> BalanceAsync(string code) => await _db.JournalEntryLines
                .Where(x => x.AccountId == _acc[code] && x.JournalEntry!.IsPosted && x.JournalEntry.EntryDate <= quarterEnd)
                .SumAsync(x => (decimal?)(x.Debit - x.Credit)) ?? 0m;

            var output = -await BalanceAsync("21400");
            var input = await BalanceAsync("11300");
            if (output <= 0m && input <= 0m) return;

            var lines = new List<(string, decimal, decimal, string, int?)>();
            if (output > 0m) lines.Add(("21400", output, 0m, "Output tax for the quarter", null));
            if (input > 0m) lines.Add(("11300", 0m, input, "Recoverable input tax for the quarter", null));

            var payable = output - input;
            lines.Add(payable >= 0m
                ? ("11100", 0m, payable, "VAT paid to the Federal Tax Authority", null)
                : ("11100", -payable, 0m, "VAT refund from the Federal Tax Authority", null));

            await JournalAsync(payDay, $"VAT return - quarter to {quarterEnd:MMMM yyyy}", lines.ToArray());
            await PostAllAsync(payDay, payDay);
        }

        private async Task OneOffEventsAsync(int index, DateTime monthStart, DateTime lastDay)
        {
            var day = monthStart.AddDays(index switch { 0 => 27, 1 => 9, 2 => 0, _ => 4 });
            if (day > lastDay) return;

            switch (index)
            {
                case 0:
                    await TryAsync("insurance prepayment", () => _posting.CreatePrepaymentScheduleAsync(_companyId, new PrepaymentCreateViewModel
                    {
                        ScheduleNumber = $"PP-{day:yyyy}-001",
                        Description = "Property all-risks insurance - annual premium",
                        PaymentDate = day,
                        FirstRecognitionDate = day,
                        TotalAmount = 48_000m,
                        RecognitionMonths = 12,
                        PrepaidAccountId = _acc["11500"],
                        ExpenseAccountId = _acc["51100"],
                        OffsetAccountId = _acc["11100"],
                        ReferenceNumber = "POL-77120"
                    }));
                    break;

                case 1:
                    await BuyAssetAsync("FA-0001", "Office computers and furniture", "Office Equipment", day, 45_000m, 0m, 36, null);
                    break;

                case 2:
                    await TryAsync("software prepayment", () => _posting.CreatePrepaymentScheduleAsync(_companyId, new PrepaymentCreateViewModel
                    {
                        ScheduleNumber = $"PP-{day:yyyy}-002",
                        Description = "Accounting software - annual subscription",
                        PaymentDate = day,
                        FirstRecognitionDate = day,
                        TotalAmount = 9_600m,
                        RecognitionMonths = 12,
                        PrepaidAccountId = _acc["11500"],
                        ExpenseAccountId = _acc["61100"],
                        OffsetAccountId = _acc["11100"]
                    }));
                    break;

                case 3:
                    await BuyAssetAsync("FA-0002", "Maintenance van", "Motor Vehicles", day, 95_000m, 15_000m, 60, null);
                    break;
            }

            await PostAllAsync(day, day);
        }

        private async Task BuyAssetAsync(string code, string name, string category, DateTime day,
            decimal cost, decimal residual, int lifeMonths, int? propertyId)
        {
            await TryAsync($"fixed asset {code}", async () =>
            {
                var id = await _fixedAssets.CreateDraftAssetAsync(_companyId, new FixedAssetCreateViewModel
                {
                    PropertyId = propertyId,
                    AssetCode = code,
                    Name = name,
                    AssetCategory = category,
                    AcquisitionDate = day,
                    InServiceDate = day,
                    Cost = cost,
                    ResidualValue = residual,
                    UsefulLifeMonths = lifeMonths,
                    AssetAccountId = _acc["15200"],
                    AccumulatedDepreciationAccountId = _acc["15300"],
                    DepreciationExpenseAccountId = _acc["62100"],
                    InitialCreditAccountId = _acc["11100"]
                });

                await PostAllAsync(day, day);

                await _fixedAssets.CapitalizeAsync(new FixedAssetCapitalizeViewModel
                {
                    FixedAssetId = id,
                    CapitalizationDate = day,
                    InServiceDate = day,
                    CreditAccountId = _acc["15400"]
                });
            });
        }

        // =====================================================================
        //  AFTER THE LAST MONTH
        // =====================================================================

        private async Task ClosePastPeriodsAsync()
        {
            // Everything up to three months ago is closed, as an accountant would have done.
            var closeBefore = new DateTime(_today.Year, _today.Month, 1).AddMonths(-2);

            var periods = await _db.AccountingPeriods
                .Where(x => !x.IsClosed && x.StartDate >= _start && x.EndDate < closeBefore)
                .OrderBy(x => x.StartDate)
                .Select(x => new { x.FiscalYear, x.PeriodNumber })
                .ToListAsync();

            foreach (var period in periods)
            {
                await TryAsync($"close period {period.PeriodNumber}/{period.FiscalYear}",
                    () => _posting.CloseAccountingPeriodAsync(_companyId, period.FiscalYear, period.PeriodNumber));
            }

            // A whole earlier fiscal year in the data is closed into retained earnings.
            var currentFiscalYear = _today.Month >= _start.Month ? _today.Year : _today.Year - 1;
            for (var year = _start.Year; year < currentFiscalYear; year++)
            {
                var fiscalYear = year;
                await TryAsync($"close fiscal year {fiscalYear}", async () =>
                {
                    var journal = await _posting.CloseFiscalYearAsync(_companyId, fiscalYear);
                    if (!journal.IsPosted)
                        await PostJournalAsync(journal.Id);
                });
            }
        }

        private async Task CreateServiceRequestsAsync()
        {
            var tenants = _tenantIds;
            var leases = await _db.Leases.AsNoTracking().Include(x => x.Unit).ToListAsync();
            var number = 0;

            async Task Add(string tenantKey, string type, string? category, string subject, string description,
                string priority, string status, int daysAgo, string? resolution = null, int? relatedInvoiceId = null,
                params (string Author, string Body, bool Internal, int HoursLater)[] comments)
            {
                if (!tenants.TryGetValue(tenantKey, out var tenantId)) return;
                var lease = leases.FirstOrDefault(x => x.TenantId == tenantId);
                if (lease == null) return;

                var submitted = _today.AddDays(-daysAgo).AddHours(9);
                var request = new TenantServiceRequest
                {
                    CompanyId = _companyId,
                    TenantId = tenantId,
                    LeaseId = lease.Id,
                    PropertyId = lease.Unit?.PropertyId,
                    UnitId = lease.UnitId,
                    RequestNumber = $"REQ-{submitted:yyyy}-{++number:0000}",
                    RequestType = type,
                    Category = category,
                    Subject = subject,
                    Description = description,
                    Priority = priority,
                    Status = status,
                    SubmittedAt = submitted,
                    AcknowledgedAt = status == ServiceRequestStatuses.Submitted ? null : submitted.AddHours(2),
                    ResolvedAt = status is ServiceRequestStatuses.Resolved or ServiceRequestStatuses.Closed ? submitted.AddDays(2) : null,
                    ClosedAt = status == ServiceRequestStatuses.Closed ? submitted.AddDays(3) : null,
                    AssignedToAppUserId = status == ServiceRequestStatuses.Submitted ? null : _userId,
                    ResolutionNotes = resolution,
                    RelatedInvoiceId = relatedInvoiceId,
                    RequestedNewDueDate = relatedInvoiceId.HasValue ? _today.AddDays(14) : null
                };

                foreach (var c in comments)
                {
                    request.Comments.Add(new TenantServiceRequestComment
                    {
                        AuthorType = c.Author == "Tenant" ? ServiceRequestAuthorTypes.Tenant : ServiceRequestAuthorTypes.Staff,
                        AuthorName = c.Author == "Tenant" ? "Tenant" : "Property team",
                        AppUserId = c.Author == "Tenant" ? null : _userId,
                        Body = c.Body,
                        IsInternal = c.Internal,
                        CreatedAt = submitted.AddHours(c.HoursLater)
                    });
                }

                _db.TenantServiceRequests.Add(request);
                await _db.SaveChangesAsync();
            }

            var unpaidNorthwind = await _db.CustomerInvoices.AsNoTracking()
                .Where(x => x.Lease!.TenantId == tenants["northwind"] && x.TotalAmount > x.AmountPaid && x.JournalEntryId != null)
                .Select(x => (int?)x.Id).FirstOrDefaultAsync();

            await Add("northwind", ServiceRequestTypes.Maintenance, "Air Conditioning", "Meeting room AC not cooling",
                "The AC in our main meeting room has been blowing warm air since yesterday morning.",
                ServiceRequestPriorities.High, ServiceRequestStatuses.InProgress, 6, null, null,
                ("Staff", "Thank you for reporting this. CoolTech will visit on Thursday between 9 and 11 am.", false, 3),
                ("Tenant", "Thank you. Our office manager will be there to let them in.", false, 4),
                ("Staff", "CoolTech suspect a gas leak - covered under the maintenance contract, no charge to the tenant.", true, 8));

            await Add("khalid", ServiceRequestTypes.Maintenance, "Plumbing", "Water leak under kitchen sink",
                "Water is pooling under the kitchen sink and the cabinet floor is wet.",
                ServiceRequestPriorities.Urgent, ServiceRequestStatuses.Submitted, 1);

            await Add("desertbloom", ServiceRequestTypes.ContractRenewal, null, "Renewal for next year",
                "We would like to renew for another year on the same terms. Please send the renewal contract.",
                ServiceRequestPriorities.Normal, ServiceRequestStatuses.Acknowledged, 9);

            await Add("omar", ServiceRequestTypes.Maintenance, "Electrical", "Bedroom light switch faulty",
                "The switch in the master bedroom sparks when turned on.",
                ServiceRequestPriorities.High, ServiceRequestStatuses.Closed, 28,
                "Electrician replaced the switch and checked the circuit. No further faults found.", null,
                ("Staff", "An electrician is booked for tomorrow morning.", false, 2));

            await Add("northwind", ServiceRequestTypes.PaymentPostponement, null, "Request more time for this quarter's rent",
                "A major client settles at the end of the month. We can clear the rent in full within two weeks.",
                ServiceRequestPriorities.Normal, ServiceRequestStatuses.Submitted, 3, null, unpaidNorthwind);

            await Add("sara", ServiceRequestTypes.Complaint, "Noise", "Construction noise next door",
                "Construction work on the neighbouring plot starts before 7 am every day.",
                ServiceRequestPriorities.Low, ServiceRequestStatuses.Resolved, 40,
                "Raised with the contractor; work now starts at 8 am in line with municipality rules.");
        }

        private async Task LeaveWorkInDraftAsync()
        {
            // A little unfinished work, so the draft screens and Post All Drafts have
            // something to show.
            await JournalAsync(_today, "Staff training course fee - to review", post: false, lines: new[]
            {
                ("61100", 2_750m, 0m, "Fire safety training for building staff", (int?)null),
                ("11100", 0m, 2_750m, "Bank transfer", (int?)null)
            });

            await TryAsync("draft bill", async () =>
            {
                var modelState = new ModelStateDictionary();
                var bill = await _bills.SaveAsync(modelState, _companyId, new VendorBillEditViewModel
                {
                    VendorId = _vendors["cooltech"].Id,
                    VendorReference = $"CT-{_today:yyMMdd}",
                    BillDate = _today.AddDays(-1),
                    AccountingDate = _today.AddDays(-1),
                    DueDate = _today.AddDays(29),
                    PropertyId = _properties["corniche"].Id,
                    ExpenseCategory = "Emergency AC repair",
                    Lines = new List<VendorBillLineViewModel>
                    {
                        new()
                        {
                            Description = "Emergency compressor repair - clinic", AccountId = _acc["52100"],
                            PropertyId = _properties["corniche"].Id, Quantity = 1, UnitPrice = 2_400m,
                            IsTaxable = true, InputTaxAttribution = InputTaxAttributions.Taxable
                        }
                    }
                });

                if (bill == null)
                    throw new InvalidOperationException(Describe(modelState));
            });
        }

        /// <summary>
        /// The business centre under the Capital Assets Scheme. It was bought while two floors
        /// were planned as staff flats, so only 70% of its input tax was recovered; it has since
        /// been let entirely to businesses, so year 1's adjustment gives back more input tax.
        /// </summary>
        private async Task RegisterCapitalAssetAsync()
        {
            var firstOfMonth = new DateTime(_today.Year, _today.Month, 1);

            await TryAsync("register the business centre under the Capital Assets Scheme", async () =>
            {
                var record = await _capitalAssets.RegisterManualAsync("Al Naeem Business Centre", _properties["naeem"].Id,
                    24_000_000m, 1_200_000m, 840_000m, firstOfMonth.AddMonths(-13),
                    "Bought while two floors were planned as staff flats (exempt), so 70% of the input tax was recovered. " +
                    "Now let entirely to businesses.");

                var (proposed, explanation) = await _capitalAssets.ProposeTaxableUseAsync(record.Id, 1);
                await _capitalAssets.PostAdjustmentAsync(record.Id, 1, proposed ?? 100m, firstOfMonth.AddMonths(-1),
                    proposed.HasValue ? explanation : "Let entirely to VAT-registered businesses.");
            });
        }

        /// <summary>
        /// A shop contract sent for approval by the leasing officer, waiting on the Approvals
        /// screen for the user to approve or return. Approval stays switched off for the
        /// company afterwards: a trainee working alone could not approve their own contracts.
        /// </summary>
        private async Task SubmitContractForApprovalAsync()
        {
            await TryAsync("contract awaiting approval", async () =>
            {
                var shop = await _db.Units.Include(x => x.Property)
                    .Where(x => x.PropertyId == _properties["annex"].Id && x.Status == "Vacant")
                    .OrderBy(x => x.Id).FirstOrDefaultAsync();
                if (shop == null) return;

                var startDate = new DateTime(_today.Year, _today.Month, 1).AddMonths(1);
                var tenantId = _tenantIds["reem"];
                var lease = new Lease
                {
                    CompanyId = _companyId,
                    UnitId = shop.Id,
                    TenantId = tenantId,
                    ContractNumber = await _codes.GenerateContractCodeAsync(shop.Id, tenantId, startDate),
                    StartDate = startDate,
                    EndDate = startDate.AddYears(1).AddDays(-1),
                    AnnualRent = shop.StandardAnnualRent,
                    BillingFrequencyMonths = 3,
                    SecurityDepositAmount = 4_750m,
                    Status = LeaseStatuses.Draft,
                    Notes = "Sample lease - awaiting approval",
                    CreatedAt = DateTime.UtcNow
                };
                VatDeterminationService.ApplyToLease(lease, VatDeterminationService.ResolveCategory(shop.Property!, shop), null);
                _db.Leases.Add(lease);
                await _db.SaveChangesAsync();

                var company = await _db.Companies.FirstAsync(x => x.Id == _companyId);
                var wasRequired = company.RequireContractApproval;
                company.RequireContractApproval = true;
                await _db.SaveChangesAsync();
                try
                {
                    // Submitted by someone other than the user, so the four-eyes rule lets them decide it.
                    var (success, message) = await _leaseWorkflow.SubmitAsync(lease.Id, 0, "Leasing Officer (sample)");
                    if (!success)
                        throw new InvalidOperationException(message);
                }
                finally
                {
                    company.RequireContractApproval = wasRequired;
                    await _db.SaveChangesAsync();
                }
            });
        }

        /// <summary>
        /// E-invoicing started voluntarily three months ago. Business tenants' invoices from then
        /// on are produced; older ones were sent and accepted, one was rejected and waits to be
        /// corrected, and last month's and this month's are ready to send.
        /// </summary>
        private async Task StartEInvoicingAsync()
        {
            var firstOfMonth = new DateTime(_today.Year, _today.Month, 1);

            await TryAsync("start e-invoicing", async () =>
            {
                var company = await _db.Companies.FirstAsync(x => x.Id == _companyId);
                if (company.EInvoicingEnabled)
                    return;

                company.EInvoicingEnabled = true;
                company.EInvoicingStartDate = firstOfMonth.AddMonths(-3);
                company.EInvoicingRevenueBand ??= EInvoicingRevenueBands.Standard;
                company.EInvoicingProviderName ??= "Training Access Point (fictional)";
                company.EInvoicingProviderAppointedOn ??= firstOfMonth.AddMonths(-4);
                if (string.IsNullOrWhiteSpace(company.EInvoicePaymentMeansCode))
                    company.EInvoicePaymentMeansCode = EInvoicePaymentMeans.CreditTransfer;
                await _db.SaveChangesAsync();

                await _eInvoices.ReassessAsync(_companyId);
                var batch = await _eInvoices.GenerateAllPendingAsync(_companyId);
                _result.Warnings.AddRange(batch.Failures.Select(x => $"e-invoice: {x.Message} {string.Join(" ", x.Problems)}"));

                var sent = await _db.CustomerInvoices.AsNoTracking()
                    .Where(x => x.EInvoiceStatus == EInvoiceStatuses.Generated && x.InvoiceDate < firstOfMonth.AddMonths(-1))
                    .OrderBy(x => x.InvoiceDate).ThenBy(x => x.Id)
                    .Select(x => x.Id).ToListAsync();
                if (sent.Count == 0)
                    return;

                var refs = sent.Select(id => new EInvoiceDocumentRef(EInvoiceService.KindInvoice, id)).ToList();
                await _eInvoices.MarkSubmittedAsync(_companyId, refs, $"TAP-{firstOfMonth.AddMonths(-1):yyyyMM}-BATCH01");

                for (var i = 0; i < refs.Count; i++)
                {
                    var rejected = i == refs.Count - 1;
                    await _eInvoices.RecordResponseAsync(_companyId, refs[i], !rejected, $"TAP-ACK-{refs[i].Id:000000}",
                        rejected ? "Buyer TRN not found in the FTA register - check the tenant's TRN and send again." : null);
                }
            });
        }

        /// <summary>
        /// Two suppliers' e-invoices delivered by the provider: one from a known vendor, ready to
        /// book, and one from a supplier not set up yet.
        /// </summary>
        private async Task ReceiveSupplierEInvoicesAsync()
        {
            await TryAsync("received e-invoices", async () =>
            {
                var issued = _today.AddDays(-2);
                var utilities = _vendors["utilities"];

                var documents = new[]
                {
                    SupplierEInvoiceXml($"NUC-E-{issued:yyMMdd}", issued, utilities.Name, utilities.TrnNumber!,
                        ("Water and electricity - Al Naeem Business Centre common areas", 3_420m),
                        ("Water and electricity - Marina Heights common areas", 2_610m)),
                    SupplierEInvoiceXml($"BW-{issued:yyyy}-0412", issued.AddDays(-1), "Blue Wave Pest Control LLC", "100000001600003",
                        ("Quarterly pest control - Marina Heights", 1_850m))
                };

                foreach (var xml in documents)
                {
                    var parsed = EInvoiceReader.Parse(xml);
                    var vendor = _vendors.Values.FirstOrDefault(x => x.TrnNumber == parsed.SupplierTrn);
                    _db.ReceivedEInvoices.Add(new ReceivedEInvoice
                    {
                        CompanyId = _companyId,
                        Uuid = parsed.Uuid,
                        DocumentTypeCode = parsed.DocumentTypeCode,
                        DocumentNumber = parsed.DocumentNumber,
                        IssueDate = parsed.IssueDate,
                        DueDate = parsed.DueDate,
                        SupplierName = parsed.SupplierName,
                        SupplierTrn = parsed.SupplierTrn,
                        SupplierTin = parsed.SupplierTin,
                        VendorId = vendor?.Id,
                        Currency = parsed.Currency,
                        NetAmount = parsed.NetAmount,
                        VatAmount = parsed.VatAmount,
                        TotalAmount = parsed.TotalAmount,
                        Xml = xml,
                        FileName = EInvoiceService.FileNameFor(parsed.DocumentNumber),
                        Status = "Received",
                        ReceivedAt = DateTime.UtcNow,
                        ReceivedByAppUserId = _userId
                    });
                }
                await _db.SaveChangesAsync();
            });
        }

        /// <summary>A supplier's PINT AE invoice addressed to this company, standard rated.</summary>
        private string SupplierEInvoiceXml(string number, DateTime issued, string supplierName, string supplierTrn,
            params (string Text, decimal Net)[] lines)
        {
            XNamespace inv = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
            XNamespace cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
            XNamespace cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
            static string A(decimal x) => x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

            var net = lines.Sum(x => x.Net);
            var vat = Math.Round(net * 0.05m, 2);

            XElement Party(string name, string trn) => new(cac + "Party",
                new XElement(cbc + "EndpointID", new XAttribute("schemeID", "0235"), trn[..10]),
                new XElement(cac + "PartyName", new XElement(cbc + "Name", name)),
                new XElement(cac + "PartyTaxScheme",
                    new XElement(cbc + "CompanyID", trn),
                    new XElement(cac + "TaxScheme", new XElement(cbc + "ID", "VAT"))),
                new XElement(cac + "PartyLegalEntity", new XElement(cbc + "RegistrationName", name)));

            XElement Category(string element) => new(cac + element,
                new XElement(cbc + "ID", "S"),
                new XElement(cbc + "Percent", "5"),
                new XElement(cac + "TaxScheme", new XElement(cbc + "ID", "VAT")));

            var document = new XElement(inv + "Invoice",
                new XAttribute(XNamespace.Xmlns + "cac", cac),
                new XAttribute(XNamespace.Xmlns + "cbc", cbc),
                new XElement(cbc + "CustomizationID", "urn:peppol:pint:billing-1@ae-1"),
                new XElement(cbc + "ProfileID", "urn:peppol:bis:billing"),
                new XElement(cbc + "ID", number),
                new XElement(cbc + "UUID", Guid.NewGuid().ToString()),
                new XElement(cbc + "IssueDate", issued.ToString("yyyy-MM-dd")),
                new XElement(cbc + "DueDate", issued.AddDays(30).ToString("yyyy-MM-dd")),
                new XElement(cbc + "InvoiceTypeCode", "380"),
                new XElement(cbc + "DocumentCurrencyCode", "AED"),
                new XElement(cac + "AccountingSupplierParty", Party(supplierName, supplierTrn)),
                new XElement(cac + "AccountingCustomerParty", Party(_company.LegalName ?? _company.Name, _company.TrnNumber ?? "100000000000003")),
                new XElement(cac + "TaxTotal",
                    new XElement(cbc + "TaxAmount", new XAttribute("currencyID", "AED"), A(vat)),
                    new XElement(cac + "TaxSubtotal",
                        new XElement(cbc + "TaxableAmount", new XAttribute("currencyID", "AED"), A(net)),
                        new XElement(cbc + "TaxAmount", new XAttribute("currencyID", "AED"), A(vat)),
                        Category("TaxCategory"))),
                new XElement(cac + "LegalMonetaryTotal",
                    new XElement(cbc + "LineExtensionAmount", new XAttribute("currencyID", "AED"), A(net)),
                    new XElement(cbc + "TaxExclusiveAmount", new XAttribute("currencyID", "AED"), A(net)),
                    new XElement(cbc + "TaxInclusiveAmount", new XAttribute("currencyID", "AED"), A(net + vat)),
                    new XElement(cbc + "PayableAmount", new XAttribute("currencyID", "AED"), A(net + vat))),
                lines.Select((line, i) => new XElement(cac + "InvoiceLine",
                    new XElement(cbc + "ID", i + 1),
                    new XElement(cbc + "InvoicedQuantity", new XAttribute("unitCode", "C62"), "1"),
                    new XElement(cbc + "LineExtensionAmount", new XAttribute("currencyID", "AED"), A(line.Net)),
                    new XElement(cac + "Item",
                        new XElement(cbc + "Name", line.Text),
                        Category("ClassifiedTaxCategory")),
                    new XElement(cac + "Price",
                        new XElement(cbc + "PriceAmount", new XAttribute("currencyID", "AED"), A(line.Net))))));

            return new XDocument(new XDeclaration("1.0", "UTF-8", null), document).ToString();
        }

        /// <summary>
        /// Scans of the registration papers, so the property screens show the document list and
        /// the expiry warnings. Each is a one-page PDF marked as a training sample.
        /// </summary>
        private async Task AddPropertyDocumentsAsync()
        {
            await TryAsync("property documents", async () =>
            {
                void Add(string propertyKey, string type, string? number, DateTime? issued, DateTime? expires, string description)
                {
                    var property = _properties[propertyKey];
                    var content = SamplePdf(type.ToUpperInvariant(), property.Name, number != null ? $"Number: {number}" : "",
                        issued.HasValue ? $"Issued: {issued:dd/MM/yyyy}" : "",
                        expires.HasValue ? $"Expires: {expires:dd/MM/yyyy}" : "",
                        "TRAINING SAMPLE - NOT A REAL DOCUMENT");

                    _db.PropertyDocuments.Add(new PropertyDocument
                    {
                        CompanyId = _companyId,
                        PropertyId = property.Id,
                        DocumentType = type,
                        DocumentNumber = number,
                        Description = description,
                        IssueDate = issued,
                        ExpiryDate = expires,
                        FileName = $"{type.Replace(" ", "-").Replace("/", "-")}-{property.Code}.pdf",
                        ContentType = "application/pdf",
                        SizeBytes = content.Length,
                        Content = content,
                        UploadedAt = DateTime.UtcNow,
                        UploadedByName = "Sample data"
                    });
                }

                var marina = _properties["marina"];
                var naeem = _properties["naeem"];
                Add("marina", "Title deed", marina.TitleDeedNumber, marina.TitleDeedDate, null, "Freehold title deed");
                Add("marina", "Insurance policy", marina.InsurancePolicyNumber, _today.AddMonths(-10).AddDays(45), marina.InsuranceExpiryDate,
                    "Property all-risks policy - renewal due soon");
                Add("naeem", "Title deed", naeem.TitleDeedNumber, naeem.TitleDeedDate, null, "Freehold title deed");
                Add("naeem", "Mortgage document", "EI-RAK-55102", new DateTime(2018, 2, 11), null, "Mortgage registration - Emirates Islamic");
                Add("marjanbay", "Building permit", _properties["marjanbay"].BuildingPermitNumber, _properties["marjanbay"].BuildingPermitDate,
                    _properties["marjanbay"].BuildingPermitDate?.AddYears(3), "Permit for the 14-storey tower");
                Add("annex", "Building permit", _properties["annex"].BuildingPermitNumber, _properties["annex"].BuildingPermitDate,
                    _properties["annex"].BuildingPermitDate?.AddYears(1), "Permit for the four-shop retail annex");
                await _db.SaveChangesAsync();
            });
        }

        /// <summary>A minimal one-page PDF showing the given lines of text.</summary>
        private static byte[] SamplePdf(params string[] lines)
        {
            static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

            var text = new System.Text.StringBuilder("BT /F1 16 Tf 72 760 Td 22 TL\n");
            foreach (var line in lines.Where(x => x.Length > 0))
                text.Append('(').Append(Escape(line)).Append(") Tj T*\n");
            text.Append("ET");
            var stream = text.ToString();

            var objects = new[]
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                $"<< /Length {System.Text.Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream"
            };

            var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            for (var i = 0; i < objects.Length; i++)
            {
                offsets.Add(System.Text.Encoding.ASCII.GetByteCount(pdf.ToString()));
                pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }

            var xref = System.Text.Encoding.ASCII.GetByteCount(pdf.ToString());
            pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
            foreach (var offset in offsets)
                pdf.Append($"{offset:0000000000} 00000 n \n");
            pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

            return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        private Task JournalAsync(DateTime date, string description,
            params (string Account, decimal Debit, decimal Credit, string Text, int? Category)[] lines) =>
            JournalAsync(date, description, true, lines);

        private async Task JournalAsync(DateTime date, string description, bool post,
            (string Account, decimal Debit, decimal Credit, string Text, int? Category)[] lines)
        {
            await TryAsync($"journal '{description}'", async () =>
            {
                var id = await _manualJournals.CreateManualJournalAsync(new ManualJournalEntryViewModel
                {
                    EntryDate = date,
                    Description = description,
                    Lines = lines.Select(x => new ManualJournalLineViewModel
                    {
                        AccountId = _acc[x.Account],
                        Debit = x.Debit,
                        Credit = x.Credit,
                        LineDescription = x.Text,
                        OwnerWithdrawalCategoryId = x.Category
                    }).ToList()
                });

                if (post)
                    await PostJournalAsync(id);
            });
        }

        private async Task PostJournalAsync(int journalEntryId)
        {
            var result = await _workflow.PostAsync(journalEntryId);
            if (!result.Success && !result.Message.Contains("already posted", StringComparison.OrdinalIgnoreCase))
                _result.Warnings.Add(result.Message);
        }

        private async Task PostAllAsync(DateTime from, DateTime to)
        {
            var batch = await _workflow.PostAllDraftsAsync(from, to, null);
            _result.Warnings.AddRange(batch.Failures);
        }

        /// <summary>
        /// Runs one step. A step that fails is recorded and skipped rather than stopping
        /// the build: a training company with one cheque missing is still useful.
        /// </summary>
        private async Task TryAsync(string step, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                _result.Warnings.Add($"{step}: {ex.GetBaseException().Message}");
                _db.ChangeTracker.Clear();
            }
        }

        private async Task TryAsync<T>(string step, Func<Task<T>> action) =>
            await TryAsync(step, async () => { await action(); });

        private static string Describe(ModelStateDictionary modelState) =>
            string.Join(" ", modelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage));

        private sealed record SampleLease(
            int LeaseId, int TenantId, int PropertyId, string Ownership, decimal Commission,
            DateTime StartDate, string Behaviour, decimal Deposit);
    }
}
