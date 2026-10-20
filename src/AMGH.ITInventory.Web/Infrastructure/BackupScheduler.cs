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
    private readonly BackupAlerts _alerts;

    public BackupScheduler(IServiceScopeFactory scopes, BackupScheduleStore store, BackupAlerts alerts, ILogger<BackupScheduler> logger)
    {
        _alerts = alerts;
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
                var problems = new List<string>();
                var check = await backups.VerifyAsync(tenant.Code, result.Value.FileName, ct);
                if (check.IsFailure || !check.Value.VerifyOk)
                    problems.Add("the backup " + result.Value.FileName + " did not pass its test restore" + (check.IsFailure ? " (the check could not run: " + check.FirstError!.Message + ")" : ": " + check.Value.VerifyNote));
                var offsite = "no off-site copy configured";
                if (backups.OffsiteConfigured)
                {
                    var copy = await backups.CopyOffsiteAsync(tenant.Code, result.Value.FileName, schedule.Keep, ct);
                    var ok = copy.IsSuccess && copy.Value.OffsiteOk;
                    offsite = ok ? "copied off-site" : "OFF-SITE COPY FAILED";
                    if (!ok) problems.Add("the off-site copy of " + result.Value.FileName + " failed: " + (copy.IsFailure ? copy.FirstError!.Message : copy.Value.OffsiteNote));
                }
                var removed = await backups.DeleteOldScheduledAsync(tenant.Code, schedule.Keep, ct);
                var checkOk = check.IsSuccess && check.Value.VerifyOk;
                if (checkOk) schedule.LastSuccessLocal = DateTime.Now;
                schedule.LastResult = (problems.Count == 0 ? "OK" : "FAILED") + $" ({(checkOk ? "checked" : "CHECK FAILED")}, {offsite}): {result.Value.FileName}" + (removed > 0 ? $" ({removed} old backup(s) removed)" : string.Empty);
                _logger.LogInformation("Scheduled backup of {Code} made: {File}", tenant.Code, result.Value.FileName);
                foreach (var problem in problems) await _alerts.SendAsync(tenant.Code, problem, ct);
            }
            else
            {
                schedule.LastResult = "FAILED: " + result.FirstError!.Message;
                await _alerts.SendAsync(tenant.Code, "the scheduled backup failed: " + result.FirstError!.Message, ct);
            }
            await _store.SaveAsync(tenant.Code, schedule, ct);
        }
    }
}
