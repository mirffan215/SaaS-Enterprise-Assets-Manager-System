using System.Globalization;
using System.Text;
using AMGH.ITInventory.Application.Abstractions.QrCodes;

namespace AMGH.ITInventory.Infrastructure.Barcodes;

/// <summary>
/// Code 128 linear barcode (ISO/IEC 15417), the standard symbology for asset tags; readable by every
/// USB/Bluetooth handheld scanner. Uses code set B for text and switches to code set C (two digits
/// per symbol) for runs of digits, which makes the AMGH-IT-xxx-00001 numbers about 5% shorter.
/// </summary>
internal static class Code128Encoder
{
    private const int StartB = 104, StartC = 105, CodeB = 100, CodeC = 99;
    private const string Stop = "1100011101011"; // stop pattern + 2-module termination bar

    // Symbol values 0-105: '1' = bar module, '0' = space module (11 modules each).
    private static readonly string[] Patterns =
    {
        "11011001100", "11001101100", "11001100110", "10010011000", "10010001100", "10001001100", "10011001000", "10011000100",
        "10001100100", "11001001000", "11001000100", "11000100100", "10110011100", "10011011100", "10011001110", "10111001100",
        "10011101100", "10011100110", "11001110010", "11001011100", "11001001110", "11011100100", "11001110100", "11101101110",
        "11101001100", "11100101100", "11100100110", "11101100100", "11100110100", "11100110010", "11011011000", "11011000110",
        "11000110110", "10100011000", "10001011000", "10001000110", "10110001000", "10001101000", "10001100010", "11010001000",
        "11000101000", "11000100010", "10110111000", "10110001110", "10001101110", "10111011000", "10111000110", "10001110110",
        "11101110110", "11010001110", "11000101110", "11011101000", "11011100010", "11011101110", "11101011000", "11101000110",
        "11100010110", "11101101000", "11101100010", "11100011010", "11101111010", "11001000010", "11110001010", "10100110000",
        "10100001100", "10010110000", "10010000110", "10000101100", "10000100110", "10110010000", "10110000100", "10011010000",
        "10011000010", "10000110100", "10000110010", "11000010010", "11001010000", "11110111010", "11000010100", "10001111010",
        "10100111100", "10010111100", "10010011110", "10111100100", "10011110100", "10011110010", "11110100100", "11110010100",
        "11110010010", "11011011110", "11011110110", "11110110110", "10101111000", "10100011110", "10001011110", "10111101000",
        "10111100010", "11110101000", "11110100010", "10111011110", "10111101110", "11101011110", "11110101110", "11010000100",
        "11010010000", "11010011100",
    };

    /// <summary>Returns the bar/space modules (true = bar), without quiet zones.</summary>
    public static bool[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || text.Length > 80) throw new ArgumentException("Barcode text must be 1-80 characters.", nameof(text));
        foreach (var ch in text)
            if (ch < 32 || ch > 126) throw new ArgumentException("Barcode text must be printable ASCII.", nameof(text));

        var values = new List<int>();
        var i = 0;
        var inC = UseC(text, 0, atStart: true);
        values.Add(inC ? StartC : StartB);
        while (i < text.Length)
        {
            if (inC)
            {
                if (i + 1 < text.Length && char.IsAsciiDigit(text[i]) && char.IsAsciiDigit(text[i + 1]))
                {
                    values.Add((text[i] - '0') * 10 + (text[i + 1] - '0'));
                    i += 2;
                    continue;
                }
                values.Add(CodeB);
                inC = false;
            }
            if (UseC(text, i, atStart: false))
            {
                values.Add(CodeC);
                inC = true;
                continue;
            }
            values.Add(text[i] - 32);
            i++;
        }

        var checksum = values[0];
        for (var k = 1; k < values.Count; k++) checksum += values[k] * k;
        values.Add(checksum % 103);

        var sb = new StringBuilder();
        foreach (var v in values) sb.Append(Patterns[v]);
        sb.Append(Stop);
        return sb.ToString().Select(c => c == '1').ToArray();
    }

    /// <summary>
    /// Switch to code set C when it saves space: at the start for 4+ digits, at the end for an even
    /// run of 4+ digits, in the middle for 6+ digits (even part only; an odd leading digit stays in B).
    /// </summary>
    private static bool UseC(string s, int at, bool atStart)
    {
        var run = 0;
        while (at + run < s.Length && char.IsAsciiDigit(s[at + run])) run++;
        var reachesEnd = at + run == s.Length;
        if (atStart) return run >= 4 && run % 2 == 0;
        if (run % 2 != 0) return false; // leave one digit in B; the rest is reconsidered next character
        return reachesEnd ? run >= 4 : run >= 6;
    }
}

/// <summary>Renders Code 128 as SVG bars (crisp at any print size).</summary>
internal sealed class BarcodeGenerator : IBarcodeGenerator
{
    public BarcodeImage Generate(string text)
    {
        var modules = Code128Encoder.Encode(text);
        const int quiet = 10; // quiet zone: 10 modules each side, required by the standard
        var width = modules.Length + quiet * 2;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {width} 40\" preserveAspectRatio=\"none\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"Barcode\">");
        sb.Append(CultureInfo.InvariantCulture, $"<rect width=\"{width}\" height=\"40\" fill=\"#fff\"/><path fill=\"#000\" d=\"");
        var x = 0;
        while (x < modules.Length)
        {
            if (!modules[x]) { x++; continue; }
            var start = x;
            while (x < modules.Length && modules[x]) x++;
            sb.Append(CultureInfo.InvariantCulture, $"M{start + quiet} 0h{x - start}v40h-{x - start}z");
        }
        sb.Append("\"/></svg>");
        return new BarcodeImage(sb.ToString(), width);
    }
}
