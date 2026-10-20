using System.Security.Cryptography;
using System.Text;

namespace AMGH.ITInventory.Security.Mfa;

/// <summary>
/// Time-based one-time passwords (RFC 6238, HMAC-SHA1, 6 digits, 30-second steps): the format every authenticator app understands
/// (Microsoft Authenticator, Google Authenticator, Authy, 1Password, ...). SHA-1 is what those apps implement; it is safe in HMAC form.
/// </summary>
public static class Totp
{
    public const int StepSeconds = 30;
    public const int Digits = 6;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static string ToBase32(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5) { sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[]? FromBase32(string text)
    {
        var clean = text.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var v = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (v < 0) return null;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8) { bytes.Add((byte)(buffer >> (bits - 8))); bits -= 8; }
        }
        return bytes.ToArray();
    }

    public static long StepOf(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds() / StepSeconds;

    public static string Code(ReadOnlySpan<byte> secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
#pragma warning disable CA5350 // RFC 6238 authenticator apps use HMAC-SHA1; HMAC does not depend on SHA-1 collision resistance
        HMACSHA1.HashData(secret, counter, hash);
#pragma warning restore CA5350
        var offset = hash[19] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The time step the code belongs to when it is valid now (one step either side, for clock drift) and newer than <paramref name="lastUsedStep"/>
    /// (a code works once); null otherwise.
    /// </summary>
    public static long? Verify(ReadOnlySpan<byte> secret, string? code, DateTime nowUtc, long? lastUsedStep)
    {
        if (code is null) return null;
        var digits = new string(code.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != Digits) return null;
        var now = StepOf(nowUtc);
        long? match = null;
        for (var step = now - 1; step <= now + 1; step++)
        {
            // compare every candidate in constant time; keep the newest match
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, step)), Encoding.ASCII.GetBytes(digits)) && (lastUsedStep is null || step > lastUsedStep))
                match = step;
        }
        return match;
    }

    /// <summary>The address an authenticator app reads from the QR code.</summary>
    public static string Uri(string issuer, string account, string secretBase32) =>
        $"otpauth://totp/{System.Uri.EscapeDataString(issuer)}:{System.Uri.EscapeDataString(account)}?secret={secretBase32}&issuer={System.Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
}
