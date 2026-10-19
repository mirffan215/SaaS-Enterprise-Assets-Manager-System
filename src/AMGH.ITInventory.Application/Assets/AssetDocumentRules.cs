namespace AMGH.ITInventory.Application.Assets;

/// <summary>Public view of the attachment rules, for the upload form.</summary>
public static class AssetDocumentRules
{
    public static IReadOnlyCollection<string> AllowedExtensions => AssetDocumentService.AllowedExtensions;
    public const long MaxBytes = AssetDocumentService.MaxBytes;
}
