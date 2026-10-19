using AMGH.ITInventory.Application.Abstractions.Lifecycle;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Persistence.Procedures;
using AMGH.ITInventory.Persistence.Repositories;
using AMGH.ITInventory.Shared.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AMGH.ITInventory.Persistence;

public static class DependencyInjection
{
    public const string ConnectionStringName = "AMGHDatabase";

    /// <summary>
    /// Registers the DbContext, repositories, unit of work and stored-procedure services.
    /// The host must also register <see cref="ICurrentUserService"/> and should register <see cref="ICompanyContext"/>
    /// (the Web project reads the company from the signed-in session).
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration,
        bool enableSensitiveDataLogging = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var tenancy = configuration.GetSection(Application.Tenancy.TenancyOptions.SectionName).Get<Application.Tenancy.TenancyOptions>() ?? new();
        // Multi-tenant mode needs no default database: every request or job names its tenant.
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? (tenancy.Enabled ? "Server=.;Database=ITInv_NoTenant;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True"
                : throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured."));

        services.AddTenancy(configuration, tenancy);
        services.TryAddScoped<ICompanyContext>(_ => new CompanyContext());
        services.AddScoped<CompanySessionInterceptor>();
        services.AddDbContext<AMGHDbContext>((provider, options) =>
        {
            options.AddInterceptors(provider.GetRequiredService<CompanySessionInterceptor>());
            options.UseSqlServer(connectionString, sql =>
            {
                sql.CommandTimeout(60);
                sql.MigrationsAssembly(typeof(AMGHDbContext).Assembly.FullName);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "dbo");
            });
            // Lookups are intentionally unfiltered; filtered dependents with required navigations are expected.
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
            if (enableSensitiveDataLogging) options.EnableSensitiveDataLogging();
        });

        services.TryAddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<StoredProcedureExecutor>();
        services.AddScoped<IAssetNumberGenerator, AssetNumberGenerator>();
        services.AddScoped<Application.Documents.IDocumentCounter, DocumentCounter>();
        services.AddScoped<IAssetLifecycleProcedures, AssetLifecycleProcedures>();
        services.AddScoped<ILicenseKeyStore, LicenseKeyStore>();
        return services;
    }

    /// <summary>
    /// Tenant accessor (always) and, when "Tenancy:Enabled" is true, the central catalog (connection string "TenantCatalog").
    /// The host sets the active tenant per request (TenantResolutionMiddleware) or per job (<see cref="ITenantAccessor.Enter"/>).
    /// </summary>
    private static IServiceCollection AddTenancy(this IServiceCollection services, IConfiguration configuration, Application.Tenancy.TenancyOptions tenancy)
    {
        services.AddOptions<Application.Tenancy.TenancyOptions>().Bind(configuration.GetSection(Application.Tenancy.TenancyOptions.SectionName));
        services.AddMemoryCache();
        services.AddScoped<TenantAccessor>(_ => new TenantAccessor(tenancy.Enabled));
        services.AddScoped<ITenantAccessor>(sp => sp.GetRequiredService<TenantAccessor>());
        if (!tenancy.Enabled) return services;

        var catalogConnection = configuration.GetConnectionString("TenantCatalog")
            ?? throw new InvalidOperationException("Tenancy is enabled but connection string 'TenantCatalog' is not configured.");
        services.AddDbContext<TenantCatalogDbContext>(o => o.UseSqlServer(catalogConnection, sql => sql.CommandTimeout(30)));
        services.AddScoped<Application.Tenancy.ITenantCatalog, Tenancy.TenantCatalog>();
        return services;
    }
}
