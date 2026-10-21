using AMGH.ITInventory.Shared.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>
/// Protects the sandbox: on destructive or security-critical actions (changing roles, the administrator's password,
/// mail settings, company status, numbering) the demo tenant answers "not available in the demo" instead of acting.
/// Other tenants are unaffected.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class BlockInDemoAttribute : ActionFilterAttribute
{
    public const string Message = "This action is disabled in the demo environment. It is available in your own company workspace.";

    public override Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var tenants = context.HttpContext.RequestServices.GetService<ITenantAccessor>();
        if (tenants?.CurrentTenant is not { IsDemo: true }) return next();

        var request = context.HttpContext.Request;
        if (string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new ObjectResult(new { error = Message }) { StatusCode = StatusCodes.Status403Forbidden };
            return Task.CompletedTask;
        }
        if (context.Controller is Microsoft.AspNetCore.Mvc.Controller controller) controller.TempData["ErrorMessage"] = Message;
        var back = request.Headers.Referer.ToString();
        context.Result = Uri.TryCreate(back, UriKind.Absolute, out var uri) && string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
            ? new RedirectResult(uri.PathAndQuery)
            : new RedirectToActionResult("Index", "Home", null);
        return Task.CompletedTask;
    }
}
