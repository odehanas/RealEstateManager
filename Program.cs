using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = ResolveConnectionString(builder);
builder.Services.AddSingleton(new DatabaseConnectionOptions
{
    ConnectionString = connectionString
});
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ConnectionStrings:DefaultConnection"] = connectionString
});

builder.Services.Configure<FeedbackNotificationOptions>(builder.Configuration.GetSection("FeedbackNotifications"));
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentCompanyAccessor, CurrentCompanyAccessor>();
builder.Services.AddScoped<PasswordHashingService>();
builder.Services.AddScoped<AppAuthService>();
builder.Services.AddScoped<AdminManagementService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddScoped<FeedbackService>();
builder.Services.AddScoped<FeedbackNotificationService>();
builder.Services.AddScoped<SubscriptionEnforcementFilter>();
builder.Services.AddScoped<PermissionEnforcementFilter>();
builder.Services.AddScoped<PortalBoundaryFilter>();
builder.Services.AddScoped<TenantPortalAuthService>();
builder.Services.AddScoped<TenantPortalService>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Home/AccessDenied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);

        // The global policy lists BOTH schemes so their identities merge into one
        // principal. A side effect is that an anonymous request is challenged on both
        // handlers, and each writes its own redirect - whichever runs last wins.
        // Each handler therefore answers only for its own half of the site, otherwise
        // the whole application would redirect to whichever login was registered last.
        options.Events.OnRedirectToLogin = context =>
        {
            if (IsPortalRequest(context.Request))
                return Task.CompletedTask;

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (IsPortalRequest(context.Request))
                return Task.CompletedTask;

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    })
    // Tenant self-service portal. Its own cookie, so a tenant session and a staff
    // session are entirely distinct and can even coexist in one browser.
    .AddCookie(AppAuthSchemes.TenantPortal, options =>
    {
        options.Cookie.Name = "RealEstateManager.TenantPortal";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/portal/login";
        options.LogoutPath = "/portal/logout";
        options.AccessDeniedPath = "/portal/login";
        options.SlidingExpiration = true;
        // Shorter than staff: tenants sign in from personal phones.
        options.ExpireTimeSpan = TimeSpan.FromHours(4);

        // Mirror of the staff handler: only answer for /portal, so a request to the
        // staff application is never redirected to the tenant login.
        options.Events.OnRedirectToLogin = context =>
        {
            if (!IsPortalRequest(context.Request))
                return Task.CompletedTask;

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (!IsPortalRequest(context.Request))
                return Task.CompletedTask;

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

static bool IsPortalRequest(HttpRequest request) =>
    request.Path.StartsWithSegments("/portal", StringComparison.OrdinalIgnoreCase);

builder.Services.AddAuthorization();

builder.Services.AddControllersWithViews(options =>
{
    // The global requirement accepts EITHER scheme, so a signed-in tenant is not
    // bounced to the staff login. PortalBoundaryFilter is what then keeps each side
    // in its own half of the application.
    options.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AppAuthSchemes.TenantPortal)
        .RequireAuthenticatedUser()
        .Build()));

    // Must run before the staff filters: those assume a staff principal, and a
    // tenant has no company permissions to check.
    options.Filters.AddService<PortalBoundaryFilter>();
    options.Filters.AddService<SubscriptionEnforcementFilter>();
    options.Filters.AddService<PermissionEnforcementFilter>();
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<VatDeterminationService>();
builder.Services.AddScoped<VatReturnService>();
builder.Services.AddScoped<OwnerReportService>();
builder.Services.AddScoped<CashFlowService>();
builder.Services.AddScoped<CodeGenerationService>();
builder.Services.AddScoped<MasterDataUniquenessService>();
builder.Services.AddScoped<VendorBillService>();
builder.Services.AddScoped<ContractQrService>();
builder.Services.AddScoped<AccountingPostingService>();
builder.Services.AddScoped<OpeningBalanceService>();
builder.Services.AddScoped<FinancialReportService>();
builder.Services.AddScoped<ManagementReportService>();
builder.Services.AddScoped<LedgerInquiryService>();
builder.Services.AddScoped<ManualJournalService>();
builder.Services.AddScoped<JournalWorkflowService>();
builder.Services.AddScoped<PettyCashService>();
builder.Services.AddScoped<PdcService>();
builder.Services.AddScoped<TenantStatementService>();
builder.Services.AddScoped<FixedAssetService>();

var app = builder.Build();

// Seeding must never prevent the host from starting. An unhandled exception here
// happens before the request pipeline exists and surfaces as HTTP 500.30, which
// leaves no UI from which to diagnose or fix the problem. Log and continue.
await RunSeederAsync("ChartOfAccountsSeeder", () => ChartOfAccountsSeeder.SeedAsync(app.Services));
await RunSeederAsync("AppSecuritySeeder", () => AppSecuritySeeder.SeedAsync(app.Services));

async Task RunSeederAsync(string name, Func<Task> seeder)
{
    try
    {
        await seeder();
    }
    catch (Exception ex)
    {
        app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup")
            .LogError(ex, "{Seeder} failed during startup. The application will still start.", name);
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<AuditLoggingMiddleware>();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static string ResolveConnectionString(WebApplicationBuilder builder)
{
    var environmentName = builder.Environment.EnvironmentName;
    var searchedPaths = new List<string>();
    var candidates = new List<string>();

    void AddCandidate(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            candidates.Add(value);
        }
    }

    AddCandidate(builder.Configuration.GetConnectionString("DefaultConnection"));
    AddCandidate(builder.Configuration["ConnectionStrings:DefaultConnection"]);

    var basePaths = new[]
    {
        builder.Environment.ContentRootPath,
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory
    }
    .Where(path => !string.IsNullOrWhiteSpace(path))
    .Distinct(StringComparer.OrdinalIgnoreCase);

    foreach (var basePath in basePaths)
    {
        if (!Directory.Exists(basePath))
        {
            continue;
        }

        searchedPaths.Add(basePath);

        var fallbackConfiguration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        AddCandidate(fallbackConfiguration.GetConnectionString("DefaultConnection"));
        AddCandidate(fallbackConfiguration["ConnectionStrings:DefaultConnection"]);
    }

    foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
    {
        try
        {
            var sqlConnectionStringBuilder = new SqlConnectionStringBuilder(candidate);
            if (!string.IsNullOrWhiteSpace(sqlConnectionStringBuilder.DataSource) &&
                !string.IsNullOrWhiteSpace(sqlConnectionStringBuilder.InitialCatalog))
            {
                return candidate;
            }
        }
        catch (ArgumentException)
        {
            // Ignore malformed candidates and continue probing other config sources.
        }
    }

    var checkedLocations = searchedPaths.Count == 0
        ? "no valid base paths"
        : string.Join(", ", searchedPaths);

    throw new InvalidOperationException(
        $"DefaultConnection is not configured correctly. Checked active configuration and appsettings files under: {checkedLocations}.");
}
