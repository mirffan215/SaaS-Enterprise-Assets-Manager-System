using AMGH.ITInventory.Application.Tenancy;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>Builds the address of a tenant's workspace: its sub-domain when a base domain is configured, else the ?tenant= parameter (development).</summary>
internal static class TenantUrls
{
    internal static string For(HttpRequest request, TenancyOptions options, string tenantCode, string path = "/")
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        if (!string.IsNullOrWhiteSpace(options.BaseDomain) && !options.AllowQueryParameter)
        {
            var host = $"{tenantCode}.{options.BaseDomain.Trim().TrimStart('.')}";
            var port = request.Host.Port is { } p and not 80 and not 443 ? ":" + p : string.Empty;
            return $"{request.Scheme}://{host}{port}{path}";
        }
        var separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{path}{separator}{TenantResolutionMiddleware.QueryName}={Uri.EscapeDataString(tenantCode)}";
    }
}
