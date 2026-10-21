using AMGH.ITInventory.Application.Common;

namespace AMGH.ITInventory.Application.Assets;

public sealed class AssetSearchCriteria
{
    public string? SearchText { get; set; }
    public int? CategoryId { get; set; }
    public int? StatusId { get; set; }
    public int? DepartmentId { get; set; }
    public int? LocationId { get; set; }
    public int? ManufacturerId { get; set; }
    public int? ClassificationId { get; set; }
    public int? CriticalityId { get; set; }
    public bool OnlyUnassigned { get; set; }
    public bool IncludeDisposed { get; set; }
    public int? BuildingId { get; set; }
    public int? RiskLevelId { get; set; }
    /// <summary>Dashboard drill-down: one of <see cref="AssetFocus"/> (e.g. "missing-qr").</summary>
    public string? Focus { get; set; }
    /// <summary>Reference date for warranty and verification focus filters (set by the service).</summary>
    public DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public string? Sort { get; set; }
    public bool Descending { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;

    public static readonly IReadOnlyList<string> SortColumns =
        new[] { "number", "name", "category", "status", "assignedTo", "location", "purchaseDate" };
}

/// <summary>Dashboard drill-down filters: the same definitions as the KPI cards.</summary>
public static class AssetFocus
{
    public const string Unassigned = "unassigned";
    public const string Maintenance = "maintenance";
    public const string Repair = "repair";
    public const string MissingQr = "missing-qr";
    public const string WarrantyExpired = "warranty-expired";
    public const string WarrantyExpiring = "warranty-expiring";
    public const string HighRisk = "high-risk";
    public const string Critical = "critical";
    public const string Lost = "lost";
    public const string VerificationOverdue = "verification-overdue";
    public const int ExpiringDays = 90;

    public static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Unassigned] = "Unassigned (in stock or reserved)", [Maintenance] = "Under maintenance", [Repair] = "Open repairs",
        [MissingQr] = "No QR code generated", [WarrantyExpired] = "Warranty expired", [WarrantyExpiring] = $"Warranty expiring within {ExpiringDays} days",
        [HighRisk] = "High or critical risk", [Critical] = "Critical assets", [Lost] = "Missing or stolen",
        [VerificationOverdue] = "Physical verification overdue"
    };
}

public sealed record AssetListItem(
    int AssetId,
    string AssetNumber,
    string AssetName,
    string CategoryName,
    string StatusCode,
    string StatusName,
    string? StatusColor,
    string? ManufacturerName,
    string? ModelName,
    string? SerialNumber,
    string? Hostname,
    string? AssignedTo,
    string? DepartmentName,
    string? LocationName,
    string? ClassificationName,
    DateOnly? PurchaseDate,
    string? RiskLevelName = null,
    string? CriticalityName = null,
    DateOnly? NextVerificationDue = null,
    DateOnly? WarrantyEnd = null);

/// <summary>
/// The asset form. Used for create and edit. Assignment (custodian) is never set here; it changes only
/// through the assignment workflow so that hand-over and acknowledgement are recorded.
/// </summary>
public sealed class AssetEditModel
{
    public int AssetId { get; set; }
    public string? AssetNumber { get; set; }
    public string? RowVersion { get; set; }

    // general
    public string AssetName { get; set; } = string.Empty;
    public int AssetCategoryId { get; set; }
    public int? AssetSubCategoryId { get; set; }
    public int AssetStatusId { get; set; }
    public int? ManufacturerId { get; set; }
    public string? ModelName { get; set; }
    public string? SerialNumber { get; set; }
    public string? LegacyAssetTag { get; set; }
    public string? Description { get; set; }

