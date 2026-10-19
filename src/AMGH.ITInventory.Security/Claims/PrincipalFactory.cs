using System.Globalization;
using System.Security.Claims;
using AMGH.ITInventory.Security.Accounts;

namespace AMGH.ITInventory.Security.Claims;

/// <summary>Builds the cookie principal for an authenticated user.</summary>
public static class PrincipalFactory
{
    public static ClaimsPrincipal Create(AuthenticatedUser user, string authenticationScheme, DateTime authTimeUtc, string? tenantCode = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Username),
            new(AppClaimTypes.DisplayName, user.DisplayName),
            new(AppClaimTypes.SecurityStamp, user.SecurityStamp.ToString("D")),
            new(AppClaimTypes.MustChangePassword, user.MustChangePassword ? "true" : "false"),
            new(AppClaimTypes.CompanyId, user.CompanyId.ToString(CultureInfo.InvariantCulture)),
            new(AppClaimTypes.CompanyCode, user.CompanyCode),
            new(AppClaimTypes.CompanyName, user.CompanyName),
            new(AppClaimTypes.PlatformAdmin, user.IsPlatformAdmin ? "true" : "false"),
            new(AppClaimTypes.AuthTime,
                new DateTimeOffset(DateTime.SpecifyKind(authTimeUtc, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
        };
        if (!string.IsNullOrEmpty(tenantCode)) claims.Add(new Claim(AppClaimTypes.Tenant, tenantCode));
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(user.Permissions.Select(p => new Claim(AppClaimTypes.Permission, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationScheme, ClaimTypes.Name, ClaimTypes.Role));
    }
}
