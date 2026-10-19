namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>Text printed in the coloured band of the barcode asset tag (appsettings.json "Tags").</summary>
public sealed class TagOptions
{
    public const string SectionName = "Tags";
    public string PropertyOf { get; set; } = "PROPERTY OF";
    public string CompanyName { get; set; } = "IT Inventory Cloud";
    public string ContactLine { get; set; } = string.Empty;
}
