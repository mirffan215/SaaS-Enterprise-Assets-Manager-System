namespace AMGH.ITInventory.Shared;

/// <summary>
/// The application's user-facing name. Only what users see uses these values; namespaces, assemblies, routes,
/// tables and columns keep their technical names for compatibility. Each customer company's own name and logo
/// come from its tenant database; these neutral values are what is shown when no company context is loaded.
/// </summary>
public static class AppInfo
{
    public const string Name = "Cloud Asset & IT Inventory";
    public const string ShortName = "IT Inventory Cloud";
    /// <summary>Product vendor shown in footers and e-mails when no company name is available.</summary>
    public const string Owner = "IT Inventory Cloud";
    public const string Tagline = "Track • Manage • Maintain • Optimize";
    public const string Version = "2.14";
}
