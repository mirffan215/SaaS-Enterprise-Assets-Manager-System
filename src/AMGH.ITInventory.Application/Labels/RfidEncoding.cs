using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AMGH.ITInventory.Application.Labels;

/// <summary>An asset's RFID tag: the EPC to write to it and the address its QR code opens.</summary>
public sealed record RfidTag(string AssetNumber, string AssetName, string Epc, string Url);

/// <summary>
/// The EPC written to an asset's RFID tag and the files that encode it. The EPC is 96 bits (24 hex characters, the size of UHF Gen2 EPC memory):
/// <c>A5</c> (this system's header) + company (6 hex) + asset (8 hex) + 8 random hex, so it is unique and not guessable from the neighbouring tag.
/// It is not a GS1 code: nothing outside this system needs to decode it, the scanner just reads the tag and the asset is found by it (/r/EPC).
/// </summary>
public static class RfidEncoding
{
    public const int EpcLength = 24;
    private static readonly System.Buffers.SearchValues<char> CsvSpecial = System.Buffers.SearchValues.Create(",\"\r\n");

    public static string NewEpc(int companyId, int assetId) =>
        "A5" + companyId.ToString("X6", CultureInfo.InvariantCulture) + assetId.ToString("X8", CultureInfo.InvariantCulture) + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));

    public static bool IsEpc(string? text) => text is { Length: EpcLength } && text.All(char.IsAsciiHexDigit);

    /// <summary>For RFID printers' encoding software and spreadsheets: one row per tag.</summary>
    public static string Csv(IEnumerable<RfidTag> tags)
    {
        var sb = new StringBuilder("AssetNumber,AssetName,EPC,Url\r\n");
        foreach (var t in tags) sb.Append(Field(t.AssetNumber)).Append(',').Append(Field(t.AssetName)).Append(',').Append(t.Epc).Append(',').Append(Field(t.Url)).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// One ZPL label per tag for Zebra RFID printers: writes the EPC into the tag (^RFW) and prints the asset number, the EPC and a QR code that opens the
    /// asset. Media size and print position depend on the printer; set them in the printer (or add ^PW / ^LL) before the first run.
    /// </summary>
    public static string Zpl(IEnumerable<RfidTag> tags)
    {
        var sb = new StringBuilder();
        foreach (var t in tags)
        {
            sb.Append("^XA\n^RFW,H,2,12,1^FD").Append(t.Epc).Append("^FS\n")
              .Append("^FO30,25^A0N,34,34^FD").Append(Zpl(t.AssetNumber)).Append("^FS\n")
              .Append("^FO30,70^A0N,22,22^FD").Append(Zpl(Shorten(t.AssetName, 32))).Append("^FS\n")
              .Append("^FO30,100^A0N,18,18^FDEPC ").Append(t.Epc).Append("^FS\n")
              .Append("^FO30,130^BQN,2,4^FDQA,").Append(Zpl(t.Url)).Append("^FS\n^XZ\n");
        }
        return sb.ToString();
    }

    private static string Field(string text) => text.AsSpan().IndexOfAny(CsvSpecial) < 0 ? text : "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>^ and ~ start commands in ZPL: they must not appear inside field text.</summary>
    private static string Zpl(string text) => text.Replace('^', ' ').Replace('~', ' ');
}
