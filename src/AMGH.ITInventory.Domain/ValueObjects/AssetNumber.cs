using System.Globalization;
using System.Text.RegularExpressions;

namespace AMGH.ITInventory.Domain.ValueObjects;

/// <summary>
/// Asset / license number such as <c>AMGH-IT-LAP-00001</c>. Numbers are issued only by
/// dbo.usp_AssetNumber_Generate; this type formats and validates them identically to
/// dbo.fn_FormatAssetNumber.
/// </summary>
public sealed partial record AssetNumber
{
    [GeneratedRegex(@"^(?<prefix>[A-Z]+-[A-Z]+)-(?<code>[A-Z]{2,10})-(?<num>\d{3,})$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public string Prefix { get; }
    public string SequenceCode { get; }
    public long Number { get; }
    public string Value { get; }

    private AssetNumber(string prefix, string sequenceCode, long number, string value)
    {
        Prefix = prefix;
        SequenceCode = sequenceCode;
        Number = number;
        Value = value;
    }

    public static AssetNumber Create(string prefix, string sequenceCode, long number, int paddingLength = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceCode);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        ArgumentOutOfRangeException.ThrowIfLessThan(paddingLength, 3);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(paddingLength, 10);

        var digits = number.ToString(CultureInfo.InvariantCulture).PadLeft(paddingLength, '0');
        return new AssetNumber(prefix, sequenceCode, number, $"{prefix}-{sequenceCode}-{digits}");
    }

    public static bool TryParse(string? value, out AssetNumber? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var m = Pattern().Match(value.Trim());
        if (!m.Success || !long.TryParse(m.Groups["num"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n <= 0)
            return false;
        result = new AssetNumber(m.Groups["prefix"].Value, m.Groups["code"].Value, n, value.Trim());
        return true;
    }

    public override string ToString() => Value;
}
