using System.Threading.RateLimiting;
using AMGH.ITInventory.Application;
using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Infrastructure;
using AMGH.ITInventory.Persistence;
using AMGH.ITInventory.Persistence.Tenancy;
using AMGH.ITInventory.Security;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // ---------------------------------------------------------------- application services
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
    // The company comes only from the authenticated session (cookie claim), never from request data.
    builder.Services.AddScoped<ICompanyContext>(sp =>
    {
        var accessor = sp.GetRequiredService<IHttpContextAccessor>();
        return new CompanyContext(
            () => accessor.HttpContext?.User is { } user ? user.GetCompanyId() : null,
            () => accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user.GetCompanyName() : null);
    });
    builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
    builder.Services.AddSingleton<IDisplayTime, DisplayTime>();
    builder.Services.Configure<TagOptions>(builder.Configuration.GetSection(TagOptions.SectionName));
    builder.Services.AddScoped<INavigation, Navigation>();
    builder.Services.AddSingleton<TenantSignInService>();
    builder.Services.AddSingleton<SignInGate>();
    builder.Services.AddSingleton<MfaPending>();
    builder.Services.AddSingleton<AMGH.ITInventory.Web.Infrastructure.Sso.SsoState>();
    builder.Services.AddSingleton<AMGH.ITInventory.Web.Infrastructure.Sso.OidcClient>();
    builder.Services.AddSingleton<OperatorSession>();
    builder.Services.AddSingleton<BackupScheduleStore>();
    builder.Services.AddSingleton<ILogoFetcher, LogoFetcher>();
    builder.Services.AddSingleton<AMGH.ITInventory.Application.Abstractions.Mail.ISecretProtector, DataProtectionSecretProtector>();
    if (!builder.Configuration.GetValue("Notifications:DisableWorker", false)) builder.Services.AddHostedService<NotificationWorker>();
    builder.Services.AddPersistence(builder.Configuration, enableSensitiveDataLogging: builder.Environment.IsDevelopment());
    builder.Services.AddSecurityServices(builder.Configuration);
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    var security = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();

    // ---------------------------------------------------------------- data protection (cookie encryption keys)
    var dataProtection = builder.Services.AddDataProtection().SetApplicationName("EAMS.Cloud");
    var keysPath = builder.Configuration["DataProtection:KeysPath"];
    if (!string.IsNullOrWhiteSpace(keysPath))
    {
        dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();
    }

    // ---------------------------------------------------------------- authentication / authorisation
    builder.Services.AddScoped<AppCookieEvents>();
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.Cookie.Name = "__Host-EAMS.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(security.SessionIdleTimeoutMinutes);
            options.SlidingExpiration = true;
            options.EventsType = typeof(AppCookieEvents);
        });

    // Every endpoint requires a signed-in user unless marked [AllowAnonymous].
    builder.Services.AddAuthorization(options =>
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

    builder.Services.AddAntiforgery(options =>
    {
        options.Cookie.Name = "__Host-EAMS.AF";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.HeaderName = "X-CSRF-TOKEN";
    });

    builder.Services.AddControllersWithViews(options =>
    {
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
        // Required fields are declared explicitly ([Required] or FluentValidation) so users see our messages.
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    });

    // Brute-force protection in addition to account lockout: 10 sign-in attempts per minute per IP.
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        // company self-registration builds a database: at most 3 per hour per address
        options.AddPolicy(RateLimitPolicies.Register, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 3, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
        options.AddPolicy(RateLimitPolicies.Login, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    });

    builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));
    builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database").AddCheck<BackupHealthCheck>("backups", tags: new[] { "backups" });
    builder.Services.AddSingleton<BackupAlerts>();
    builder.Services.AddSingleton<BackgroundJobs>();

    // ---------------------------------------------------------------- pipeline
    var tenancySettings = builder.Configuration.GetSection(TenancyOptions.SectionName).Get<TenancyOptions>() ?? new TenancyOptions();
    if (tenancySettings.Enabled && tenancySettings.DemoEnabled) builder.Services.AddHostedService<DemoTenantSeeder>();
    if (tenancySettings.Enabled) builder.Services.AddScoped<PlatformOverview>();
    if (tenancySettings.Enabled) builder.Services.AddHostedService<BackupScheduler>();
    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }
    app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");
    // Numbers and dates are entered and parsed in one fixed format regardless of the server's regional settings.
    var culture = new System.Globalization.CultureInfo("en-GB");
    app.UseRequestLocalization(new Microsoft.AspNetCore.Builder.RequestLocalizationOptions
    {
        DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(culture),
        SupportedCultures = new[] { culture },
        SupportedUICultures = new[] { culture }
    });
    app.UseHttpsRedirection();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseStaticFiles();
    app.UseSerilogRequestLogging();
    app.UseRouting();
    app.UseRateLimiter();
    // Database per tenant: the tenant is resolved before authentication, which already reads the tenant's database.
    var tenancy = app.Services.GetRequiredService<IOptions<TenancyOptions>>().Value;
    if (tenancy.Enabled)
    {
        using var scope = app.Services.CreateScope();
        try { await TenantCatalogInitializer.EnsureAsync(scope.ServiceProvider.GetRequiredService<TenantCatalogDbContext>()); }
        catch (Exception ex) { Log.Error(ex, "The tenant catalog could not be initialised"); }
        // company databases are brought up to this version BEFORE the site takes requests: the program reads columns that newer versions add
        if (tenancy.AutoUpgrade)
        {
            try
            {
                foreach (var s in (await scope.ServiceProvider.GetRequiredService<ITenantMigrationService>().UpgradeAllAsync()).Where(s => !s.UpToDate))
                    Log.Error("Company {Code} is not up to date: {Reason}", s.Code, s.Problem ?? s.Pending + " change(s) pending");
            }
            catch (Exception ex) { Log.Error(ex, "The start-up upgrade of the company databases failed"); }
        }
        app.UseMiddleware<TenantResolutionMiddleware>();
    }
    app.UseAuthentication();
    app.UseMiddleware<ForcePasswordChangeMiddleware>();
    app.UseMiddleware<IpAllowListMiddleware>();
    app.UseMiddleware<ForceMfaSetupMiddleware>();
    app.UseAuthorization();

    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = r => !r.Tags.Contains("backups") }).AllowAnonymous();
    app.MapHealthChecks("/health/backups", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = r => r.Tags.Contains("backups") }).AllowAnonymous();
    app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "EAMS (Enterprise Asset Management System) terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
