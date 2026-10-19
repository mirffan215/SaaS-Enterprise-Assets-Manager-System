using AMGH.ITInventory.Application.Tenancy;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>
/// Creates the sandbox tenant "demo" (its own database, sample estate, administrator "admin") the first time the site
/// starts with "Tenancy:DemoEnabled" and "Tenancy:DemoAdminPassword" set. Does nothing when the tenant exists.
/// </summary>
internal sealed class DemoTenantSeeder : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TenancyOptions _options;
    private readonly ILogger<DemoTenantSeeder> _logger;

    public DemoTenantSeeder(IServiceScopeFactory scopes, IOptions<TenancyOptions> options, ILogger<DemoTenantSeeder> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.DemoAdminPassword))
        {
            _logger.LogWarning("Tenancy:DemoEnabled is set but Tenancy:DemoAdminPassword is empty: the demo tenant is not created");
            return;
        }
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); // let the catalog be initialised first
            using var scope = _scopes.CreateScope();
            var catalog = scope.ServiceProvider.GetRequiredService<ITenantCatalog>();
            if (await catalog.ExistsAsync(TenancyOptions.DemoCode, stoppingToken)) return;

            var provisioning = scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>();
            var result = await provisioning.ProvisionTenantAsync(TenancyOptions.DemoCode, "Demo Company", "demo.admin@example.invalid",
                _options.DemoAdminPassword, isDemo: true, stoppingToken);
            if (result.IsFailure) _logger.LogError("The demo tenant could not be created: {Error}", result.FirstError?.Message);
            else _logger.LogInformation("Demo tenant created");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The demo tenant could not be created");
        }
    }
}
