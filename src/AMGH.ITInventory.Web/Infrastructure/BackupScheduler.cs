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
    private readonly BackgroundJobs _jobs;
    private readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);

    public BackupScheduler(IServiceScopeFactory scopes, BackupScheduleStore store, BackupAlerts alerts, BackgroundJobs jobs, ILogger<BackupScheduler> logger)
    {
        _jobs = jobs;
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
        const string Job = "Backup scheduler";
        _jobs.Started(Job, "Checks every minute whether a company's scheduled backup is due, makes it, test-restores it and copies it off-site.", TimeSpan.FromMinutes(1));
        var made = 0;
        var failed = 0;
        try
        {
            using var scope = _scopes.CreateScope();
            var catalog = scope.ServiceProvider.GetService<ITenantCatalog>();
            if (catalog is null) return;
            var backups = scope.ServiceProvider.GetRequiredService<ITenantBackupService>();
            foreach (var tenant in (await catalog.ListAsync(ct)).Where(t => t.IsActive && !t.IsDemo))
            {
                var schedule = await _store.LoadAsync(tenant.Code, ct);
                var now = DateTime.Now;
                if (schedule.RunningSinceLocal is { } since && !_running.Contains(tenant.Code))
                {
                    // the site was stopped or restarted in the middle of a backup: say so, and let the schedule try again
                    schedule.AddRun(new BackupRun { StartedLocal = since, FinishedLocal = now, Kind = "Scheduled", Ok = false, Summary = "Interrupted: the site was restarted while this backup was running." });
                    schedule.RunningSinceLocal = null;
                    schedule.LastResult = "FAILED: interrupted by a restart of the site";
                    schedule.LastRunLocal = null;
                    await _store.SaveAsync(tenant.Code, schedule, ct);
                }
                if (!schedule.IsDue(now)) continue;

                // mark first: a slow or failing backup must not be started again every minute
                _running.Add(tenant.Code);
                schedule.LastRunLocal = now;
                schedule.RunningSinceLocal = now;
                schedule.LastResult = "Running…";
                await _store.SaveAsync(tenant.Code, schedule, ct);
                var summary = string.Empty;
                var ok = false;
                try
                {
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
                            var copied = copy.IsSuccess && copy.Value.OffsiteOk;
                            offsite = copied ? "copied off-site" : "OFF-SITE COPY FAILED";
                            if (!copied) problems.Add("the off-site copy of " + result.Value.FileName + " failed: " + (copy.IsFailure ? copy.FirstError!.Message : copy.Value.OffsiteNote));
                        }
                        var removed = await backups.DeleteOldScheduledAsync(tenant.Code, schedule.Keep, ct);
                        var checkOk = check.IsSuccess && check.Value.VerifyOk;
                        if (checkOk) schedule.LastSuccessLocal = DateTime.Now;
                        ok = problems.Count == 0;
                        summary = $"{result.Value.FileName} ({(checkOk ? "checked" : "CHECK FAILED")}, {offsite})" + (removed > 0 ? $"; {removed} old backup(s) removed" : string.Empty);
                        schedule.LastResult = (ok ? "OK" : "FAILED") + ": " + summary;
                        _logger.LogInformation("Scheduled backup of {Code} made: {File}", tenant.Code, result.Value.FileName);
                        foreach (var problem in problems) await _alerts.SendAsync(tenant.Code, problem, ct);
                    }
                    else
                    {
                        summary = result.FirstError!.Message;
                        schedule.LastResult = "FAILED: " + summary;
                        await _alerts.SendAsync(tenant.Code, "the scheduled backup failed: " + summary, ct);
                    }
                }
                finally
                {
                    _running.Remove(tenant.Code);
                    schedule.RunningSinceLocal = null;
                    schedule.AddRun(new BackupRun { StartedLocal = now, FinishedLocal = DateTime.Now, Kind = "Scheduled", Ok = ok, Summary = string.IsNullOrEmpty(summary) ? "Stopped before it finished." : summary });
                    await _store.SaveAsync(tenant.Code, schedule, CancellationToken.None);
                }
                if (ok) made++; else failed++;
            }
        }
        finally { _jobs.Finished(Job, failed == 0, made + failed == 0 ? "Nothing was due." : $"{made} backup(s) made, {failed} failed."); }
    }
}
