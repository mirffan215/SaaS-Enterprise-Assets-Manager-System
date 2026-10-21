using Microsoft.AspNetCore.DataProtection;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>
/// A short operator session for the operator page: after the operator key has been entered once, a protected cookie (30 minutes) lets
/// the operator use the page without sending the key with every form. The cookie holds only its own expiry, encrypted by Data Protection.
/// </summary>
public sealed class OperatorSession
{
    internal const string CookieName = "__Host-EAMS.Operator";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly IDataProtector _protector;

    public OperatorSession(IDataProtectionProvider provider) => _protector = provider.CreateProtector("EAMS.OperatorSession.v1");

    public void Start(HttpContext context, DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var expires = (nowUtc ?? DateTime.UtcNow).Add(Lifetime);
        context.Response.Cookies.Append(CookieName, _protector.Protect(expires.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true });
    }

    public bool IsActive(HttpContext context, DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Request.Cookies[CookieName] is not { Length: > 0 } cookie) return false;
        try
        {
            return long.TryParse(_protector.Unprotect(cookie), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var ticks)
                   && ticks > (nowUtc ?? DateTime.UtcNow).Ticks;
        }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
    }

    public static void End(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.Cookies.Delete(CookieName, new CookieOptions { Secure = true, Path = "/" });
    }
}
