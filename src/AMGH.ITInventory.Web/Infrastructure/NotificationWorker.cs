using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Notifications;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Shared.Abstractions;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>
/// Background sender: every minute it sends due e-mails for each active company; once a day (from 06:00 server time) it
/// runs the scheduled reminders. Each company is processed in its own scope and company context, so Row-Level Security
/// applies exactly as for a signed-in user. Changes are recorded as SYSTEM.
/// </summary>
internal sealed class NotificationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<NotificationWorker> _logger;

    public NotificationWorker(IServiceScopeFactory scopes, ILogger<NotificationWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); // let the site start first
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Notification run failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        List<int> companies;
        using (var scope = _scopes.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ICompanyContext>();
            using var platform = context.EnterPlatform();
            companies = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Repository<Company>()
                .QueryAsync(q => q.Where(c => c.Status == AMGH.ITInventory.Domain.Entities.CompanyStatuses.Active).Select(c => c.CompanyId), ct);
        }
        var runReminders = DateTime.Now.Hour >= 6;
        foreach (var id in companies)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                using var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>().Enter(id);
                if (runReminders)
                {
                    var queued = await scope.ServiceProvider.GetRequiredService<INotificationScheduler>().RunDailyAsync(false, ct);
                    if (queued > 0) _logger.LogInformation("Company {CompanyId}: {Count} reminder(s) created", id, queued);
                }
                var (sent, failed, retrying) = await scope.ServiceProvider.GetRequiredService<IMailQueue>().ProcessAsync(50, ct);
                if (sent + failed + retrying > 0)
                    _logger.LogInformation("Company {CompanyId}: e-mail sent {Sent}, failed {Failed}, retrying {Retrying}", id, sent, failed, retrying);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Notifications for company {CompanyId} failed; other companies continue", id);
            }
        }
    }
}
