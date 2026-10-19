namespace AMGH.ITInventory.Application.Abstractions.QrCodes;

/// <summary>A generated QR code: SVG for printing (sharp at any size) and PNG for storage and screen use.</summary>
public sealed record QrImage(string Svg, byte[] Png, int Modules);

public interface IQrCodeGenerator
{
    /// <param name="payload">Text to encode (UTF-8).</param>
    /// <param name="quietZoneModules">White border in modules; 4 is the standard, 2 is acceptable on small labels.</param>
    QrImage Generate(string payload, int quietZoneModules = 2);
}
