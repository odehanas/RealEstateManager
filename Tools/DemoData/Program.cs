// Builds a LOCAL demo database for the help screenshots. Every name, number and
// amount here is fictional. Never points at the live database.
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

// REM_DEMO_DB names a different throwaway database, e.g. for comparing schemas.
var dbName = Environment.GetEnvironmentVariable("REM_DEMO_DB") ?? "RemDemo";
var Cs = $@"Server=(localdb)\ClaudeREM;Database={dbName};Trusted_Connection=True;TrustServerCertificate=True";

var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(Cs).Options;
using var db = new ApplicationDbContext(options, new NoCompany(), new DatabaseConnectionOptions { ConnectionString = Cs });

await db.Database.EnsureDeletedAsync();
await db.Database.EnsureCreatedAsync();

var company = new Company
{
    Name = "Gulf Horizon Properties",
    LegalName = "Gulf Horizon Properties LLC",
    BaseCurrency = "AED",
    FiscalYearStartMonth = 1,
    TrnNumber = "100234567800003",
    AddressLine1 = "Office 1104, Julphar Tower",
    AddressLine2 = "Al Hamra Street",
    City = "Ras Al Khaimah",
    Country = "United Arab Emirates",
    Phone = "+971 7 222 4400",
    Email = "accounts@gulfhorizon.example",
    ProvisionalVatRecoveryRate = 70m,
    // E-invoicing identity (fictional). Switched off: the demo shows the set-up state.
    TradeLicenseNumber = "RAK-10458",
    LegalRegistrationType = LegalRegistrationTypes.TradeLicence,
    LicensingAuthority = "Department of Economic Development - Ras Al Khaimah",
    Emirate = "RAK",
    CountryCode = "AE",
    BankIban = "AE070331234567890123456",
    BankAccountName = "Gulf Horizon Properties LLC",
    EInvoicePaymentMeansCode = EInvoicePaymentMeans.CreditTransfer,
    EInvoicingRevenueBand = EInvoicingRevenueBands.Standard
};
db.Companies.Add(company);
await db.SaveChangesAsync();

// ---- Staff ---------------------------------------------------------------
var hasher = new PasswordHashingService();
AppUser User(string username, string name, bool admin = false) => new()
{
    Username = username, DisplayName = name, IsActive = true, IsSystemAdmin = admin,
    PasswordHash = hasher.HashPassword(admin ? "Admin@123" : "Demo@123")
};
var admin = User("admin", "System Administrator", admin: true);
var fatima = User("fatima.noor", "Fatima Noor");
var ravi = User("ravi.menon", "Ravi Menon");
var hassan = User("hassan.ali", "Hassan Ali");
db.AppUsers.AddRange(admin, fatima, ravi, hassan);
await db.SaveChangesAsync();

foreach (var u in new[] { admin, fatima, ravi, hassan })
    db.AppUserCompanies.Add(new AppUserCompany { AppUserId = u.Id, CompanyId = company.Id, IsDefault = true });

void Grant(AppUser u, params string[] keys)
{
    foreach (var k in keys) db.AppUserPermissions.Add(new AppUserPermission { AppUserId = u.Id, PermissionKey = k });
}
Grant(fatima, AppPermissions.MasterDataView, AppPermissions.AccountingManage, AppPermissions.ReportsView);
Grant(ravi, AppPermissions.MasterDataView, AppPermissions.LeasesManage, AppPermissions.ServiceRequestsManage);
Grant(hassan, AppPermissions.MasterDataManage, AppPermissions.LeasesManage);

// ---- Properties and units --------------------------------------------------
Property Prop(string code, string name, string type, string own, string vat, string address, decimal value, string? owner = null, decimal commission = 0) => new()
{
    CompanyId = company.Id, Code = code, Name = name, PropertyType = type, OwnershipType = own,
    VatCategory = vat, Address = address, AssetValue = value, OwnerName = owner, CommissionPercentage = commission, IsActive = true
};
var marina = Prop("OWN-R-0001", "Marina Heights Residence", "Residential", "Owned", VatCategories.Exempt, "Al Marjan Island, Ras Al Khaimah", 18_500_000m);
var naeem = Prop("OWN-C-0001", "Al Naeem Business Centre", "Commercial", "Owned", VatCategories.Standard, "Al Nakheel, Ras Al Khaimah", 24_000_000m);
var corniche = Prop("MNG-C-0001", "Corniche Retail Plaza", "Commercial", "Managed", VatCategories.Standard, "Corniche Al Qawasim, Ras Al Khaimah", 0m, "Al Mansoori Holdings", 8m);
var palm = Prop("MNG-R-0001", "Palm View Villas", "Residential", "Managed", VatCategories.Exempt, "Mina Al Arab, Ras Al Khaimah", 0m, "Sheikha Investments LLC", 6m);
db.Properties.AddRange(marina, naeem, corniche, palm);
await db.SaveChangesAsync();

