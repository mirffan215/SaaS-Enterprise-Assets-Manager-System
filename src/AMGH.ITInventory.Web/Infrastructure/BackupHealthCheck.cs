using AMGH.ITInventory.Application.Tenancy;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>
/// /health/backups: Healthy when every company with a schedule has a recent, checked backup; Unhealthy (HTTP 503) when the last scheduled run failed or
/// a backup is more than six hours overdue. The response carries no details; the company and reason are in the log.
/// </summary>
internal sealed class BackupHealthCheck : IHealthCheck
{
    internal static readonly TimeSpan Grace = TimeSpan.FromHours(6);
    private readonly IServiceScopeFactory _scopes;
    private readonly BackupScheduleStore _store;
    private readonly ILogger<BackupHealthCheck> _logger;

    public BackupHealthCheck(IServiceScopeFactory scopes, BackupScheduleStore store, ILogger<BackupHealthCheck> logger)
    {
        _scopes = scopes;
        _store = store;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var catalog = scope.ServiceProvider.GetService<ITenantCatalog>();
        if (catalog is null) return HealthCheckResult.Healthy();
        var problems = 0;
        foreach (var tenant in (await catalog.ListAsync(cancellationToken)).Where(t => t.IsActive && !t.IsDemo))
        {
            var schedule = await _store.LoadAsync(tenant.Code, cancellationToken);
            if (schedule.Mode == BackupSchedule.Off) continue;
            if (schedule.LastResult?.Contains("FAILED", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.LogError("Backup health: the last scheduled backup of {Code} failed: {Result}", tenant.Code, schedule.LastResult);
                problems++;
            }
            else if (schedule.IsOverdue(DateTime.Now, Grace))
            {
                _logger.LogError("Backup health: the scheduled backup of {Code} is overdue (last success {Last})", tenant.Code, schedule.LastSuccessLocal);
                problems++;
            }
        }
        return problems == 0 ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
    }
}