    // technical
    public string? Hostname { get; set; }
    public string? IpAddress { get; set; }
    public string? MacAddress { get; set; }
    public string? OperatingSystem { get; set; }
    public string? OsVersion { get; set; }
    public string? Processor { get; set; }
    public decimal? RamGb { get; set; }
    public decimal? StorageGb { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? ImeiNumber { get; set; }
    public string? PhoneNumber { get; set; }

    // procurement (Assets.ViewFinancials)
    public int? VendorId { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public decimal? PurchaseCost { get; set; }
    public int? CurrencyId { get; set; }
    public int? UsefulLifeMonths { get; set; }
    public decimal? SalvageValue { get; set; }
    // Release 2.9: fixed-asset fields
    public DateOnly? CapitalizationDate { get; set; }
    public string? DepreciationMethod { get; set; }
    public string? FixedAssetGroup { get; set; }
    public string? ErpFixedAssetId { get; set; }
    public decimal? RecordedBookValue { get; set; }
    public DateOnly? RecordedBookValueDate { get; set; }
    public string? CostCenter { get; set; }
    public string? AssetCondition { get; set; }
    public bool MaintenanceRequired { get; set; }
    public static readonly IReadOnlyList<string> DepreciationMethods = new[] { "StraightLine", "DecliningBalance", "UnitsOfProduction", "None" };
    public static readonly IReadOnlyList<string> Conditions = new[] { "New", "Good", "Fair", "Poor", "Damaged" };

    // placement
    public int? DepartmentId { get; set; }
    public int? LocationId { get; set; }
    public int? BuildingId { get; set; }
    public int? FloorId { get; set; }
    public int? RoomId { get; set; }

    // information security (Assets.ViewSecurityDetails)
    public int? AssetOwnerEmployeeId { get; set; }
    public int? ClassificationId { get; set; }
    public int? CriticalityId { get; set; }
    public int? RiskLevelId { get; set; }
    public bool StoresSensitiveData { get; set; }
    public bool IsDiskEncrypted { get; set; }
    public bool EndpointProtectionInstalled { get; set; }
    public bool IsMdmManaged { get; set; }
    public bool BackupRequired { get; set; }
    public DateOnly? LastSecurityReviewDate { get; set; }
    public string? SecurityNotes { get; set; }

    // lifecycle
    public DateOnly? CommissionDate { get; set; }
    public DateOnly? EndOfSupportDate { get; set; }
    public DateOnly? EndOfLifeDate { get; set; }
    public DateOnly? NextAuditDueDate { get; set; }
    public string? Notes { get; set; }

    // read-only context for the edit page
    public string? CurrentStatusCode { get; set; }
    public bool StatusLocked { get; set; }
}

public sealed record AssetFormLookups(
    IReadOnlyList<LookupOption> Categories,
    IReadOnlyList<LookupOption> SubCategories,
    IReadOnlyList<LookupOption> Statuses,
    IReadOnlyList<LookupOption> Manufacturers,
    IReadOnlyList<LookupOption> Vendors,
    IReadOnlyList<LookupOption> Currencies,
    IReadOnlyList<LookupOption> Departments,
    IReadOnlyList<LookupOption> Employees,
    IReadOnlyList<LookupOption> Locations,
    IReadOnlyList<LookupOption> Buildings,
    IReadOnlyList<LookupOption> Floors,
    IReadOnlyList<LookupOption> Rooms,
    IReadOnlyList<LookupOption> Classifications,
    IReadOnlyList<LookupOption> Criticalities,
    IReadOnlyList<LookupOption> RiskLevels);

public sealed record AssetDetails(
    int AssetId, string AssetNumber, string AssetName, string? Description,
    string CategoryName, string? SubCategoryName, string StatusCode, string StatusName, string? StatusColor,
    string? ManufacturerName, string? ModelName, string? SerialNumber, string? LegacyAssetTag, string? RfidEpc,
    string? Hostname, string? IpAddress, string? MacAddress, string? OperatingSystem, string? OsVersion, string? Processor,
    decimal? RamGb, decimal? StorageGb, string? FirmwareVersion, string? ImeiNumber, string? PhoneNumber,
    string? VendorName, string? PurchaseOrderNumber, string? InvoiceNumber, DateOnly? PurchaseDate, decimal? PurchaseCost,
    string? CurrencyCode, int? UsefulLifeMonths, decimal? SalvageValue, decimal? BookValue,
    string? AssignedTo, string? AssignedEmployeeNumber, string? DepartmentName,
    string? LocationName, string? BuildingName, string? FloorName, string? RoomName,
    string? OwnerName, string? ClassificationName, string? CriticalityName, int? RtoHours, int? RpoHours, string? RiskLevelName,
    bool StoresSensitiveData, bool IsDiskEncrypted, bool EndpointProtectionInstalled, bool IsMdmManaged, bool BackupRequired,
    DateOnly? LastSecurityReviewDate, string? SecurityNotes,
    DateOnly? CommissionDate, DateOnly? EndOfSupportDate, DateOnly? EndOfLifeDate, DateOnly? NextAuditDueDate, DateOnly? DecommissionDate,
    string? Notes, DateOnly? WarrantyEndDate,
    string CreatedBy, DateTime CreatedDateUtc, string? ModifiedBy, DateTime? ModifiedDateUtc, string RowVersion,
    FixedAssetInfo? FixedAsset = null, IReadOnlyList<Lifecycle.CostTotal>? MaintenanceCosts = null);

/// <summary>Release 2.9 fixed-asset fields. The financial ones are null for users without the financial permission.</summary>
public sealed record FixedAssetInfo(DateOnly? CapitalizationDate, string? DepreciationMethod, string? FixedAssetGroup, string? ErpFixedAssetId,
    decimal? RecordedBookValue, DateOnly? RecordedBookValueDate, string? CostCenter, string? Condition, bool MaintenanceRequired,
    DateOnly? LastMaintenance, DateOnly? NextMaintenance, DateOnly? DisposalDate, string? DisposalReason);

public sealed record AssetHistoryEntry(long Id, DateTime EventDateUtc, string EventType, string? FieldName, string? OldValue,
    string? NewValue, string? Remarks, string PerformedBy);

public sealed record AssetDocumentItem(int AssetDocumentId, string DocumentType, string DocumentTitle, string OriginalFileName,
    long FileSizeBytes, string? ClassificationName, string UploadedBy, DateTime UploadedDateUtc);

public sealed record CreatedAsset(int AssetId, string AssetNumber);

public sealed record AssetHeader(int AssetId, string AssetNumber, string AssetName, string StatusCode, string StatusName, string? Location, string? Custodian);
