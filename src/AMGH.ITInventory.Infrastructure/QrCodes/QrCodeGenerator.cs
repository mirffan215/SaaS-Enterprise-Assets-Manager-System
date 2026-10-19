using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using AMGH.ITInventory.Application.Abstractions.QrCodes;

namespace AMGH.ITInventory.Infrastructure.QrCodes;

internal sealed class QrCodeGenerator : IQrCodeGenerator
{
    private const int PngScale = 8; // pixels per module

    public QrImage Generate(string payload, int quietZoneModules = 2)
    {
        ArgumentNullException.ThrowIfNull(payload);
        quietZoneModules = Math.Clamp(quietZoneModules, 0, 8);
        var matrix = QrEncoder.Encode(payload);
        return new QrImage(ToSvg(matrix, quietZoneModules), ToPng(matrix, quietZoneModules, PngScale), matrix.GetLength(0));
    }

    /// <summary>One path; each horizontal run of dark modules is one rectangle. Scales to any print size.</summary>
    internal static string ToSvg(bool[,] m, int border)
    {
        var size = m.GetLength(0);
        var full = size + border * 2;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {full} {full}\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"QR code\">");
        sb.Append(CultureInfo.InvariantCulture, $"<rect width=\"{full}\" height=\"{full}\" fill=\"#fff\"/><path fill=\"#000\" d=\"");
        for (var y = 0; y < size; y++)
        {
            var x = 0;
            while (x < size)
            {
                if (!m[y, x]) { x++; continue; }
                var start = x;
                while (x < size && m[y, x]) x++;
                sb.Append(CultureInfo.InvariantCulture, $"M{start + border} {y + border}h{x - start}v1h-{x - start}z");
            }
        }
        sb.Append("\"/></svg>");
        return sb.ToString();
    }

    /// <summary>1-bit greyscale PNG (no external libraries): signature, IHDR, IDAT (zlib), IEND.</summary>
    internal static byte[] ToPng(bool[,] m, int border, int scale)
    {
        var size = m.GetLength(0);
        var px = (size + border * 2) * scale;
        var stride = (px + 7) / 8;
        var raw = new byte[(stride + 1) * px];
        for (var y = 0; y < px; y++)
        {
            var row = y * (stride + 1);
            raw[row] = 0; // filter: none
            for (var x = 0; x < px; x++)
            {
                int mx = x / scale - border, my = y / scale - border;
                var dark = mx >= 0 && my >= 0 && mx < size && my < size && m[my, mx];
                if (!dark) raw[row + 1 + x / 8] |= (byte)(0x80 >> (x % 8)); // 1 = white
            }
        }

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), px);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), px);
        ihdr[8] = 1;  // bit depth
        ihdr[9] = 0;  // greyscale
        WriteChunk(png, "IHDR", ihdr);
        using (var compressed = new MemoryStream())
        {
            using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);
            WriteChunk(png, "IDAT", compressed.ToArray());
        }
        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc32(typeBytes, data);
        BinaryPrimitives.WriteUInt32BigEndian(len, crc);
        s.Write(len);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
