using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>Who is waiting at the second step of sign-in (the password was correct).</summary>
public sealed record MfaPendingState(int UserId, int CompanyId, string CompanyCode, string? TenantCode, string Username, bool Remember, string? ReturnUrl);

/// <summary>
/// The "password accepted, code still needed" state: a sealed, tamper-proof cookie that is valid for five minutes and means nothing else.
/// It is not a session: no page accepts it as a sign-in.
/// </summary>
public sealed class MfaPending
{
    public const string CookieName = "__Host-EAMS.Mfa";
    private readonly ITimeLimitedDataProtector _protector;

    public MfaPending(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("EAMS.MfaPending.v1").ToTimeLimitedDataProtector();

    public void Start(HttpContext context, MfaPendingState state) =>
        context.Response.Cookies.Append(CookieName, _protector.Protect(JsonSerializer.Serialize(state), TimeSpan.FromMinutes(5)),
            new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true, Path = "/", MaxAge = TimeSpan.FromMinutes(5) });

    public MfaPendingState? Read(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value)) return null;
        try { return JsonSerializer.Deserialize<MfaPendingState>(_protector.Unprotect(value)); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException) { return null; }
    }

    public static void Clear(HttpContext context) => context.Response.Cookies.Delete(CookieName, new CookieOptions { Secure = true, Path = "/" });
}
