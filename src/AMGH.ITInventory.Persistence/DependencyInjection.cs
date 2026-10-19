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
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

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
}
