using System.Net;

namespace AMGH.ITInventory.Security.Platform;

/// <summary>An IP allow-list: single addresses and CIDR ranges (IPv4 and IPv6), separated by commas, spaces or new lines.</summary>
public static class IpRules
{
    public const int MaxLength = 1000;

    internal static readonly char[] Separators = { ',', ';', ' ', '\r', '\n', '\t' };

    /// <summary>The entries of the list, or the first entry that is not an address or range.</summary>
    public static (IReadOnlyList<(IPAddress Network, int Bits)> Rules, string? BadEntry) Parse(string? text)
    {
        var rules = new List<(IPAddress, int)>();
        foreach (var entry in (text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('/');
            if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out var address)) return (rules, entry);
            // .NET accepts shorthand such as 1.2.3 (= 1.2.0.3) and leading zeros: a typo must not become a different address
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !string.Equals(address.ToString(), parts[0], StringComparison.Ordinal)) return (rules, entry);
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            var max = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
            var bits = max;
            if (parts.Length == 2 && (!int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out bits) || bits < 0 || bits > max)) return (rules, entry);
            rules.Add((address, bits));
        }
        return (rules, null);
    }

    /// <summary>True when the list is empty (anywhere) or the address is inside one of its entries. An unknown address is refused when a list exists.</summary>
    public static bool Allows(string? ipAddress, string? ranges)
    {
        if (string.IsNullOrWhiteSpace(ranges)) return true;
        var (rules, bad) = Parse(ranges);
        if (bad is not null || rules.Count == 0) return true; // a damaged list never locks everybody out: the settings page refuses to save one
        if (!IPAddress.TryParse(ipAddress, out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return rules.Any(r => Contains(r.Network, r.Bits, ip));
    }

    private static bool Contains(IPAddress network, int bits, IPAddress ip)
    {
        if (network.AddressFamily != ip.AddressFamily) return false;
        var a = network.GetAddressBytes();
        var b = ip.GetAddressBytes();
        var full = bits / 8;
        for (var i = 0; i < full; i++) if (a[i] != b[i]) return false;
        var rest = bits % 8;
        if (rest == 0) return true;
        var mask = (byte)(0xFF << (8 - rest));
        return (a[full] & mask) == (b[full] & mask);
    }
}
