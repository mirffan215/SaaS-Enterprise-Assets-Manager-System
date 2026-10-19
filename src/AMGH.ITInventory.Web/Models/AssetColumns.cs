using AMGH.ITInventory.Application.Assets;

namespace AMGH.ITInventory.Web.Models;

/// <summary>A selectable column of the asset register. <see cref="SortKey"/> is null when the column cannot be sorted.</summary>
public sealed record AssetColumn(string Key, string Header, string? SortKey, Func<AssetListItem, string?> Value, bool Small = false);

/// <summary>Columns of the asset register. Asset number and name are always shown.</summary>
public static class AssetColumns
{
    public const string CookieName = "AMGH.AssetColumns";
    private static string? D(DateOnly? d) => d?.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    public static readonly IReadOnlyList<AssetColumn> All = new AssetColumn[]
    {
        new("category", "Category", "category", a => a.CategoryName),
        new("status", "Status", "status", a => a.StatusName),
        new("assignedTo", "Assigned to", "assignedTo", a => a.AssignedTo),
        new("department", "Department", null, a => a.DepartmentName),
        new("location", "Location", "location", a => a.LocationName),
        new("serial", "Serial number", null, a => a.SerialNumber, Small: true),
        new("hostname", "Hostname", null, a => a.Hostname, Small: true),
        new("manufacturer", "Manufacturer / model", null, a => string.Join(" ", new[] { a.ManufacturerName, a.ModelName }.Where(x => x is not null)), Small: true),
        new("classification", "Classification", null, a => a.ClassificationName),
        new("risk", "Risk level", null, a => a.RiskLevelName),
        new("criticality", "Criticality", null, a => a.CriticalityName),
        new("purchaseDate", "Purchase date", "purchaseDate", a => D(a.PurchaseDate), Small: true),
        new("warrantyEnd", "Warranty end", null, a => D(a.WarrantyEnd), Small: true),
        new("nextVerification", "Next verification", null, a => D(a.NextVerificationDue), Small: true)
    };

    public static readonly IReadOnlyList<string> Defaults = new[] { "category", "status", "assignedTo", "location", "serial" };

    /// <summary>Parses "a,b,c" keeping only known keys in the registry's order; falls back to the defaults.</summary>
    public static IReadOnlyList<AssetColumn> Parse(string? csv)
    {
        var keys = (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        var chosen = All.Where(c => keys.Contains(c.Key)).ToList();
        return chosen.Count > 0 ? chosen : All.Where(c => Defaults.Contains(c.Key)).ToList();
    }
}
