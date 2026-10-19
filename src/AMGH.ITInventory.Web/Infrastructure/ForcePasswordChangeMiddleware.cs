using AMGH.ITInventory.Security.Claims;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>Users with a temporary or expired password can only reach the change-password page.</summary>
internal sealed class ForcePasswordChangeMiddleware
{
    private static readonly PathString[] Allowed =
    {
        "/Account/ChangePassword", "/Account/Logout", "/Account/KeepAlive", "/Home/Error", "/Home/Status", "/health"
    };

    private readonly RequestDelegate _next;

    public ForcePasswordChangeMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.MustChangePassword()
            && !Allowed.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.Redirect("/Account/ChangePassword");
            return Task.CompletedTask;
        }
        return _next(context);
    }
}
