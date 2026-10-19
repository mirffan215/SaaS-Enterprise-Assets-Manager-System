using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Common;
using AMGH.ITInventory.Domain.Constants;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Errors;
using AMGH.ITInventory.Shared.Results;
using FluentValidation;

namespace AMGH.ITInventory.Application.Assets;

public interface IAssetService
{
    Task<PagedResult<AssetListItem>> SearchAsync(AssetSearchCriteria criteria, CancellationToken cancellationToken = default);
    Task<AssetDetails?> GetDetailsAsync(int assetId, AssetAccess access, CancellationToken cancellationToken = default);
    Task<AssetEditModel> NewAsync(CancellationToken cancellationToken = default);
    Task<AssetEditModel?> GetForEditAsync(int assetId, AssetAccess access, CancellationToken cancellationToken = default);
    Task<Result<CreatedAsset>> CreateAsync(AssetEditModel model, AssetAccess access, CancellationToken cancellationToken = default);
    Task<Result> UpdateAsync(AssetEditModel model, AssetAccess access, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(int assetId, string rowVersion, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssetHistoryEntry>> GetHistoryAsync(int assetId, int maxRows = 200, CancellationToken cancellationToken = default);

    /// <summary>Number, name and status for the heading of lifecycle forms (null when unknown).</summary>
    Task<AssetHeader?> HeaderAsync(int assetId, CancellationToken cancellationToken = default);

    /// <summary>Resolves a scanned label's asset number to the asset id (null when unknown or deleted).</summary>
    Task<int?> FindIdByNumberAsync(string assetNumber, CancellationToken cancellationToken = default);

    /// <summary>Database-backed checks (codes, hierarchy, uniqueness). Also used by the Excel import.</summary>
    Task<IReadOnlyList<Error>> ValidateReferencesAsync(AssetEditModel model, bool isNew, string? currentStatusCode, CancellationToken cancellationToken = default);
}

internal sealed class AssetService : IAssetService
{
    private static readonly int[] PageSizes = { 10, 25, 50, 100 };

    private readonly IUnitOfWork _uow;
    private readonly IAssetNumberGenerator _numbers;
    private readonly IValidator<AssetEditModel> _validator;
    private readonly IDateTimeProvider _clock;

    public AssetService(IUnitOfWork uow, IAssetNumberGenerator numbers, IValidator<AssetEditModel> validator, IDateTimeProvider clock)
    {
        _uow = uow;
        _numbers = numbers;
        _validator = validator;
        _clock = clock;
    }

    // ------------------------------------------------------------------ queries

    public async Task<PagedResult<AssetListItem>> SearchAsync(AssetSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var pageSize = PageSizes.Contains(criteria.PageSize) ? criteria.PageSize : 25;
        var repo = _uow.Repository<Asset>();
        var total = await repo.QueryCountAsync(q => AssetQueries.Filter(q, criteria), cancellationToken);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var page = Math.Clamp(criteria.Page, 1, pages);

        var items = await repo.QueryAsync(q => AssetQueries.Sort(AssetQueries.Filter(q, criteria), criteria.Sort, criteria.Descending)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(AssetQueries.ToListItem), cancellationToken);
        return new PagedResult<AssetListItem>(items, total, page, pageSize);
    }

    public async Task<AssetDetails?> GetDetailsAsync(int assetId, AssetAccess access, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        var rows = await _uow.Repository<Asset>().QueryAsync(q => q.Where(a => a.AssetId == assetId).Select(a => new
        {
            Asset = a,
            Category = a.AssetCategory.CategoryName,
            SubCategory = a.AssetSubCategory == null ? null : a.AssetSubCategory.SubCategoryName,
            a.AssetStatus.StatusCode, a.AssetStatus.StatusName, a.AssetStatus.ColorHex,
            Manufacturer = a.Manufacturer == null ? null : a.Manufacturer.ManufacturerName,
            Vendor = a.Vendor == null ? null : a.Vendor.VendorName,
            Currency = a.Currency == null ? null : a.Currency.CurrencyCode,
            AssignedTo = a.AssignedEmployee == null ? null : a.AssignedEmployee.FirstName + " " + a.AssignedEmployee.LastName,
            AssignedNo = a.AssignedEmployee == null ? null : a.AssignedEmployee.EmployeeNumber,
            Department = a.Department == null ? null : a.Department.DepartmentName,
            Location = a.Location == null ? null : a.Location.LocationName,
            Building = a.Building == null ? null : a.Building.BuildingName,
            Floor = a.Floor == null ? null : a.Floor.FloorName,
            Room = a.Room == null ? null : a.Room.RoomName,
            Owner = a.AssetOwner == null ? null : a.AssetOwner.FirstName + " " + a.AssetOwner.LastName,
            Classification = a.Classification == null ? null : a.Classification.ClassificationName,
            Criticality = a.Criticality == null ? null : a.Criticality.CriticalityName,
            Rto = a.Criticality == null ? null : a.Criticality.RtoHours,
            Rpo = a.Criticality == null ? null : a.Criticality.RpoHours,
            Risk = a.RiskLevel == null ? null : a.RiskLevel.RiskLevelName,
            WarrantyEnd = a.Warranties.Where(w => !w.IsDeleted).Max(w => (DateOnly?)w.EndDate)
        }), cancellationToken);
        if (rows.Count == 0) return null;

        var r = rows[0];
        var a = r.Asset;
        var fin = access.CanViewFinancials;
        var sec = access.CanViewSecurityDetails;
        return new AssetDetails(
            a.AssetId, a.AssetNumber, a.AssetName, a.Description,
            r.Category, r.SubCategory, r.StatusCode, r.StatusName, r.ColorHex,
            r.Manufacturer, a.ModelName, a.SerialNumber, a.LegacyAssetTag,
            a.Hostname, a.IpAddress, a.MacAddress, a.OperatingSystem, a.OsVersion, a.Processor,
            a.RamGb, a.StorageGb, a.FirmwareVersion, a.ImeiNumber, a.PhoneNumber,
            fin ? r.Vendor : null, fin ? a.PurchaseOrderNumber : null, fin ? a.InvoiceNumber : null, a.PurchaseDate,
            fin ? a.PurchaseCost : null, fin ? r.Currency : null, fin ? a.UsefulLifeMonths : null, fin ? a.SalvageValue : null,
            fin ? AssetRules.BookValue(a.PurchaseCost, a.SalvageValue, a.UsefulLifeMonths, a.PurchaseDate, _clock.UtcToday) : null,
            r.AssignedTo, r.AssignedNo, r.Department, r.Location, r.Building, r.Floor, r.Room,
            sec ? r.Owner : null, sec ? r.Classification : null, sec ? r.Criticality : null, sec ? r.Rto : null, sec ? r.Rpo : null, sec ? r.Risk : null,
            sec && a.StoresSensitiveData, sec && a.IsDiskEncrypted, sec && a.EndpointProtectionInstalled, sec && a.IsMdmManaged, sec && a.BackupRequired,
            sec ? a.LastSecurityReviewDate : null, sec ? a.SecurityNotes : null,
            a.CommissionDate, a.EndOfSupportDate, a.EndOfLifeDate, a.NextAuditDueDate, a.DecommissionDate,
            a.Notes, r.WarrantyEnd,
            a.CreatedBy, a.CreatedDate, a.ModifiedBy, a.ModifiedDate, Convert.ToBase64String(a.RowVersion),
            await FixedAssetInfoAsync(a, fin, cancellationToken));
    }

    public async Task<AssetEditModel> NewAsync(CancellationToken cancellationToken = default)
    {
        var inStock = await _uow.Repository<AssetStatus>().QueryAsync(q => q
            .Where(s => s.StatusCode == AssetStatusCodes.InStock).Select(s => s.AssetStatusId), cancellationToken);
        var baseCurrency = await _uow.Repository<Currency>().QueryAsync(q => q
            .Where(c => c.IsBaseCurrency && !c.IsDeleted).Select(c => c.CurrencyId), cancellationToken);
        return new AssetEditModel
        {
            AssetStatusId = inStock.FirstOrDefault(),
            CurrencyId = baseCurrency.Count > 0 ? baseCurrency[0] : null,
            NextAuditDueDate = _clock.UtcToday.AddMonths(AssetRules.DefaultAuditIntervalMonths)
        };
    }

    public async Task<AssetEditModel?> GetForEditAsync(int assetId, AssetAccess access, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        var rows = await _uow.Repository<Asset>().QueryAsync(q => q.Where(a => a.AssetId == assetId)
            .Select(a => new { Asset = a, a.AssetStatus.StatusCode }), cancellationToken);
        if (rows.Count == 0) return null;
        var model = AssetMapper.ToEditModel(rows[0].Asset, access);
        model.CurrentStatusCode = rows[0].StatusCode;
        model.StatusLocked = AssetRules.IsWorkflowStatus(rows[0].StatusCode);
        return model;
    }

    public async Task<IReadOnlyList<AssetHistoryEntry>> GetHistoryAsync(int assetId, int maxRows = 200, CancellationToken cancellationToken = default) =>
        await _uow.Repository<AssetHistory>().QueryAsync(q => q.Where(h => h.AssetId == assetId)
            .OrderByDescending(h => h.AssetHistoryId).Take(Math.Clamp(maxRows, 1, 1000))
            .Select(h => new AssetHistoryEntry(h.AssetHistoryId, h.EventDate, h.EventType.ToString(), h.FieldName,
                h.OldValue, h.NewValue, h.Remarks, h.PerformedBy)), cancellationToken);

    public async Task<AssetHeader?> HeaderAsync(int assetId, CancellationToken cancellationToken = default) =>
        (await _uow.Repository<Asset>().QueryAsync(q => q.Where(a => a.AssetId == assetId).Select(a => new AssetHeader(a.AssetId, a.AssetNumber,
            a.AssetName, a.AssetStatus.StatusCode, a.AssetStatus.StatusName,
            a.Location == null ? null : a.Location.LocationName, a.AssignedEmployee == null ? null : a.AssignedEmployee.FirstName + " " + a.AssignedEmployee.LastName)),
            cancellationToken)).FirstOrDefault();

    public async Task<int?> FindIdByNumberAsync(string assetNumber, CancellationToken cancellationToken = default)
    {
        var number = (assetNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (number.Length is 0 or > 30) return null;
        var ids = await _uow.Repository<Asset>().QueryAsync(q => q.Where(a => a.AssetNumber == number).Select(a => a.AssetId), cancellationToken);
        return ids.Count == 1 ? ids[0] : null;
    }

    // ------------------------------------------------------------------ commands

    public async Task<Result<CreatedAsset>> CreateAsync(AssetEditModel model, AssetAccess access, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(access);
        AssetRules.Normalize(model);
        AssetMapper.ClearRestricted(model, access);

        var errors = await ValidateAllAsync(model, isNew: true, currentStatusCode: null, cancellationToken);
        if (errors.Count > 0) return Result<CreatedAsset>.Failure(errors.ToArray());

        var sequenceCode = await GetSequenceCodeAsync(model.AssetCategoryId, cancellationToken);
        try
        {
            var created = await _uow.ExecuteInTransactionAsync(async ct =>
            {
                // Generated inside the transaction: if the insert fails, the number is not consumed.
                var number = await _numbers.NextAsync(sequenceCode, ct);
                var asset = new Asset { AssetNumber = number };
                AssetMapper.Apply(model, asset, access, isNew: true);
                asset.NextAuditDueDate ??= _clock.UtcToday.AddMonths(AssetRules.DefaultAuditIntervalMonths);
                await _uow.Repository<Asset>().AddAsync(asset, ct);
                await _uow.SaveChangesAsync(ct);
                return new CreatedAsset(asset.AssetId, number);
            }, cancellationToken);
            return Result<CreatedAsset>.Success(created);
        }
        catch (Exception ex) when (ToError(ex) is { } error)
        {
            return Result<CreatedAsset>.Failure(error);
        }
    }

    public async Task<Result> UpdateAsync(AssetEditModel model, AssetAccess access, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(access);
        AssetRules.Normalize(model);
        AssetMapper.ClearRestricted(model, access); // never validated nor written without permission

        var repo = _uow.Repository<Asset>();
        var asset = await repo.FirstOrDefaultAsync(a => a.AssetId == model.AssetId, cancellationToken);
        if (asset is null) return Result.Failure(Error.NotFound("This asset no longer exists."));

        var status = await _uow.Repository<AssetStatus>().QueryAsync(q => q
            .Where(s => s.AssetStatusId == asset.AssetStatusId).Select(s => s.StatusCode), cancellationToken);
        var currentCode = status[0];
        if (currentCode == AssetStatusCodes.Disposed)
            return Result.Failure(Error.BusinessRule("Disposed assets are kept as a read-only record and cannot be edited."));
        if (AssetRules.IsWorkflowStatus(currentCode)) model.AssetStatusId = asset.AssetStatusId; // locked by workflow

        if (!TryRowVersion(model.RowVersion, out var rowVersion))
            return Result.Failure(Error.Validation("The form is out of date. Reload the asset and try again."));

        var errors = await ValidateAllAsync(model, isNew: false, currentCode, cancellationToken);
        if (errors.Count > 0) return Result.Failure(errors.ToArray());

        repo.SetOriginalRowVersion(asset, rowVersion);
        AssetMapper.Apply(model, asset, access, isNew: false);
        try
        {
            await _uow.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ToError(ex) is { } error)
        {
            return Result.Failure(error);
        }
    }

    public async Task<Result> DeleteAsync(int assetId, string rowVersion, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<Asset>();
        var asset = await repo.FirstOrDefaultAsync(a => a.AssetId == assetId, cancellationToken);
        if (asset is null) return Result.Failure(Error.NotFound("This asset no longer exists."));
        if (!TryRowVersion(rowVersion, out var rv))
            return Result.Failure(Error.Validation("The page is out of date. Reload the asset and try again."));

        var code = (await _uow.Repository<AssetStatus>().QueryAsync(q => q
            .Where(s => s.AssetStatusId == asset.AssetStatusId).Select(s => s.StatusCode), cancellationToken))[0];
        if (code is AssetStatusCodes.Assigned or AssetStatusCodes.PendingDisposal or AssetStatusCodes.Disposed)
            return Result.Failure(Error.BusinessRule(code == AssetStatusCodes.Assigned
                ? "Return the asset from its custodian before deleting it."
                : "Assets in the disposal process are retired through the disposal workflow, not deleted."));

        repo.SetOriginalRowVersion(asset, rv);
        repo.Remove(asset); // soft delete; the asset number stays reserved forever
        try
        {
            await _uow.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ToError(ex) is { } error)
        {
            return Result.Failure(error);
        }
    }

    // ------------------------------------------------------------------ validation

    private async Task<List<Error>> ValidateAllAsync(AssetEditModel model, bool isNew, string? currentStatusCode, CancellationToken ct)
    {
        var result = await _validator.ValidateAsync(model, ct);
        var errors = result.Errors.Select(e => Error.Validation(e.ErrorMessage, e.PropertyName)).ToList();
        if (errors.Count == 0) errors.AddRange(await ValidateReferencesAsync(model, isNew, currentStatusCode, ct));
        return errors;
    }

    public async Task<IReadOnlyList<Error>> ValidateReferencesAsync(AssetEditModel m, bool isNew, string? currentStatusCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(m);
        var ct = cancellationToken;
        var errors = new List<Error>();

        var category = await _uow.Repository<AssetCategory>().QueryAsync(q => q
            .Where(c => c.AssetCategoryId == m.AssetCategoryId && !c.IsDeleted && c.IsActive).Select(c => c.AssetCategoryId), ct);
        if (category.Count == 0) errors.Add(Error.Validation("Select a valid, active category.", nameof(m.AssetCategoryId)));

        if (m.AssetSubCategoryId is { } sub && !await _uow.Repository<AssetSubCategory>().AnyAsync(
                s => s.AssetSubCategoryId == sub && s.AssetCategoryId == m.AssetCategoryId && !s.IsDeleted, ct))
            errors.Add(Error.Validation("The sub-category does not belong to the selected category.", nameof(m.AssetSubCategoryId)));

        var statusCode = (await _uow.Repository<AssetStatus>().QueryAsync(q => q
            .Where(s => s.AssetStatusId == m.AssetStatusId && !s.IsDeleted).Select(s => s.StatusCode), ct)).FirstOrDefault();
        var statusChanged = statusCode is not null && !string.Equals(statusCode, currentStatusCode, StringComparison.OrdinalIgnoreCase);
        if (statusCode is null)
            errors.Add(Error.Validation("Select a valid status.", nameof(m.AssetStatusId)));
        else if (isNew && !AssetRules.InitialStatuses.Contains(statusCode))
            errors.Add(Error.Validation("A new asset can only be In stock, Reserved, Under maintenance or Retired. " +
                                        "Use Assign to give it to someone.", nameof(m.AssetStatusId)));
        else if (!isNew && statusChanged && !AssetRules.ManualStatuses.Contains(statusCode))
            errors.Add(Error.Validation("That status is set by its workflow (assignment or disposal), not on this form.", nameof(m.AssetStatusId)));

        if (m.BuildingId is { } b && !await _uow.Repository<Building>().AnyAsync(x => x.BuildingId == b && x.LocationId == m.LocationId, ct))
            errors.Add(Error.Validation("The building is not at the selected location.", nameof(m.BuildingId)));
        if (m.FloorId is { } f && !await _uow.Repository<Floor>().AnyAsync(x => x.FloorId == f && x.BuildingId == m.BuildingId, ct))
            errors.Add(Error.Validation("The floor is not in the selected building.", nameof(m.FloorId)));
        if (m.RoomId is { } r && !await _uow.Repository<Room>().AnyAsync(x => x.RoomId == r && x.FloorId == m.FloorId, ct))
            errors.Add(Error.Validation("The room is not on the selected floor.", nameof(m.RoomId)));

        var assets = _uow.Repository<Asset>();
        if (m.SerialNumber is not null && await assets.AnyAsync(a => a.SerialNumber == m.SerialNumber
                && a.ManufacturerId == m.ManufacturerId && a.AssetId != m.AssetId, ct))
            errors.Add(Error.Conflict("Another asset from this manufacturer already has this serial number.", nameof(m.SerialNumber)));
        if (!string.IsNullOrWhiteSpace(m.ErpFixedAssetId) && await assets.AnyAsync(a => a.ErpFixedAssetId == m.ErpFixedAssetId.Trim() && a.AssetId != m.AssetId, ct))
            errors.Add(Error.Conflict("Another asset of this company already has this D365 fixed asset ID.", nameof(m.ErpFixedAssetId)));
        if (m.Hostname is not null && await assets.AnyAsync(a => a.Hostname == m.Hostname && a.AssetId != m.AssetId, ct))
            errors.Add(Error.Conflict("Another asset already uses this hostname.", nameof(m.Hostname)));

        return errors;
    }

    // ------------------------------------------------------------------ helpers

    private async Task<string> GetSequenceCodeAsync(int categoryId, CancellationToken ct) =>
        (await _uow.Repository<AssetCategory>().QueryAsync(q => q.Where(c => c.AssetCategoryId == categoryId)
            .Select(c => c.AssetNumberSequence.SequenceCode), ct))[0];

    internal static bool TryRowVersion(string? value, out byte[] rowVersion)
    {
        rowVersion = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            rowVersion = Convert.FromBase64String(value);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static Error? ToError(Exception ex) => ex switch
    {
        ConcurrencyConflictException c => Error.Conflict(c.Message),
        DuplicateValueException d => Error.Conflict(d.Message),
        DataIntegrityException i => Error.Validation(i.Message),
        BusinessRuleException b => Error.BusinessRule(b.Message),
        _ => null
    };

    private async Task<FixedAssetInfo> FixedAssetInfoAsync(Asset a, bool fin, CancellationToken ct)
    {
        var maint = await _uow.Repository<AssetMaintenance>().QueryAsync(q => q.Where(m => m.AssetId == a.AssetId)
            .Select(m => new { m.MaintenanceStatus, m.CompletedDate, m.ScheduledDate, m.NextMaintenanceDate }), ct);
        var last = maint.Where(m => m.CompletedDate != null).Select(m => (DateOnly?)DateOnly.FromDateTime(m.CompletedDate!.Value)).DefaultIfEmpty().Max();
        var next = maint.Where(m => m.MaintenanceStatus == Domain.Enums.MaintenanceStatus.Scheduled).Select(m => (DateOnly?)m.ScheduledDate)
            .Concat(maint.Select(m => m.NextMaintenanceDate)).Where(d => d is not null && d >= _clock.UtcToday).DefaultIfEmpty().Min();
        var disposal = (await _uow.Repository<AssetDisposal>().QueryAsync(q => q.Where(d => d.AssetId == a.AssetId && d.DisposalStatus == Domain.Enums.WorkflowStatus.Completed)
            .Select(d => new { d.DisposalDate, d.Reason }), ct)).FirstOrDefault();
        return new FixedAssetInfo(fin ? a.CapitalizationDate : null, fin ? a.DepreciationMethod : null, fin ? a.FixedAssetGroup : null,
            fin ? a.ErpFixedAssetId : null, fin ? a.RecordedBookValue : null, fin ? a.RecordedBookValueDate : null,
            a.CostCenter, a.AssetCondition, a.MaintenanceRequired, last, next, disposal?.DisposalDate, disposal?.Reason);
    }
}
