using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Security.Platform;
using AMGH.ITInventory.Shared.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>A person whose company requires two-step verification, and who has not set it up, can only reach the setup page.</summary>
internal sealed class ForceMfaSetupMiddleware
{
    private static readonly PathString[] Allowed =
    {
        "/Account/MfaSetup", "/Account/Logout", "/Account/KeepAlive", "/Account/ChangePassword", "/Home/Error", "/Home/Status", "/health", "/lib", "/css", "/js", "/favicon", "/branding"
    };

    private readonly RequestDelegate _next;

    public ForceMfaSetupMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true && context.User.MfaSetupRequired()
            && !Allowed.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.Redirect("/Account/MfaSetup");
            return Task.CompletedTask;
        }
        return _next(context);
    }
}

/// <summary>
/// Applies the company's address list to every request of a signed-in session, so a list saved today also ends the sessions that are
/// already open from other networks. The list is cached for a minute per company.
/// </summary>
internal sealed class IpAllowListMiddleware
{
    private static readonly PathString[] Exempt = { "/Account/Logout", "/health", "/lib", "/css", "/js", "/favicon" };
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(1);
    private readonly RequestDelegate _next;

    public IpAllowListMiddleware(RequestDelegate next) => _next = next;

    public static string CacheKey(string? tenantCode, int companyId) => $"ipallow:{tenantCode}:{companyId}";

    public async Task InvokeAsync(HttpContext context, IMemoryCache cache, ITenantAccessor tenants)
    {
        if (context.User.Identity?.IsAuthenticated == true && context.User.GetCompanyId() is { } companyId
            && !Exempt.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            var key = CacheKey(tenants.CurrentTenant?.Code, companyId);
            if (!cache.TryGetValue(key, out string? ranges))
            {
                ranges = (await context.RequestServices.GetRequiredService<ICompanySecurityService>().GetAsync(companyId, context.RequestAborted))?.AllowedIpRanges;
                cache.Set(key, ranges, CacheTime);
            }
            if (!IpRules.Allows(context.Connection.RemoteIpAddress?.ToString(), ranges))
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync("<!doctype html><meta charset=utf-8><title>Not allowed</title><body style=\"font-family:system-ui,sans-serif;max-width:34rem;margin:15vh auto;padding:0 1rem\">" +
                    "<h1>Access from this network is not allowed</h1><p>Your company only allows access from approved networks. Connect from the office network or VPN, or contact your administrator.</p></body>", context.RequestAborted);
                return;
            }
        }
        await _next(context);
    }
}
