namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>
/// Adds browser security headers to every response. The Content-Security-Policy allows scripts,
/// styles and fonts only from this server, so all JavaScript lives in wwwroot/js (no inline script).
/// </summary>
internal sealed class SecurityHeadersMiddleware
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            // a response may set a stricter policy of its own (the sandboxed e-mail preview); otherwise the site policy applies
            if (!h.ContainsKey("Content-Security-Policy")) h["Content-Security-Policy"] = ContentSecurityPolicy;
            h["X-Content-Type-Options"] = "nosniff";
            if (!h.ContainsKey("X-Frame-Options")) h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            // The camera is allowed only on the physical-verification page (QR scanning), and only for this site.
            var camera = context.Request.Path.StartsWithSegments("/verification", StringComparison.OrdinalIgnoreCase) ? "camera=(self)" : "camera=()";
            h["Permissions-Policy"] = camera + ", microphone=(), geolocation=(), payment=(), usb=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            h.Remove("X-Powered-By");

            // Authenticated pages must not be stored by the browser or proxies.
            if (context.User.Identity?.IsAuthenticated == true && !context.Request.Path.StartsWithSegments("/lib"))
            {
                h.CacheControl = "no-store, no-cache, must-revalidate";
                h.Pragma = "no-cache";
            }
            return Task.CompletedTask;
        });
        return _next(context);
    }
}
