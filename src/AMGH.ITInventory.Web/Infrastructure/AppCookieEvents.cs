using System.Globalization;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Security;
using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>
/// Enforces the absolute session limit and re-checks the user against the database every few
/// minutes, so disabling a user, changing roles or resetting a password ends existing sessions.
/// </summary>
internal sealed class AppCookieEvents : CookieAuthenticationEvents
{
    private const string LastValidatedKey = ".amgh.validated";

    private readonly IAccountService _accounts;
    private readonly IDateTimeProvider _clock;
    private readonly SecurityOptions _options;
    private readonly ILogger<AppCookieEvents> _logger;
    private readonly ITenantAccessor _tenants;

    public AppCookieEvents(IAccountService accounts, IDateTimeProvider clock, IOptions<SecurityOptions> options,
        ILogger<AppCookieEvents> logger, ITenantAccessor tenants)
    {
        _tenants = tenants;
        _accounts = accounts;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (principal?.Identity?.IsAuthenticated != true) return;

        var now = _clock.UtcNow;
        var userId = principal.GetUserId();
        var stamp = principal.GetSecurityStamp();
        var authTime = principal.GetAuthTimeUtc();
        var companyId = principal.GetCompanyId();
        // Cookies issued before Release 2.0 carry no company: they are rejected and the user signs in again.
        if (userId is null || stamp is null || authTime is null || companyId is null)
        {
            await RejectAsync(context, "Invalid session cookie");
            return;
        }

        // A session is bound to the tenant it was issued for: it is never valid in another customer's workspace.
        if (_tenants.IsMultiTenant && !string.Equals(principal.GetTenantCode(), _tenants.CurrentTenant?.Code, StringComparison.OrdinalIgnoreCase))
        {
            await RejectAsync(context, "Session belongs to another company");
            return;
        }

        if (now - authTime.Value > TimeSpan.FromHours(_options.SessionAbsoluteTimeoutHours))
        {
            await RejectAsync(context, "Absolute session limit reached");
            return;
        }

        var lastValidated = context.Properties.Items.TryGetValue(LastValidatedKey, out var raw)
                            && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
            ? new DateTime(ticks, DateTimeKind.Utc)
            : authTime.Value;
        if (now - lastValidated < TimeSpan.FromMinutes(_options.SecurityStampValidationMinutes)) return;

        if (!await _accounts.ValidateSecurityStampAsync(userId.Value, companyId.Value, stamp.Value, context.HttpContext.RequestAborted))
        {
            await RejectAsync(context, "Account or company access changed (disabled, roles, password or membership)");
            return;
        }

        context.Properties.Items[LastValidatedKey] = now.Ticks.ToString(CultureInfo.InvariantCulture);
        context.ShouldRenew = true;
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (IsAjax(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        return base.RedirectToLogin(context);
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (IsAjax(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        return base.RedirectToAccessDenied(context);
    }

    private async Task RejectAsync(CookieValidatePrincipalContext context, string reason)
    {
        var principal = context.Principal!;
        _logger.LogInformation("Session ended for {User}: {Reason}", principal.Identity?.Name, reason);
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await _accounts.LogEventAsync(principal.GetUserId(), principal.GetCompanyId(), principal.Identity?.Name ?? "(unknown)", LoginEventType.SessionExpired,
            context.HttpContext.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString(), reason);
    }

    private static bool IsAjax(HttpRequest request) =>
        string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
}
