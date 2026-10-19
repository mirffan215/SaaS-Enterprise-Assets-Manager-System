using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Shared.Abstractions;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>
/// Database per tenant: finds the customer a request belongs to and points the data layer at that customer's database.
/// The tenant comes from, in this order: the sub-domain ({tenant}.base-domain), the <c>?tenant=code</c> query
/// parameter (local development only, "Tenancy:AllowQueryParameter"), the <c>X-Tenant-Code</c> header, and the
/// tenant cookie that remembers the last query/picker choice. Only active tenants of the catalog resolve; the signed-in
/// session is bound to its tenant separately (cookie claim, checked in <see cref="AppCookieEvents"/>).
/// Only registered in multi-tenant mode ("Tenancy:Enabled").
/// </summary>
internal sealed class TenantResolutionMiddleware
{
    public const string HeaderName = "X-Tenant-Code";
    public const string QueryName = "tenant";
    internal const string TenantCookie = "EAMS.Tenant";

    private static readonly string[] PublicPaths = { "/health", "/tenant", "/account/exploredemo", "/branding/default-logo.svg", "/favicon" };

    private readonly RequestDelegate _next;
    private readonly TenancyOptions _options;

    public TenantResolutionMiddleware(RequestDelegate next, IOptions<TenancyOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context, ITenantAccessor accessor, ITenantCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (code, fromQuery) = Resolve(context.Request);
        if (code is null)
        {
            if (IsPublic(context.Request.Path) || accessor is not TenantAccessor { } ) { await _next(context); return; }
            await NoTenantAsync(context);
            return;
        }

        var tenant = await catalog.FindActiveAsync(code, context.RequestAborted);
        if (tenant is null || accessor is not TenantAccessor settable)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Unknown company.", context.RequestAborted);
            return;
        }

        settable.Set(tenant);
        if (fromQuery && !string.Equals(context.Request.Cookies[TenantCookie], tenant.Code, StringComparison.Ordinal))
            context.Response.Cookies.Append(TenantCookie, tenant.Code, new CookieOptions
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, IsEssential = true
            });
        await _next(context);
    }

    /// <summary>The tenant code the request names, and whether it came from the query string.</summary>
    internal (string? Code, bool FromQuery) Resolve(HttpRequest request)
    {
        if (SubdomainOf(request.Host.Host) is { } sub) return (sub, false);
        if (_options.AllowQueryParameter && request.Query[QueryName].ToString() is { Length: > 0 } q) return (q, true);
        if (request.Headers[HeaderName].ToString() is { Length: > 0 } h) return (h, false);
        if (request.Cookies[TenantCookie] is { Length: > 0 } c) return (c, false);
        return (null, false);
    }

    /// <summary>"acme" for acme.example.com when the base domain is example.com; null for the base domain itself, "www" or other hosts.</summary>
    internal string? SubdomainOf(string host)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseDomain)) return null;
        var suffix = "." + _options.BaseDomain.Trim().TrimStart('.');
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        var label = host[..^suffix.Length];
        return label.Length == 0 || label.Contains('.', StringComparison.Ordinal) || label.Equals("www", StringComparison.OrdinalIgnoreCase) ? null : label;
    }

    private static bool IsPublic(PathString path) => PublicPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    private static Task NoTenantAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) && context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect("/Tenant/Select");
            return Task.CompletedTask;
        }
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return context.Response.WriteAsync("Specify the company (X-Tenant-Code header or company sub-domain).", context.RequestAborted);
    }
}
