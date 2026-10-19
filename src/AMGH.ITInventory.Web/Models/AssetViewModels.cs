using AMGH.ITInventory.Application.Assets;
using AMGH.ITInventory.Application.Common;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Web.Models;

public sealed class AssetIndexViewModel
{
    public required AssetSearchCriteria Criteria { get; init; }
    public required PagedResult<AssetListItem> Results { get; init; }
    public required AssetFormLookups Lookups { get; init; }
    public bool CanCreate { get; init; }
    public bool CanImport { get; init; }
    public bool CanExport { get; init; }
    public bool CanPrintLabels { get; init; }
    public IReadOnlyList<AssetColumn> Columns { get; init; } = AssetColumns.Parse(null);
    public IReadOnlyList<AMGH.ITInventory.Application.Views.SavedViewItem> Views { get; init; } = Array.Empty<AMGH.ITInventory.Application.Views.SavedViewItem>();
    public string QueryString { get; init; } = string.Empty;
    public bool HasAdvancedFilter => Criteria.ManufacturerId is not null || Criteria.ClassificationId is not null
        || Criteria.CriticalityId is not null || Criteria.OnlyUnassigned || Criteria.IncludeDisposed;
}

public sealed class AssetFormViewModel
{
    public required AssetEditModel Asset { get; init; }
    public required AssetFormLookups Lookups { get; init; }
    public required AssetAccess Access { get; init; }
    public bool IsEdit => Asset.AssetId > 0;
}

public sealed class AssetLifecycleViewModel
{
    public IReadOnlyList<AMGH.ITInventory.Application.Lifecycle.MovementItem>? Movements { get; init; }
    public IReadOnlyList<AMGH.ITInventory.Application.Lifecycle.MaintenanceItem>? Maintenance { get; init; }
    public IReadOnlyList<AMGH.ITInventory.Application.Lifecycle.WarrantyItem>? Warranties { get; init; }
    public IReadOnlyList<AMGH.ITInventory.Application.Lifecycle.LicenseAllocationItem>? Licences { get; init; }
    public IReadOnlyList<AMGH.ITInventory.Application.Lifecycle.DisposalItem>? Disposals { get; init; }
    public bool CanMove { get; init; }
    public bool CanMaintain { get; init; }
    public bool CanWarranty { get; init; }
    public bool CanVerify { get; init; }
    public bool CanDispose { get; init; }
    public bool AnyAction => CanMove || CanMaintain || CanWarranty || CanVerify || CanDispose;
}

public sealed class AssetDetailsViewModel
{
    public AssetLifecycleViewModel Lifecycle { get; init; } = new();
    public required AssetDetails Asset { get; init; }
    public required AssetAccess Access { get; init; }
    public IReadOnlyList<AssetHistoryEntry> History { get; init; } = Array.Empty<AssetHistoryEntry>();
    public IReadOnlyList<AssetDocumentItem>? Documents { get; init; }
    public IReadOnlyList<LookupOption> Classifications { get; init; } = Array.Empty<LookupOption>();
    public bool CanEdit { get; init; }
    public bool CanDelete { get; init; }
    public bool CanUpload { get; init; }
    public bool CanDeleteDocuments { get; init; }
    public string AllowedExtensions { get; init; } = string.Empty;
    public IReadOnlyList<AMGH.ITInventory.Application.Assignments.AssignmentHistoryItem>? Assignments { get; init; }
    public IReadOnlyList<AMGH.ITInventory.Application.Labels.LabelHistoryEntry> LabelHistory { get; init; } = Array.Empty<AMGH.ITInventory.Application.Labels.LabelHistoryEntry>();
    public bool CanAssign { get; init; }
    public bool CanReturn { get; init; }
    public bool CanPrintLabel { get; init; }
    public AMGH.ITInventory.Application.Assignments.AssignmentHistoryItem? CurrentAssignment => Assignments?.FirstOrDefault(a => a.IsCurrent);
}

public sealed class AssetImportViewModel
{
    public ImportPreview? Preview { get; init; }
    public IReadOnlyList<CreatedAsset>? Created { get; init; }
    public IReadOnlyList<Error> CommitErrors { get; init; } = Array.Empty<Error>();
    public int MaxRows { get; init; }
}
