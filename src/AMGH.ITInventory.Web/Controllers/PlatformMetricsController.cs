using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>
/// /platform/metrics: per-company usage and health in the Prometheus text format, for monitoring tools (Prometheus, Grafana Agent, Zabbix, Datadog).
/// Needs the operator key as "Authorization: Bearer &lt;key&gt;" (or the X-Operator-Key header); answers 404 otherwise, so the address does not reveal itself.
/// </summary>
[AllowAnonymous]
[Route("platform/metrics")]
public sealed class PlatformMetricsController : Controller
{
    private readonly TenancyOptions _options;
    private readonly PlatformOverview? _overview;

    public PlatformMetricsController(IOptions<TenancyOptions> options, IServiceProvider services)
    {
        _options = options.Value;
        _overview = services.GetService<PlatformOverview>();
    }

    internal static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private bool KeyIsValid()
    {
        if (string.IsNullOrWhiteSpace(_options.OperatorKey)) return false;
        var supplied = Request.Headers["X-Operator-Key"].ToString();
        var auth = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(supplied) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) supplied = auth["Bearer ".Length..].Trim();
        return !string.IsNullOrEmpty(supplied) && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(_options.OperatorKey)));
    }

    [HttpGet("")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Index()
    {
        if (_overview is null || !KeyIsValid()) return NotFound();
        var snapshot = await _overview.GetAsync(fresh: false, HttpContext.RequestAborted);
        return Content(Render(snapshot), "text/plain; version=0.0.4; charset=utf-8");
    }

    internal static string Render(PlatformSnapshot snapshot)
    {
        var sb = new StringBuilder();
        void Header(string name, string help, string type = "gauge") => sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n').Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
        void Line(string name, string label, double value) =>
            sb.Append(name).Append("{company=\"").Append(Escape(label)).Append("\"} ").Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');

        var rows = snapshot.Companies;
        Header("eams_companies", "Number of companies");
        sb.Append("eams_companies ").Append(rows.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        Header("eams_company_active", "1 when the company can be used, 0 when suspended");
        foreach (var c in rows) Line("eams_company_active", c.Tenant.Code, c.Tenant.IsActive ? 1 : 0);
        Header("eams_company_database_bytes", "Size of the company database (data files)");
        foreach (var c in rows) Line("eams_company_database_bytes", c.Tenant.Code, c.Usage?.DatabaseBytes ?? 0);
        Header("eams_company_document_bytes", "Size of the company's uploaded documents");
        foreach (var c in rows) Line("eams_company_document_bytes", c.Tenant.Code, c.DocumentBytes);
        Header("eams_company_users", "Users of the company");
        foreach (var c in rows) Line("eams_company_users", c.Tenant.Code, c.Usage?.Users ?? 0);
        Header("eams_company_assets", "Assets of the company");
        foreach (var c in rows) Line("eams_company_assets", c.Tenant.Code, c.Usage?.Assets ?? 0);
        Header("eams_company_schema_pending", "Database changes not yet applied (0 = up to date)");
        foreach (var c in rows) Line("eams_company_schema_pending", c.Tenant.Code, c.Schema?.Pending ?? 0);
        Header("eams_company_last_backup_age_seconds", "Seconds since the newest backup (-1 when there is none)");
        foreach (var c in rows) Line("eams_company_last_backup_age_seconds", c.Tenant.Code, c.LastBackupUtc is null ? -1 : (snapshot.AtUtc - c.LastBackupUtc.Value).TotalSeconds);
        Header("eams_company_last_backup_checked", "1 when the newest backup passed its test restore, 0 when it failed, -1 when not checked");
        foreach (var c in rows) Line("eams_company_last_backup_checked", c.Tenant.Code, c.LastBackupChecked is null ? -1 : c.LastBackupChecked.Value ? 1 : 0);
        Header("eams_company_warnings", "Number of open warnings for the company");
        foreach (var c in rows) Line("eams_company_warnings", c.Tenant.Code, c.Warnings.Count);
        Header("eams_disk_free_bytes", "Free space of a disk used by the platform");
        foreach (var d in snapshot.Disks) sb.Append("eams_disk_free_bytes{volume=\"").Append(Escape(d.Label)).Append("\"} ").Append(d.FreeBytes.ToString(CultureInfo.InvariantCulture)).Append('\n');
        return sb.ToString();
    }
}