Unit U(Property p, int n, string floor, string usage, decimal area, decimal rent, string status = "Vacant") => new()
{
    PropertyId = p.Id, UnitNumber = $"{p.Code}-U{n:00}", Floor = floor, UsageType = usage,
    AreaSqFt = area, StandardAnnualRent = rent, Status = status, IsActive = true
};
var units = new[]
{
    U(marina, 1, "3", "Apartment 2BR", 1250, 78_000), U(marina, 2, "5", "Apartment 2BR", 1320, 84_000),
    U(marina, 3, "7", "Apartment 3BR", 1650, 96_000), U(marina, 4, "9", "Apartment 3BR", 1650, 98_000),
    U(naeem, 1, "2", "Office", 2100, 150_000), U(naeem, 2, "4", "Office", 2400, 190_000),
    U(naeem, 3, "6", "Office", 1200, 110_000, "UnderMaintenance"),
    U(corniche, 1, "G", "Retail", 1400, 120_000), U(corniche, 2, "G", "Clinic", 1100, 96_000),
    U(palm, 1, "-", "Villa 4BR", 3800, 140_000), U(palm, 2, "-", "Villa 4BR", 3800, 145_000)
};
db.Units.AddRange(units);

// ---- Tenants ------------------------------------------------------------------
Tenant T(int n, string name, string email, string phone, string nat, string? eid = null, string? passport = null, string? licence = null, string? trn = null) => new()
{
    CompanyId = company.Id, Code = $"TNT-{n:00000}", FullName = name, Email = email, Phone = phone, Nationality = nat,
    IdNumber = eid, PassportNumber = passport, TradeLicenseNumber = licence, TrnNumber = trn, IsActive = true,
    // A licence holder is a business: its invoices are in scope for e-invoicing.
    PartyType = licence != null ? PartyTypes.Business : PartyTypes.Individual,
    LicensingAuthority = licence == null ? null
        : licence.StartsWith("RAKEZ") ? "Ras Al Khaimah Economic Zone (RAKEZ)" : "Department of Economic Development - Ras Al Khaimah",
    Address = licence != null ? $"Office {100 + n * 7}, Al Muntasir Road, Al Nakheel" : null,
    City = "Ras Al Khaimah",
    Emirate = "RAK",
    CountryCode = "AE"
};
var tenants = new[]
{
    T(1, "Omar Al Hashimi", "omar.hashimi@mail.example", "+971 50 412 7781", "UAE", eid: "784-1985-4127781-2"),
    T(2, "Priya Raman", "priya.raman@mail.example", "+971 55 330 9142", "India", eid: "784-1990-3309142-6", passport: "Z4418093"),
    T(3, "Northwind Trading LLC", "accounts@northwind.example", "+971 7 226 5100", "UAE", licence: "RAK-45821", trn: "100345678900003"),
    T(4, "Falcon Logistics FZ-LLC", "finance@falconlog.example", "+971 7 204 8800", "UAE", licence: "RAKEZ-77310", trn: "100456789000003"),
    T(5, "Sara Mitchell", "sara.mitchell@mail.example", "+971 52 870 1166", "United Kingdom", passport: "531187744"),
    T(6, "Desert Bloom Café LLC", "hello@desertbloom.example", "+971 7 233 0909", "UAE", licence: "RAK-51907", trn: "100567890100003"),
    T(7, "Khalid Al Suwaidi", "khalid.suwaidi@mail.example", "+971 56 118 4420", "UAE", eid: "784-1979-1184420-9"),
    T(8, "Bright Smile Dental Clinic", "admin@brightsmile.example", "+971 7 245 7070", "UAE", licence: "RAK-60244", trn: "100678901200003")
};
db.Tenants.AddRange(tenants);

// ---- Vendors ----------------------------------------------------------------
db.Vendors.AddRange(
    new Vendor { CompanyId = company.Id, Code = "VEN-0001", Name = "Emirates Facility Services LLC", ServiceType = "Cleaning & Security", ContactPerson = "Anil Kumar", Phone = "+971 7 228 1200", Email = "billing@efs.example", TrnNumber = "100789012300003", TradeLicenseNumber = "RAK-30215", IsVatRegistered = true, IsActive = true },
    new Vendor { CompanyId = company.Id, Code = "VEN-0002", Name = "CoolTech Air Conditioning", ServiceType = "HVAC Maintenance", ContactPerson = "Yousef Darwish", Phone = "+971 7 236 4455", Email = "service@cooltech.example", TrnNumber = "100890123400003", TradeLicenseNumber = "RAK-41877", IsVatRegistered = true, IsActive = true },
    new Vendor { CompanyId = company.Id, Code = "VEN-0003", Name = "Rashid Handyman Services", ServiceType = "General Repairs", ContactPerson = "Rashid Khan", Phone = "+971 50 667 2210", TradeLicenseNumber = "RAK-72034", IsVatRegistered = false, IsActive = true });

await db.SaveChangesAsync();

// ---- Tenant portal login for the demo tenant (Northwind Trading) ---------------
db.TenantPortalUsers.Add(new TenantPortalUser
{
    CompanyId = company.Id, TenantId = tenants[2].Id, Username = "tenant@example.com",
    PasswordHash = hasher.HashPassword("Tenant123!"), IsActive = true, MustChangePassword = false,
    LastLoginAt = new DateTime(2026, 10, 12, 9, 14, 0)
});
await db.SaveChangesAsync();

Console.WriteLine($"demo master data ready: company {company.Id}, {units.Length} units, {tenants.Length} tenants");

sealed class NoCompany : ICurrentCompanyAccessor
{
    public int? CompanyId => null;
    public int? UserId => null;
    public bool IsAuthenticated => false;
}
