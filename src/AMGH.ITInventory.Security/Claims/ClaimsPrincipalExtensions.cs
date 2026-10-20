using System.Globalization;
using System.Security.Claims;

namespace AMGH.ITInventory.Security.Claims;

public static class ClaimsPrincipalExtensions
{
    public static int? GetUserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id : null;
    }

    public static string GetDisplayName(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.FindFirstValue(AppClaimTypes.DisplayName) ?? principal.Identity?.Name ?? string.Empty;
    }

    public static bool HasPermission(this ClaimsPrincipal principal, string permission)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.Identity?.IsAuthenticated == true && principal.HasClaim(AppClaimTypes.Permission, permission);
    }

    public static bool HasAnyPermission(this ClaimsPrincipal principal, params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return permissions.Any(principal.HasPermission);
    }

    public static bool MfaSetupRequired(this ClaimsPrincipal principal) =>
        string.Equals(principal?.FindFirst(AppClaimTypes.MfaSetupRequired)?.Value, "true", StringComparison.Ordinal);

    public static bool MustChangePassword(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return string.Equals(principal.FindFirstValue(AppClaimTypes.MustChangePassword), "true", StringComparison.Ordinal);
    }

    public static Guid? GetSecurityStamp(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return Guid.TryParse(principal.FindFirstValue(AppClaimTypes.SecurityStamp), out var g) ? g : null;
    }

    public static DateTime? GetAuthTimeUtc(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return long.TryParse(principal.FindFirstValue(AppClaimTypes.AuthTime), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
            ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime : null;
    }

    /// <summary>The session's company; null when the cookie carries none (it is then rejected at validation).</summary>
    public static int? GetCompanyId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.Identity?.IsAuthenticated == true
               && int.TryParse(principal.FindFirstValue(AppClaimTypes.CompanyId), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id : null;
    }

    /// <summary>The tenant the session was issued for, or null (single-database mode).</summary>
    public static string? GetTenantCode(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.FindFirstValue(AppClaimTypes.Tenant);
    }

    public static string GetCompanyName(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.FindFirstValue(AppClaimTypes.CompanyName) ?? string.Empty;
    }

    public static bool IsPlatformAdmin(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.Identity?.IsAuthenticated == true
               && string.Equals(principal.FindFirstValue(AppClaimTypes.PlatformAdmin), "true", StringComparison.Ordinal);
    }

    public static string GetCompanyCode(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.FindFirstValue(AppClaimTypes.CompanyCode) ?? string.Empty;
    }

    /// <summary>True when the Super Administrator works in a company they are not a member of.</summary>
    public static bool IsPlatformAccessSession(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.IsInRole(Accounts.AccountService.PlatformAdministratorRole);
    }
}
