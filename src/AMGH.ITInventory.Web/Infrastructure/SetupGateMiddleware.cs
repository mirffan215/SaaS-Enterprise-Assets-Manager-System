using AMGH.ITInventory.Application.Tenancy;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>
/// Single-database hosting with a "Setup:Key": until the database has been set up, every page leads to /Setup. Once it is set up the check is never
/// made again for the life of the process.
/// </summary>
internal sealed class SetupGateMiddleware
{
    private static readonly PathString[] Open =
    {
        "/Setup", "/lib", "/css", "/js", "/images", "/favicon", "/health", "/manifest.webmanifest", "/sw.js", "/offline.html", "/branding/default-logo.svg"
    };
    private static volatile bool _initialized;
    private readonly RequestDelegate _next;

    public SetupGateMiddleware(RequestDelegate next) => _next = next;

    internal static void MarkInitialized() => _initialized = true;

    public async Task InvokeAsync(HttpContext context, ISingleDatabaseSetup setup, IConfiguration configuration)
    {
        if (_initialized || Open.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase))) { await _next(context); return; }
        var connection = configuration.GetConnectionString("AMGHDatabase");
        try
        {
            if (!string.IsNullOrWhiteSpace(connection) && await setup.IsInitializedAsync(connection, context.RequestAborted)) { _initialized = true; await _next(context); return; }
        }
        catch (Microsoft.Data.SqlClient.SqlException) { /* the database cannot be reached: the setup page says so */ }
        if (HttpMethods.IsGet(context.Request.Method)) { context.Response.Redirect("/Setup"); return; }
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    }
}
