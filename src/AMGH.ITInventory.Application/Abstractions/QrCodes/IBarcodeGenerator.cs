namespace AMGH.ITInventory.Application.Abstractions.QrCodes;

/// <summary>A Code 128 barcode as SVG; <see cref="Modules"/> is the total width including quiet zones.</summary>
public sealed record BarcodeImage(string Svg, int Modules);

public interface IBarcodeGenerator
{
    /// <summary>Encodes printable ASCII text (e.g. an asset number) as a Code 128 barcode.</summary>
    BarcodeImage Generate(string text);
}
