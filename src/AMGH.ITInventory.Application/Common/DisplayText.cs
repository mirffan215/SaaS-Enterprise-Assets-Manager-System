using System.Text;

namespace AMGH.ITInventory.Application.Common;

public static class DisplayText
{
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal)
    {
        ["LoginSuccess"] = "Signed in",
        ["LoginFailed"] = "Sign-in failed",
        ["Lockout"] = "Account locked",
        ["Logout"] = "Signed out",
        ["SessionExpired"] = "Session ended",
        ["AccountDisabled"] = "Disabled account sign-in",
        ["MfaEnabled"] = "Two-step verification turned on",
        ["MfaReset"] = "Two-step verification reset",
        ["RecoveryCodeUsed"] = "Recovery code used",
        ["SsoSignIn"] = "Signed in with single sign-on",
        ["QrGenerated"] = "QR code generated",
        ["Oem"] = "OEM",
        ["Size50x30"] = "50 × 30 mm",
        ["Size70x40"] = "70 × 40 mm",
        ["Size100x50"] = "100 × 50 mm",
    };

    /// <summary>"PasswordChanged" → "Password changed". Used for enum values shown to users.</summary>
    public static string Humanize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (Overrides.TryGetValue(value, out var o)) return o;
        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(value[i - 1]) || (i + 1 < value.Length && char.IsLower(value[i + 1]))))
                sb.Append(' ').Append(char.ToLowerInvariant(c));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}
