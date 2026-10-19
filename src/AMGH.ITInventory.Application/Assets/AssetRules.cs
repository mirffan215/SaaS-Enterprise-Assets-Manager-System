using AMGH.ITInventory.Domain.Constants;

namespace AMGH.ITInventory.Application.Assets;

/// <summary>Lifecycle rules shared by the asset form and the Excel import.</summary>
public static class AssetRules
{
    /// <summary>Statuses a user may set directly. Others are set only by workflows.</summary>
    public static readonly IReadOnlySet<string> ManualStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        AssetStatusCodes.InStock, AssetStatusCodes.Reserved, AssetStatusCodes.UnderMaintenance,
        AssetStatusCodes.Missing, AssetStatusCodes.Stolen, AssetStatusCodes.Retired
    };

    /// <summary>Statuses allowed for a brand-new asset (form or import).</summary>
    public static readonly IReadOnlySet<string> InitialStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        AssetStatusCodes.InStock, AssetStatusCodes.Reserved, AssetStatusCodes.UnderMaintenance, AssetStatusCodes.Retired
    };

    /// <summary>Statuses owned by a workflow: assignment, disposal. The form cannot change them.</summary>
    public static bool IsWorkflowStatus(string statusCode) => !ManualStatuses.Contains(statusCode);

    public const int DefaultAuditIntervalMonths = 12;

    /// <summary>Straight-line net book value; mirrors dbo.fn_GetAssetBookValue.</summary>
    public static decimal? BookValue(decimal? cost, decimal? salvage, int? usefulLifeMonths, DateOnly? purchaseDate, DateOnly asOf)
    {
        if (cost is null || usefulLifeMonths is null or <= 0 || purchaseDate is null) return cost;
        if (asOf < purchaseDate.Value) return cost;
        var months = (asOf.Year - purchaseDate.Value.Year) * 12 + asOf.Month - purchaseDate.Value.Month;
        if (asOf.Day < purchaseDate.Value.Day) months--;
        months = Math.Max(months, 0);
        var floor = salvage ?? 0m;
        if (months >= usefulLifeMonths) return floor;
        return Math.Round(cost.Value - (cost.Value - floor) * months / usefulLifeMonths.Value, 2);
    }

    /// <summary>Trims text fields, turns blanks into null and normalises MAC/hostname formatting.</summary>
    public static void Normalize(AssetEditModel m)
    {
        ArgumentNullException.ThrowIfNull(m);
        m.AssetName = (m.AssetName ?? string.Empty).Trim();
        m.ModelName = Clean(m.ModelName);
        m.SerialNumber = Clean(m.SerialNumber)?.ToUpperInvariant();
        m.LegacyAssetTag = Clean(m.LegacyAssetTag);
        m.Description = Clean(m.Description);
        m.Hostname = Clean(m.Hostname)?.ToUpperInvariant();
        m.IpAddress = Clean(m.IpAddress);
        m.MacAddress = Clean(m.MacAddress)?.ToUpperInvariant().Replace('-', ':');
        m.OperatingSystem = Clean(m.OperatingSystem);
        m.OsVersion = Clean(m.OsVersion);
        m.Processor = Clean(m.Processor);
        m.FirmwareVersion = Clean(m.FirmwareVersion);
        m.ImeiNumber = Clean(m.ImeiNumber)?.Replace(" ", string.Empty, StringComparison.Ordinal);
        m.PhoneNumber = Clean(m.PhoneNumber);
        m.PurchaseOrderNumber = Clean(m.PurchaseOrderNumber);
        m.InvoiceNumber = Clean(m.InvoiceNumber);
        m.SecurityNotes = Clean(m.SecurityNotes);
        m.Notes = Clean(m.Notes);
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
