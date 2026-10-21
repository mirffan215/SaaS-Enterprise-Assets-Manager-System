using System.Net.Http.Json;
using AMGH.ITInventory.Application.Tenancy;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>Tells the operator when a backup problem happens: always in the log, and as a JSON message {"text": ...} to Tenancy:AlertWebhookUrl when set.</summary>
internal sealed class BackupAlerts
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly TenancyOptions _options;
    private readonly ILogger<BackupAlerts> _logger;

    public BackupAlerts(IOptions<TenancyOptions> options, ILogger<BackupAlerts> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string company, string problem, CancellationToken ct)
    {
        _logger.LogError("BACKUP ALERT for {Company}: {Problem}", company, problem);
        if (string.IsNullOrWhiteSpace(_options.AlertWebhookUrl)) return;
        if (!Uri.TryCreate(_options.AlertWebhookUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.LogWarning("Tenancy:AlertWebhookUrl must be an https address; no alert was sent");
            return;
        }
        try
        {
            using var response = await Http.PostAsJsonAsync(uri, new { text = $"[{Environment.MachineName}] Backup problem for company {company}: {problem}" }, ct);
            if (!response.IsSuccessStatusCode) _logger.LogWarning("The alert webhook answered HTTP {Status}", (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { _logger.LogWarning(ex, "The alert could not be delivered to the webhook"); }
    }
}
