using AMGH.ITInventory.Application.Tenancy;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>On start-up brings every company database to the installed version's schema. A failure is logged per company and never stops the site.</summary>
internal sealed partial class TenantUpgradeService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<TenantUpgradeService> _logger;

    public TenantUpgradeService(IServiceScopeFactory scopes, ILogger<TenantUpgradeService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch (OperationCanceledException) { return; }
        try
        {
            using var scope = _scopes.CreateScope();
            var status = await scope.ServiceProvider.GetRequiredService<ITenantMigrationService>().UpgradeAllAsync(stoppingToken);
            foreach (var s in status.Where(s => !s.UpToDate)) LogNotUpToDate(s.Code, s.Problem ?? s.Pending + " change(s) pending");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { LogFailed(ex); }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Company {Code} is not up to date: {Reason}")] private partial void LogNotUpToDate(string code, string reason);
    [LoggerMessage(Level = LogLevel.Error, Message = "The start-up upgrade of the company databases failed")] private partial void LogFailed(Exception ex);
}
