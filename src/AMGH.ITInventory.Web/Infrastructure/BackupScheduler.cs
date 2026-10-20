using AMGH.ITInventory.Application.Tenancy;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>
/// Makes the scheduled (daily / weekly) backups: every minute it looks at each workspace's schedule and, when a backup is due,
/// makes it and deletes scheduled backups beyond the number to keep. The demo workspace is not backed up.
/// </summary>
internal sealed class BackupScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly BackupScheduleStore _store;
    private readonly ILogger<BackupScheduler> _logger;

    public BackupScheduler(IServiceScopeFactory scopes, BackupScheduleStore store, ILogger<BackupScheduler> logger)
    {
        _scopes = scopes;
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); } catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Backup scheduler run failed"); }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); } catch (OperationCanceledException) { return false; }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var catalog = scope.ServiceProvider.GetService<ITenantCatalog>();
        if (catalog is null) return;
        var backups = scope.ServiceProvider.GetRequiredService<ITenantBackupService>();
        foreach (var tenant in (await catalog.ListAsync(ct)).Where(t => t.IsActive && !t.IsDemo))
        {
            var schedule = await _store.LoadAsync(tenant.Code, ct);
            var now = DateTime.Now;
            if (!schedule.IsDue(now)) continue;

            // mark first: a slow or failing backup must not be started again every minute
            schedule.LastRunLocal = now;
            schedule.LastResult = "Running…";
            await _store.SaveAsync(tenant.Code, schedule, ct);
            var result = await backups.BackupAsync(tenant.Code, scheduled: true, cancellationToken: ct);
            if (result.IsSuccess)
            {
                var check = await backups.VerifyAsync(tenant.Code, result.Value.FileName, ct);
                var checkNote = check.IsFailure ? "check could not run" : check.Value.VerifyOk ? "checked" : "FAILED ITS CHECK";
                if (check.IsFailure || !check.Value.VerifyOk) _logger.LogError("Scheduled backup {File} of {Code} did not pass its test restore", result.Value.FileName, tenant.Code);
                var removed = await backups.DeleteOldScheduledAsync(tenant.Code, schedule.Keep, ct);
                schedule.LastResult = $"OK ({checkNote}): {result.Value.FileName}" + (removed > 0 ? $" ({removed} old backup(s) removed)" : string.Empty);
                _logger.LogInformation("Scheduled backup of {Code} made: {File}", tenant.Code, result.Value.FileName);
            }
            else
            {
                schedule.LastResult = "FAILED: " + result.FirstError!.Message;
                _logger.LogError("Scheduled backup of {Code} failed: {Error}", tenant.Code, result.FirstError!.Message);
            }
            await _store.SaveAsync(tenant.Code, schedule, ct);
        }
    }
}
