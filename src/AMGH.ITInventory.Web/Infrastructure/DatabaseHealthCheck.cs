using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>
/// /health: "Healthy" when SQL Server answers within 5 seconds, otherwise "Unhealthy" (HTTP 503). The response carries no
/// details (no server names, versions or errors): the reason is written to the application log only.
/// </summary>
internal sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<DatabaseHealthCheck> _logger;
    private readonly AMGH.ITInventory.Shared.Abstractions.ITenantAccessor _tenants;
    private readonly IServiceProvider _services;

    public DatabaseHealthCheck(IUnitOfWork uow, ILogger<DatabaseHealthCheck> logger, AMGH.ITInventory.Shared.Abstractions.ITenantAccessor tenants,
        IServiceProvider services)
    {
        _tenants = tenants;
        _services = services;
        _uow = uow;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            // Multi-tenant: no tenant database is chosen on /health, so the central catalog is what is checked.
            if (_tenants.IsMultiTenant)
            {
                await _services.GetRequiredService<AMGH.ITInventory.Application.Tenancy.ITenantCatalog>().ListAsync(timeout.Token);
                return HealthCheckResult.Healthy();
            }
            // tblCompany is platform-level (not company-filtered): readable without a signed-in company
            await _uow.Repository<Company>().AnyAsync(c => c.CompanyId > 0, timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Health check: the database did not answer");
            return HealthCheckResult.Unhealthy();
        }
    }
}
