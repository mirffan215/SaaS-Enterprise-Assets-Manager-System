using System.Security.Cryptography;

namespace AMGH.ITInventory.Security.Passwords;

/// <summary>Cryptographically random temporary passwords that always satisfy the password policy.</summary>
public static class TemporaryPasswordGenerator
{
    // Ambiguous characters (0/O, 1/l/I) are excluded so passwords can be read out safely.
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!#%*+-=?@^_";

    public static string Generate(int length = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 12);
        var all = Upper + Lower + Digits + Symbols;
        var chars = new char[length];
        chars[0] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        chars[1] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        chars[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        chars[3] = Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)];
        for (var i = 4; i < length; i++) chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }
}
