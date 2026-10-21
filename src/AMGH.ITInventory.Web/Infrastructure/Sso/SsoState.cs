using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace AMGH.ITInventory.Web.Infrastructure.Sso;

public sealed record SsoStateData(string State, string Nonce, string Verifier, string? TenantCode, string CompanyCode, string? ReturnUrl);

/// <summary>What must survive the round trip to the identity provider: a sealed cookie, ten minutes, usable by the callback only.</summary>
public sealed class SsoState
{
    public const string CookieName = "__Host-EAMS.Sso";
    private readonly ITimeLimitedDataProtector _protector;

    public SsoState(IDataProtectionProvider provider) => _protector = provider.CreateProtector("EAMS.Sso.v1").ToTimeLimitedDataProtector();

    // Lax: the provider sends the browser back with a normal link (a cross-site navigation), which a Strict cookie would not accompany
    public void Start(HttpContext context, SsoStateData data) =>
        context.Response.Cookies.Append(CookieName, _protector.Protect(JsonSerializer.Serialize(data), TimeSpan.FromMinutes(10)),
            new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, IsEssential = true, Path = "/", MaxAge = TimeSpan.FromMinutes(10) });

    public SsoStateData? Take(HttpContext context)
    {
        var found = context.Request.Cookies.TryGetValue(CookieName, out var value);
        context.Response.Cookies.Delete(CookieName, new CookieOptions { Secure = true, Path = "/" });
        if (!found || string.IsNullOrEmpty(value)) return null;
        try { return JsonSerializer.Deserialize<SsoStateData>(_protector.Unprotect(value)); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException) { return null; }
    }
}
